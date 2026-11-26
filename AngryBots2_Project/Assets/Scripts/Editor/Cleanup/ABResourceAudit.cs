// ABResourceAudit.cs
// -----------------------------------------------------------------------------
// 只读资源审计：找出「冗余（重复）/ 无效（断引用、缺脚本）/ 过时（不可达）」
// 仅读取，不删除任何资产。
//
// 用法（batchmode）：
//   Tuanjie.exe -batchmode -projectPath <AB2> -executeMethod ABCleanup.ABResourceAudit.Run
// 产物：
//   E:\MyPlayGround\_logs\ab_resource_audit.json
//   E:\MyPlayGround\_logs\ab_resource_audit_report.md
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ABCleanup
{
    public static class ABResourceAudit
    {
        const string LogDir = @"E:\MyPlayGround\_logs";
        const string JsonPath = LogDir + @"\ab_resource_audit.json";
        const string MdPath = LogDir + @"\ab_resource_audit_report.md";

        static readonly Regex GuidRegex = new Regex(@"guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);
        static readonly Regex ScriptLineRegex = new Regex(@"m_Script:\s*\{[^\}]*guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);

        /// <summary>会内嵌 guid 文本引用的资产扩展名。</summary>
        static readonly HashSet<string> TextExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".asset", ".mat", ".controller", ".overridecontroller",
            ".anim", ".mask", ".physicmaterial", ".physicsmaterial2d", ".mixer",
            ".shadergraph", ".shadersubgraph", ".shadervariants", ".guiskin", ".fontsettings",
            ".inputactions", ".lighting", ".rendertexture", ".cubemap", ".flare",
            ".spriteatlas", ".spriteatlasv2", ".terrainlayer", ".brush", ".playable", ".signal",
            ".preset", ".vfx", ".vfxblock", ".vfxoperator", ".vfxcompiled", ".uxml", ".uss",
            ".scenetemplate", ".lightingsettings", ".volume", ".renderer2ddata",
        };

        /// <summary>不参与「未引用」判定的可执行/工具类文件。</summary>
        static readonly HashSet<string> CodeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".boo", ".dll", ".asmdef", ".asmref", ".rsp", ".shader", ".hlsl",
            ".cginc", ".compute", ".raytrace", ".meta", ".txt", ".md", ".json", ".xml", ".csv",
            ".preset",
        };

        [Serializable]
        public class BrokenRef
        {
            public string file;
            public string guid;
            public string kind;      // MissingScript / MissingAsset
            public string context;   // 该行原文（截断）
        }

        [Serializable]
        public class AssetEntry
        {
            public string path;
            public string ext;
            public long size;
            public bool reachable;
            public bool nameMentionedInCode;
        }

        [Serializable]
        public class DupGroup
        {
            public string hash;
            public long size;
            public List<string> paths = new List<string>();
        }

        [Serializable]
        public class Report
        {
            public string generatedUtc;
            public int assetCount;
            public long totalBytes;
            public int reachableCount;
            public long reachableBytes;
            public int unreachableCount;
            public long unreachableBytes;
            public int brokenRefCount;
            public int brokenFileCount;
            public int dupGroupCount;
            public long dupExtraBytes;
            public List<string> roots = new List<string>();
            public List<string> missingGuids = new List<string>();
            public List<BrokenRef> broken = new List<BrokenRef>();
            public List<AssetEntry> unreachable = new List<AssetEntry>();
            public List<DupGroup> duplicates = new List<DupGroup>();
            public List<string> orphanMetas = new List<string>();
            public List<string> fullyUnreachableDirs = new List<string>();
            public Dictionary<string, int> unreachableByTopDir = new Dictionary<string, int>();
            public Dictionary<string, long> unreachableBytesByTopDir = new Dictionary<string, long>();
        }

        public static void Run()
        {
            Directory.CreateDirectory(LogDir);
            var rep = new Report { generatedUtc = DateTime.Now.ToString("s") };
            Debug.Log("[ABCleanup] === AB resource audit begin ===");
            Debug.Log("[ABCleanup] project: " + Application.dataPath);

            // ---------- 1) 全量资产清单 + guid 映射 ----------
            var allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var guidToPath = new Dictionary<string, string>(StringComparer.Ordinal);
            var guids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in allPaths)
            {
                var g = AssetDatabase.AssetPathToGUID(p);
                if (!string.IsNullOrEmpty(g))
                {
                    guids.Add(g);
                    if (!guidToPath.ContainsKey(g)) guidToPath[g] = p;
                }
            }
            // 包内资产（Packages/…）的 guid 也算有效引用目标
            foreach (var g in LoadGuidsFromDisk(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages")))
                guids.Add(g);
            foreach (var g in LoadGuidsFromDisk(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "PackageCache")))
                guids.Add(g);

            Debug.Log($"[ABCleanup] assets={allPaths.Count}, guids={guids.Count}");

            // ---------- 2) 可达性：构建场景 + Resources + ProjectSettings 引用 ----------
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path)) roots.Add(s.path);

            // 任意层级的 Resources / Editor Default Resources 目录都会被 Unity 无条件打进包内
            foreach (var p in allPaths)
            {
                if (p.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.IndexOf("/Editor Default Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                    roots.Add(p);
            }
            foreach (var d in Directory.Exists(Path.Combine(Application.dataPath, "StreamingAssets"))
                         ? Directory.GetFiles(Path.Combine(Application.dataPath, "StreamingAssets"), "*", SearchOption.AllDirectories)
                         : Array.Empty<string>())
            {
                var rel = "Assets" + d.Substring(Application.dataPath.Length).Replace('\\', '/');
                roots.Add(rel);
            }
            // ProjectSettings 里引用的资产（渲染管线、Always Included Shaders 等）
            var projSettings = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProjectSettings");
            if (Directory.Exists(projSettings))
            {
                foreach (var f in Directory.GetFiles(projSettings))
                {
                    string txt;
                    try { txt = File.ReadAllText(f, Encoding.UTF8); } catch { continue; }
                    foreach (Match m in GuidRegex.Matches(txt))
                        if (guidToPath.TryGetValue(m.Groups[1].Value, out var path)) roots.Add(path);
                }
            }

            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in roots)
            {
                var root = r;
                var direct = AssetDatabase.GetDependencies(root, true);
                foreach (var d in direct) reachable.Add(d);
                reachable.Add(root);
            }
            rep.roots = roots.OrderBy(x => x).ToList();
            Debug.Log($"[ABCleanup] roots={roots.Count}, reachable={reachable.Count}");

            // ---------- 3) 代码里按名字引用的资产（动态加载的兜底保护） ----------
            var codeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allPaths)
            {
                var e = Path.GetExtension(p).ToLowerInvariant();
                if (e != ".cs" && e != ".js") continue;
                string txt;
                try { txt = File.ReadAllText(p, Encoding.UTF8); } catch { continue; }
                foreach (Match m in Regex.Matches(txt, @"""([^""\r\n]{2,80})"""))
                    codeNames.Add(m.Groups[1].Value);
            }
            Debug.Log($"[ABCleanup] code string literals={codeNames.Count}");

            // ---------- 4) 断引用扫描 ----------
            var broken = new List<BrokenRef>();
            foreach (var p in allPaths)
            {
                if (!TextExt.Contains(Path.GetExtension(p))) continue;
                string txt;
                try { txt = File.ReadAllText(p, Encoding.UTF8); } catch { continue; }
                if (txt.IndexOf("guid:", StringComparison.Ordinal) < 0) continue;

                var lines = txt.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    foreach (Match m in GuidRegex.Matches(line))
                    {
                        var g = m.Groups[1].Value;
                        if (guids.Contains(g) || IsBuiltinGuid(g)) continue;
                        bool isScript = line.IndexOf("m_Script", StringComparison.Ordinal) >= 0;
                        broken.Add(new BrokenRef
                        {
                            file = p,
                            guid = g,
                            kind = isScript ? "MissingScript" : "MissingAsset",
                            context = line.Length > 160 ? line.Substring(0, 160) : line.Trim(),
                        });
                        if (!rep.missingGuids.Contains(g)) rep.missingGuids.Add(g);
                    }
                }
            }
            rep.broken = broken;
            rep.brokenRefCount = broken.Count;
            rep.brokenFileCount = broken.Select(b => b.file).Distinct().Count();
            Debug.Log($"[ABCleanup] broken refs={rep.brokenRefCount} in {rep.brokenFileCount} files, missing guids={rep.missingGuids.Count}");

            // ---------- 5) 不可达资产 ----------
            var unreachable = new List<AssetEntry>();
            long totalBytes = 0, reachBytes = 0, unreachBytes = 0;
            foreach (var p in allPaths)
            {
                long size = 0;
                try { size = new FileInfo(ToAbs(p)).Length; } catch { }
                totalBytes += size;
                bool ok = reachable.Contains(p);
                if (ok) reachBytes += size; else unreachBytes += size;

                if (ok) continue;
                var ext = Path.GetExtension(p).ToLowerInvariant();
                if (CodeExt.Contains(ext)) continue;   // 脚本/着色器不参与未引用判定

                var name = Path.GetFileNameWithoutExtension(p);
                unreachable.Add(new AssetEntry
                {
                    path = p,
                    ext = ext,
                    size = size,
                    reachable = false,
                    nameMentionedInCode = codeNames.Contains(name),
                });
            }
            rep.assetCount = allPaths.Count;
            rep.totalBytes = totalBytes;
            rep.reachableCount = allPaths.Count - unreachable.Count;
            rep.reachableBytes = reachBytes;
            rep.unreachableCount = unreachable.Count;
            rep.unreachableBytes = unreachBytes;
            rep.unreachable = unreachable.OrderByDescending(x => x.size).ToList();

            foreach (var u in unreachable)
            {
                var seg = u.path.Split('/');
                var top = seg.Length > 1 ? seg[1] : "(root)";
                rep.unreachableByTopDir.TryGetValue(top, out var c);
                rep.unreachableByTopDir[top] = c + 1;
                rep.unreachableBytesByTopDir.TryGetValue(top, out var b);
                rep.unreachableBytesByTopDir[top] = b + u.size;
            }
            Debug.Log($"[ABCleanup] unreachable={rep.unreachableCount} ({MB(rep.unreachableBytes)} MB) / total {MB(totalBytes)} MB");

            // 整个目录都不可达（目录级清理候选）
            var dirTotal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var dirDead = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allPaths)
            {
                if (CodeExt.Contains(Path.GetExtension(p))) continue;
                var dir = p.Substring(0, Math.Max(0, p.LastIndexOf('/')));
                dirTotal.TryGetValue(dir, out var t); dirTotal[dir] = t + 1;
                if (!reachable.Contains(p)) { dirDead.TryGetValue(dir, out var d); dirDead[dir] = d + 1; }
            }
            foreach (var kv in dirTotal)
                if (kv.Value > 1 && dirDead.TryGetValue(kv.Key, out var dead) && dead == kv.Value)
                    rep.fullyUnreachableDirs.Add($"{kv.Key} ({dead} files)");
            rep.fullyUnreachableDirs.Sort(StringComparer.OrdinalIgnoreCase);

            // ---------- 6) 重复资源（同字节内容、不同路径） ----------
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var p in allPaths)
            {
                var ext = Path.GetExtension(p).ToLowerInvariant();
                if (CodeExt.Contains(ext)) continue;
                var abs = ToAbs(p);
                long len;
                try { len = new FileInfo(abs).Length; } catch { continue; }
                if (len < 1024) continue; // 小于 1KB 的重复无意义
                string h;
                try { h = HashFile(abs); } catch { continue; }
                var key = ext + ":" + len + ":" + h;
                if (!byHash.TryGetValue(key, out var list)) byHash[key] = list = new List<string>();
                list.Add(p);
            }
            foreach (var kv in byHash.Where(k => k.Value.Count > 1))
            {
                var parts = kv.Key.Split(':');
                long len = long.Parse(parts[1]);
                rep.duplicates.Add(new DupGroup
                {
                    hash = kv.Key,
                    size = len,
                    paths = kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                });
                rep.dupExtraBytes += len * (kv.Value.Count - 1);
            }
            rep.duplicates = rep.duplicates.OrderByDescending(d => d.size * (d.paths.Count - 1)).ToList();
            rep.dupGroupCount = rep.duplicates.Count;
            Debug.Log($"[ABCleanup] duplicate groups={rep.dupGroupCount}, wasted={MB(rep.dupExtraBytes)} MB");

            // ---------- 7) 孤儿 .meta ----------
            foreach (var meta in Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories))
            {
                var target = meta.Substring(0, meta.Length - 5);
                if (!File.Exists(target) && !Directory.Exists(target))
                    rep.orphanMetas.Add("Assets" + meta.Substring(Application.dataPath.Length).Replace('\\', '/'));
            }
            Debug.Log($"[ABCleanup] orphan meta={rep.orphanMetas.Count}");

            // ---------- 8) 输出 ----------
            File.WriteAllText(JsonPath, JsonUtility.ToJson(rep, true), new UTF8Encoding(false));
            File.WriteAllText(MdPath, BuildMarkdown(rep), new UTF8Encoding(false));

            Debug.Log("[ABCleanup] json: " + JsonPath);
            Debug.Log("[ABCleanup] md  : " + MdPath);
            Debug.Log("[ABCleanup] AUDIT DONE");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------
        static string BuildMarkdown(Report rep)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AngryBots2 资源审计（冗余 / 无效 / 过时）");
            sb.AppendLine();
            sb.AppendLine($"生成时间：{rep.generatedUtc}");
            sb.AppendLine();
            sb.AppendLine("## 总览");
            sb.AppendLine();
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 资产总数 | {rep.assetCount} ({MB(rep.totalBytes)} MB) |");
            sb.AppendLine($"| 可达（参与构建） | {rep.reachableCount} ({MB(rep.reachableBytes)} MB) |");
            sb.AppendLine($"| 不可达候选 | {rep.unreachableCount} ({MB(rep.unreachableBytes)} MB) |");
            sb.AppendLine($"| 断引用 | {rep.brokenRefCount} 处 / {rep.brokenFileCount} 个文件 / {rep.missingGuids.Count} 个缺失 guid |");
            sb.AppendLine($"| 完全重复 | {rep.dupGroupCount} 组，冗余 {MB(rep.dupExtraBytes)} MB |");
            sb.AppendLine($"| 孤儿 .meta | {rep.orphanMetas.Count} |");
            sb.AppendLine();
            sb.AppendLine("## 不可达资产按顶层目录");
            sb.AppendLine();
            sb.AppendLine("| 目录 | 文件数 | 体积 |");
            sb.AppendLine("|---|---|---|");
            foreach (var kv in rep.unreachableBytesByTopDir.OrderByDescending(k => k.Value))
                sb.AppendLine($"| {kv.Key} | {rep.unreachableByTopDir[kv.Key]} | {MB(kv.Value)} MB |");
            sb.AppendLine();
            sb.AppendLine("## 整目录不可达（目录级清理候选）");
            sb.AppendLine();
            foreach (var d in rep.fullyUnreachableDirs) sb.AppendLine("- " + d);
            sb.AppendLine();
            sb.AppendLine("## 不可达资产明细");
            sb.AppendLine();
            sb.AppendLine("| 文件 | 体积 KB | 代码里出现同名 |");
            sb.AppendLine("|---|---|---|");
            foreach (var u in rep.unreachable)
                sb.AppendLine($"| {u.path} | {u.size / 1024.0:F1} | {(u.nameMentionedInCode ? "是" : "")} |");
            sb.AppendLine();
            sb.AppendLine("## 断引用");
            sb.AppendLine();
            sb.AppendLine("| 文件 | 类型 | 缺失 guid | 上下文 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var b in rep.broken)
                sb.AppendLine($"| {b.file} | {b.kind} | {b.guid} | `{b.context.Replace("|", "\\|")}` |");
            sb.AppendLine();
            sb.AppendLine("## 完全重复");
            sb.AppendLine();
            foreach (var d in rep.duplicates)
            {
                sb.AppendLine($"### {d.size / 1024.0:F1} KB × {d.paths.Count}");
                foreach (var p in d.paths) sb.AppendLine($"- {p}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static bool IsBuiltinGuid(string g)
        {
            // Unity 内置资源 guid 形如 0000000000000000f000000000000000
            for (int i = 0; i < 16; i++) if (g[i] != '0') return false;
            return true;
        }

        static string ToAbs(string assetPath) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', '\\'));

        static IEnumerable<string> LoadGuidsFromDisk(string root)
        {
            if (!Directory.Exists(root)) yield break;
            foreach (var m in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                string txt;
                try { txt = File.ReadAllText(m, Encoding.UTF8); } catch { continue; }
                var mm = GuidRegex.Match(txt);
                if (mm.Success) yield return mm.Groups[1].Value;
            }
        }

        static string HashFile(string path)
        {
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static string MB(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1");
    }
}
#endif
