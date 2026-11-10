// U6PortInventory.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 移植清单生成器 (namespace ABPort)
//
// 职责：
//   1. 枚举只读源工程 AngryBots-Unity6/Assets 下全部实体资产（排除 Library /
//      TextMesh Pro 包内资源 / Standard assets）。
//   2. 与 AB2 比对：
//        SamePathSameGuidSameBytes  -> 跳过（已经完全存在）
//        SamePathSameGuidDiffBytes  -> 内容漂移，仅记录
//        SamePathDiffGuid           -> 需要重映射 GUID
//        U6Only                     -> 需要拷贝
//   3. 扫描 GUID 冲突：U6 的 guid 若在 AB2 里已被「另一个相对路径」占用，则迁入时
//      必须重新生成 guid 并改写引用（保守模式：所有 non-identity 的 guid 一律变化）。
//   4. 产出 E:\MyPlayGround\_logs\u6_port_inventory.json
//
// 用法（batchmode）：
//   Unity.exe -batchmode -quit -projectPath <AB2> \
//       -executeMethod ABPort.U6PortInventory.Run \
//       -logFile E:\MyPlayGround\_logs\u6_inventory.log
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

namespace ABPort
{
    public static class U6PortInventory
    {
        public const string U6Root = U6PortPaths.U6Root;
        public const string LogDir = U6PortPaths.LogDir;
        public const string JsonPath = LogDir + @"\u6_port_inventory.json";

        /// <summary>移植时要排除的顶层目录（包内资源 / 不参与移植）。</summary>
        static readonly HashSet<string> ExcludedTopDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TextMesh Pro",
            "Standard assets",
            "Library",
            "AngryBots2",   // U6 里的 AB2 回灌产物（上一轮反向移植），不属于移植范围
        };

