// ABResourceCleanup.cs
// -----------------------------------------------------------------------------
// 资源清理：删除「不可达（过时）」资产，跳过「代码里按名字出现」的保护项。
//
// 安全策略：
//   * 可达性每次运行都重算（构建场景 + 任意层级 Resources + StreamingAssets +
//     ProjectSettings 里引用的资产），不复用旧报告。
//   * 只删「不参与构建」的实体资产；脚本/着色器等可执行文件一律不动。
//   * 文件名（不含扩展名）在任意 .cs/.js 字符串字面量里出现过 -> 视为可能被
//     Resources.Load / 按名加载，保留。
//
// 用法：
//   Tuanjie.exe -batchmode -projectPath <AB2> -executeMethod ABCleanup.ABResourceCleanup.DryRun
//   Tuanjie.exe -batchmode -projectPath <AB2> -executeMethod ABCleanup.ABResourceCleanup.Run
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ABCleanup
{
    public static class ABResourceCleanup
    {
        const string LogDir = @"E:\MyPlayGround\_logs";
        const string ManifestPath = LogDir + @"\ab_resource_cleanup.json";
        const string ReportPath = LogDir + @"\ab_resource_cleanup.md";

        static readonly Regex GuidRegex = new Regex(@"guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);

        /// <summary>不参与删除的可执行/工具类文件。</summary>
        static readonly HashSet<string> CodeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".boo", ".dll", ".asmdef", ".asmref", ".rsp", ".shader", ".hlsl",
            ".cginc", ".compute", ".raytrace", ".meta", ".txt", ".md", ".json", ".xml", ".csv",
            ".preset",
        };

        [Serializable]
        public class Plan
        {
            public string generatedUtc;
            public bool dryRun;
            public int candidateCount;
            public long candidateBytes;
            public int protectedCount;
            public long protectedBytes;
            public int deletedCount;
            public long deletedBytes;
            public List<string> failed = new List<string>();
            public List<string> foldersRemoved = new List<string>();
            public List<string> protectedItems = new List<string>();
            public List<string> candidates = new List<string>();
        }

        /// <summary>只统计不删除。</summary>
        public static void DryRun()
        {
            var plan = BuildPlan();
            plan.dryRun = true;
            WritePlan(plan);
            Debug.Log($"[ABCleanup] DRY RUN candidates={plan.candidateCount} ({MB(plan.candidateBytes)} MB), protected={plan.protectedCount}");
            Debug.Log("[ABCleanup] DRY RUN DONE");
            EditorApplication.Exit(0);
        }

        /// <summary>执行删除。</summary>
        public static void Run()
        {
            var plan = BuildPlan();
            plan.dryRun = false;
            long bytes = plan.candidateBytes;

            Debug.Log($"[ABCleanup] deleting {plan.candidateCount} assets ({MB(bytes)} MB)...");
            AssetDatabase.StartAssetEditing();
            var failed = new List<string>();
            try
            {
                for (int i = 0; i < plan.candidates.Count; i += 100)
                {
                    var chunk = plan.candidates.Skip(i).Take(100).ToList();
                    var chunkFailed = new List<string>();
                    AssetDatabase.DeleteAssets(chunk.ToArray(), chunkFailed);
                    failed.AddRange(chunkFailed);
                    if ((i / 100) % 3 == 0)
                        Debug.Log($"[ABCleanup] progress {Math.Min(i + 100, plan.candidates.Count)}/{plan.candidates.Count}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            plan.failed = failed.OrderBy(x => x).ToList();
            plan.deletedCount = plan.candidateCount - failed.Count;
            plan.deletedBytes = bytes;

            // 清理空目录（从深到浅）
            var emptyDirs = new List<string>();
            foreach (var dir in AssetDatabase.GetAllAssetPaths().Where(AssetDatabase.IsValidFolder)
                         .OrderByDescending(d => d.Count(c => c == '/')))
            {
                if (Directory.Exists(ToAbs(dir)) &&
                    !Directory.EnumerateFileSystemEntries(ToAbs(dir)).Any())
                    emptyDirs.Add(dir);
            }
            foreach (var d in emptyDirs)
            {
                if (AssetDatabase.DeleteAsset(d)) plan.foldersRemoved.Add(d);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            WritePlan(plan);
            Debug.Log($"[ABCleanup] deleted={plan.deletedCount}, failed={plan.failed.Count}, emptyFoldersRemoved={plan.foldersRemoved.Count}");
            foreach (var f in plan.failed) Debug.LogError("[ABCleanup] delete failed: " + f);
            Debug.Log("[ABCleanup] CLEANUP DONE");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------
        static Plan BuildPlan()
        {
            Directory.CreateDirectory(LogDir);
            var plan = new Plan { generatedUtc = DateTime.Now.ToString("s") };

            var allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p))
                .ToList();

            // guid 集合（含 Packages）
            var guidToPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in allPaths)
            {
                var g = AssetDatabase.AssetPathToGUID(p);
                if (!string.IsNullOrEmpty(g) && !guidToPath.ContainsKey(g)) guidToPath[g] = p;
            }

            // 根：构建场景 + 任意层级 Resources + StreamingAssets + ProjectSettings 引用
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path)) roots.Add(s.path);
            foreach (var p in allPaths)
                if (p.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.IndexOf("/Editor Default Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                    roots.Add(p);
            var streaming = Path.Combine(Application.dataPath, "StreamingAssets");
            if (Directory.Exists(streaming))
                foreach (var f in Directory.GetFiles(streaming, "*", SearchOption.AllDirectories))
                    roots.Add("Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/'));

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
                if (string.IsNullOrEmpty(r) || !File.Exists(ToAbs(r))) continue;
                try
                {
                    foreach (var d in AssetDatabase.GetDependencies(r, true)) reachable.Add(d);
                }
                catch (Exception e) { Debug.LogWarning("[ABCleanup] deps failed for " + r + ": " + e.Message); }
                reachable.Add(r);
            }

            // 代码里按名字出现的保护集
            var codeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allPaths)
            {
                var e = Path.GetExtension(p).ToLowerInvariant();
                if (e != ".cs" && e != ".js") continue;
                string txt;
                try { txt = File.ReadAllText(ToAbs(p), Encoding.UTF8); } catch { continue; }
                foreach (Match m in Regex.Matches(txt, @"""([^""\r\n]{2,80})"""))
                    codeNames.Add(m.Groups[1].Value);
            }

            foreach (var p in allPaths)
            {
                if (reachable.Contains(p)) continue;
                if (CodeExt.Contains(Path.GetExtension(p))) continue;
                // Gizmos 图标由 EditorGUIUtility/Gizmos.DrawIcon 按路径字符串加载，
                // 包内代码里的引用不在本脚本的扫描范围内，整体保留（体积可忽略）。
                if (p.IndexOf("/Gizmos/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                long size = 0;
                try { size = new FileInfo(ToAbs(p)).Length; } catch { }
                var baseName = Path.GetFileNameWithoutExtension(p);
                if (codeNames.Contains(baseName) || codeNames.Contains(Path.GetFileName(p)))
                {
                    plan.protectedItems.Add(p);
                    plan.protectedBytes += size;
                    continue;
                }
                plan.candidates.Add(p);
                plan.candidateBytes += size;
            }

            plan.candidates.Sort(StringComparer.OrdinalIgnoreCase);
            plan.protectedItems.Sort(StringComparer.OrdinalIgnoreCase);
            plan.candidateCount = plan.candidates.Count;
            plan.protectedCount = plan.protectedItems.Count;
            return plan;
        }

        static void WritePlan(Plan plan)
        {
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));

            var sb = new StringBuilder();
            sb.AppendLine("# AngryBots2 资源清理清单");
            sb.AppendLine();
            sb.AppendLine($"生成时间：{plan.generatedUtc}　模式：{(plan.dryRun ? "试运行" : "实际删除")}");
            sb.AppendLine();
            sb.AppendLine($"- 候选：{plan.candidateCount} 个 / {MB(plan.candidateBytes)} MB");
            sb.AppendLine($"- 已删除：{plan.deletedCount} 个 / {MB(plan.deletedBytes)} MB");
            sb.AppendLine($"- 失败：{plan.failed.Count}");
            sb.AppendLine($"- 保护（代码里出现同名）：{plan.protectedCount} 个 / {MB(plan.protectedBytes)} MB");
            sb.AppendLine($"- 清掉的空目录：{plan.foldersRemoved.Count}");
            sb.AppendLine();
            sb.AppendLine("## 保护清单");
            sb.AppendLine();
            foreach (var p in plan.protectedItems) sb.AppendLine("- " + p);
            sb.AppendLine();
            sb.AppendLine("## 删除候选");
            sb.AppendLine();
            foreach (var p in plan.candidates) sb.AppendLine("- " + p);
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        }

        static string ToAbs(string assetPath) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', '\\'));

        static string MB(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1");
    }
}
#endif