        /// <summary>不作为「实体资产」参与移植的扩展名。</summary>
        static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".meta", ".cs", ".js", ".boo", ".dll",
        };

        static readonly Regex GuidRegex = new Regex(@"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline | RegexOptions.Compiled);

        // --- 数据模型 ---------------------------------------------------------
        [Serializable]
        public class Entry
        {
            public string path;       // U6 相对路径（不含 Assets/ 前缀，统一 '/' -> '\'）
            public string ext;
            public string guid;       // U6 的 guid（asset 的，不是 meta 的）
            public long size;
            public string status;     // SameBytes / SamePathDiffGuid / U6Only / SamePathSameGuidDiffBytes
            public string ab2Path;    // 同路径比对用的 AB2 相对路径（可能为空）
            public string ab2Guid;
        }

        [Serializable]
        public class GuidConflict
        {
            public string guid;
            public string u6Path;
            public string ab2Path;
            public string reason;
        }

        [Serializable]
        public class Report
        {
            public string generatedUtc;
            public string u6Root;
            public string ab2Root;
            public int u6AssetCount;
            public int ab2AssetCount;
            public int u6GuidCount;
            public int ab2GuidCount;
            public int commonGuidCount;
            public int samePathSameGuidSameBytes;
            public int samePathSameGuidDiffBytes;
            public int samePathDiffGuid;
            public int u6Only;
            public int guidConflictCount;
            public int u6OnlyGuidCount;
            public List<Entry> entries = new List<Entry>();
            public List<GuidConflict> guidConflicts = new List<GuidConflict>();
            public List<string> note = new List<string>();
        }

        // --- 工具 -------------------------------------------------------------
        static string Norm(string rel) => rel.Replace('/', '\\');

        static List<string> EnumerateAssets(string root, Func<string, bool> filter)
        {
            var outList = new List<string>();
            if (!Directory.Exists(root)) return outList;
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Norm(f.Substring(root.Length + 1));
                var top = rel.Split('\\')[0];
                if (ExcludedTopDirs.Contains(top)) continue;
                if (filter != null && !filter(rel)) continue;
                outList.Add(rel);
            }
            outList.Sort(StringComparer.OrdinalIgnoreCase);
            return outList;
        }

        static Dictionary<string, string> LoadGuidMap(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return map;
            foreach (var m in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                string txt;
                try { txt = File.ReadAllText(m, Encoding.UTF8); } catch { continue; }
                var mm = GuidRegex.Match(txt);
                if (!mm.Success) continue;
                var assetRel = Norm(m.Substring(root.Length + 1));
                assetRel = assetRel.Substring(0, assetRel.Length - ".meta".Length);
                map[mm.Groups[1].Value] = assetRel;
            }
            return map;
        }

        // --- 主流程 -----------------------------------------------------------
        public static void Run()
        {
            Directory.CreateDirectory(LogDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rep = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                u6Root = U6Root,
                ab2Root = Application.dataPath,
            };

            Debug.Log("[ABPort] === U6 port inventory begin ===");
            Debug.Log("[ABPort] U6 root : " + U6Root);
            Debug.Log("[ABPort] AB2 root: " + Application.dataPath);

            // 1) 枚举 U6 实体资产（排除 TMP/Standard Assets/脚本）
            var u6All = EnumerateAssets(U6Root, null);
            var u6Assets = EnumerateAssets(U6Root, rel => !IgnoredExtensions.Contains(Path.GetExtension(rel)));
            Debug.Log($"[ABPort] U6 files (excl. TMP/StdAssets) = {u6All.Count}, asset files = {u6Assets.Count}");

            // 2) guid 映射
            var gu6 = LoadGuidMap(U6Root);
            var gab2 = LoadGuidMap(Application.dataPath);
            rep.u6GuidCount = gu6.Count;
            rep.ab2GuidCount = gab2.Count;
            rep.u6AssetCount = u6Assets.Count;

            var ab2Assets = EnumerateAssets(Application.dataPath, rel => !IgnoredExtensions.Contains(Path.GetExtension(rel)));
            rep.ab2AssetCount = ab2Assets.Count;
            var ab2Set = new HashSet<string>(ab2Assets, StringComparer.OrdinalIgnoreCase);

            var u6AssetSet = new HashSet<string>(u6Assets, StringComparer.OrdinalIgnoreCase);
            var ab2GuidSet = new HashSet<string>(gab2.Keys, StringComparer.Ordinal);

            // 3) 逐资产比对
            foreach (var rel in u6Assets)
            {
                var u6Full = Path.Combine(U6Root, rel);
                var e = new Entry { path = rel, ext = Path.GetExtension(rel), size = new FileInfo(u6Full).Length };
                var u6Meta = u6Full + ".meta";
                if (File.Exists(u6Meta))
                {
                    var mm = GuidRegex.Match(File.ReadAllText(u6Meta, Encoding.UTF8));
                    if (mm.Success) e.guid = mm.Groups[1].Value;
                }

                if (ab2Set.Contains(rel))
                {
                    var ab2Full = Path.Combine(Application.dataPath, rel);
                    e.ab2Path = rel;
                    var ab2Meta = ab2Full + ".meta";
                    if (File.Exists(ab2Meta))
                    {
                        var mm = GuidRegex.Match(File.ReadAllText(ab2Meta, Encoding.UTF8));
                        if (mm.Success) e.ab2Guid = mm.Groups[1].Value;
                    }

                    if (e.guid != null && e.guid == e.ab2Guid)
                        e.status = SameBytes(u6Full, ab2Full) ? "SameBytes" : "SamePathSameGuidDiffBytes";
                    else
                        e.status = "SamePathDiffGuid";

                    if (e.guid != null && e.ab2Guid != null && e.guid != e.ab2Guid && ab2GuidSet.Contains(e.guid))
                        rep.guidConflicts.Add(new GuidConflict
                        {
                            guid = e.guid,
                            u6Path = rel,
                            ab2Path = gab2[e.guid],
                            reason = "同一路径不同 guid，且 U6 的 guid 已被 AB2 另一路径占用",
                        });
                }
                else
                {
                    e.status = "U6Only";
                    if (e.guid != null && ab2GuidSet.Contains(e.guid))
                        rep.guidConflicts.Add(new GuidConflict
                        {
                            guid = e.guid,
                            u6Path = rel,
                            ab2Path = gab2[e.guid],
                            reason = "U6 独有资产，但其 guid 在 AB2 已被另一路径占用（跨路径冲突）",
                        });
                }
                rep.entries.Add(e);
            }

            rep.samePathSameGuidSameBytes = rep.entries.Count(x => x.status == "SameBytes");
            rep.samePathSameGuidDiffBytes = rep.entries.Count(x => x.status == "SamePathSameGuidDiffBytes");
            rep.samePathDiffGuid = rep.entries.Count(x => x.status == "SamePathDiffGuid");
            rep.u6Only = rep.entries.Count(x => x.status == "U6Only");
            rep.guidConflictCount = rep.guidConflicts.Count;
            rep.commonGuidCount = gu6.Keys.Count(k => ab2GuidSet.Contains(k));
            rep.u6OnlyGuidCount = gu6.Keys.Count(k => !ab2GuidSet.Contains(k));

            // 4) 结论备注
            rep.note.Add("同源共路径资产：同 guid 同字节可跳过；同名不同 guid 需重映射。");
            rep.note.Add("guid 冲突处理策略（保守）：凡是 non-identity 的 guid（即 U6 路径与 AB2 路径不一致）一律重新生成，并改写引用。");
            rep.note.Add("identity 判定：U6 相对路径 == AB2 相对路径，或 U6 的 'AngryBots2/<path>' == AB2 的 '<path>'（U6 把 AB2 内容放进了子目录）。");

            var json = JsonUtility.ToJson(rep, true);
            File.WriteAllText(JsonPath, json, new UTF8Encoding(false));

            Debug.Log("[ABPort] --------------------------------------------------");
            Debug.Log($"[ABPort] U6 assets         : {rep.u6AssetCount}");
            Debug.Log($"[ABPort] AB2 assets        : {rep.ab2AssetCount}");
            Debug.Log($"[ABPort] same+same+bytes   : {rep.samePathSameGuidSameBytes}  (skip)");
            Debug.Log($"[ABPort] same guid diff bytes: {rep.samePathSameGuidDiffBytes}  (record only)");
            Debug.Log($"[ABPort] same path diff guid: {rep.samePathDiffGuid}  (needs remap)");
            Debug.Log($"[ABPort] U6 only            : {rep.u6Only}  (needs copy)");
            Debug.Log($"[ABPort] guid conflicts     : {rep.guidConflictCount}");
            Debug.Log($"[ABPort] identity guid count: {CountIdentityGuids(gu6, gab2)}");
            Debug.Log($"[ABPort] inventory json     : {JsonPath}");
            Debug.Log($"[ABPort] inventory done in {sw.ElapsedMilliseconds} ms");
            Debug.Log("[ABPort] INVENTORY RESULT: PASS");
        }

        /// <summary>统计 U6 guid 中「相对路径在 AB2 上完全相同」的数量（identity，可原样保留 guid）。</summary>
        public static int CountIdentityGuids(Dictionary<string, string> gu6, Dictionary<string, string> gab2)
        {
            int n = 0;
            foreach (var kv in gu6)
            {
                if (!gab2.TryGetValue(kv.Key, out var ab2Rel)) continue;
                var u6Rel = kv.Value;
                if (string.Equals(u6Rel, ab2Rel, StringComparison.OrdinalIgnoreCase)) { n++; continue; }
                if (u6Rel.StartsWith("AngryBots2\\", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(u6Rel.Substring("AngryBots2\\".Length), ab2Rel, StringComparison.OrdinalIgnoreCase)) { n++; }
            }
            return n;
        }

        static bool SameBytes(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                using (var sa = File.OpenRead(a))
                using (var sb = File.OpenRead(b))
                {
                    var ba = new byte[64 * 1024]; var bb = new byte[64 * 1024];
                    while (true)
                    {
                        int na = sa.Read(ba, 0, ba.Length);
                        int nb = sb.Read(bb, 0, bb.Length);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                    }
                }
            }
            catch { return false; }
        }
    }
}
#endif