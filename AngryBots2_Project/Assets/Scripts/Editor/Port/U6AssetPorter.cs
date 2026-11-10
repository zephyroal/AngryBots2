// U6AssetPorter.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 主移植器 (namespace ABPort)
//
// 把只读源 AngryBots-Unity6/Assets 中的资产移植进 AB2，遵守：
//   * 绝不修改 U6 源目录（只读）
//   * 不删除 / 不覆盖 AB2 现有资产；同路径资产保持不动
//   * 全部内容落到 Assets/U6Port/**（目标目录映射见 MapTarget）
//   * GUID 安全：
//       - U6 路径未被 AB2 占用 -> 原样保留 U6 的 guid（引用自动接上 AB2 已有同源资产）
//       - U6 路径已被 AB2 占用 -> 目标路径改为 U6Port 下的映射路径；如果 U6 的 guid
//         在 AB2 中已被「另一路径」占用，则重新生成 guid，并全局改写引用
//   * .cs 全部转 UTF-8（U6 有 GBK 编码的源文件）
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <AB2> -executeMethod ABPort.U6AssetPorter.Run \
//             -logFile E:\MyPlayGround\_logs\u6_port_assets.log
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
    public static class U6AssetPorter
    {
        // --- 迁移清单 -------------------------------------------------------
        /// <summary>顶层目录：true = 全部递归迁移。</summary>
        static readonly Dictionary<string, bool> IncludeTopDirs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["AngryBots"] = true,          // 旧 lightmap / 反射探针（P2 会重烘）
            // "AngryBots2" 故意不列入：那是 U6 里的 AB2 回灌产物，拷回 AB2 只会制造 1.1GB 重复副本
            ["Animations"] = true,
            // "Editor" 故意不列入：U6 的编辑器脚本是它自己的 Unity6 开发工具（GameplayProbe/VisualCheck 等），
            // 使用 linearVelocity / VolumeManager.isInitialized 等 Unity6 专有 API，移植进 AB2 会直接编译失败，且无移植价值。
            ["Explosions"] = true,
            ["Fonts"] = true,
            ["Gizmos"] = true,
            ["Materials"] = true,
            ["Objects"] = true,
            ["PhysicMaterials"] = true,
            ["Prefabs"] = true,
            ["Resources"] = true,          // 后处理 shader（按任务要求用 URP Volume 代替，仍原样保留）
            ["Scripts"] = true,
            ["Settings"] = true,
            ["Shaders"] = true,
            ["Sounds"] = true,
            ["Textures"] = true,
        };

        public enum Action { SkipSameBytes, CopyAsIs, CopyRemap, CopyNew, ConvertScript }

        public class PlanItem
        {
            public string u6Rel;         // U6 相对路径
            public string targetRel;     // AB2 相对路径
            public string u6Guid;        // U6 的 guid（可能为空）
            public string finalGuid;     // AB2 中最终使用的 guid
            public Action action;
            public string reason;
        }

        public static readonly List<PlanItem> Plan = new List<PlanItem>();

        /// <summary>AB2 现有全部 .cs 的文件名（不含扩展名），用于 CS0101 防护。</summary>
        static HashSet<string> _ab2ScriptNames;

        /// <summary>收集 AB2 现有脚本名（排除 U6Port 自身，避免自我冲突误判）。</summary>
        static HashSet<string> CollectAb2ScriptNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var portAbs = Path.Combine(Application.dataPath, "U6Port");
            foreach (var f in Directory.EnumerateFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (f.StartsWith(portAbs, StringComparison.OrdinalIgnoreCase)) continue;
                set.Add(Path.GetFileNameWithoutExtension(f));
            }
            return set;
        }
        static Dictionary<string, string> _u6GuidMap;
        static Dictionary<string, string> _ab2GuidMap;
        static Dictionary<string, string> _u6PathToGuid;
        static Dictionary<string, string> _finalGuidByU6Path;

        // --- 目标路径映射 ---------------------------------------------------
        /// <summary>
        /// U6 相对路径（不含 Assets/ 前缀）-> AB2 目标路径。
        ///
        /// 铁律：**所有移植内容一律落在 Assets/U6Port/ 之内**，绝不写回 AB2 既有资产树。
        /// 原因：
        ///   1. 写入范围约定只允许 Assets/U6Port/ 与 Assets/Scripts/Editor/Port/；
        ///   2. U6 的 Assets/Editor/*.cs 与 AB2 的 Assets/Scripts/Editor/*.cs 同名同类型，
        ///      只要落到 Assets/ 下任意位置就会被 Assembly-CSharp-Editor 重复编译 → CS0101；
        ///   3. GUID 引用与路径无关，搬进 U6Port 不会破坏任何引用。
        ///
        /// 返回值形态："Assets/U6Port/<U6 原始相对路径>"，可直接交给 U6PortCommon.AbsPath。
        /// </summary>
        public static string MapTarget(string u6Rel)
        {
            var rel = U6PortCommon.Norm(u6Rel).Replace('/', '\\').TrimStart('\\');
            // 统一落到 U6Port 下，保留 U6 的原始相对结构（便于人工核对）
            return U6PortCommon.PortRoot + "/" + U6PortCommon.Slash(rel);
        }

        /// <summary>写入前的最后防线：目标路径必须落在 Assets/ 之内，否则抛异常中止。</summary>
        static void AssertInsideAssets(string assetPath, string u6Rel)
        {
            var norm = U6PortCommon.Slash(assetPath);
            if (!norm.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"target '{assetPath}' for '{u6Rel}' is outside Assets/ - refusing to write");
            var full = U6PortCommon.AbsPath(norm);
            var fullNorm = Path.GetFullPath(full).TrimEnd('\\', '/');
            var dataNorm = Path.GetFullPath(Application.dataPath).TrimEnd('\\', '/');
            if (!fullNorm.StartsWith(dataNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"target '{full}' for '{u6Rel}' resolves outside {Application.dataPath} - refusing to write");
        }

        // --- 规划 -----------------------------------------------------------
        public static void BuildPlan()
        {
            Plan.Clear();
            _u6GuidMap = U6PortCommon.BuildU6GuidMap();
            _ab2GuidMap = U6PortCommon.BuildAb2GuidMap();
            _ab2ScriptNames = CollectAb2ScriptNames();
            _u6PathToGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _u6GuidMap)
            {
                // BuildGuidMap 返回的是 .meta 的相对路径（含 ".meta"），
                // 这里必须剥掉后缀，否则按资产相对路径查 guid 会全部落空。
                var p = kv.Value;
                if (p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    p = p.Substring(0, p.Length - ".meta".Length);
                _u6PathToGuid[p] = kv.Key;
            }
            _finalGuidByU6Path = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(U6PortCommon.U6Root))
                throw new Exception("U6 source root not found: " + U6PortCommon.U6Root);

            var files = Directory.EnumerateFiles(U6PortCommon.U6Root, "*", SearchOption.AllDirectories)
                .Select(f => U6PortCommon.Norm(f.Substring(U6PortCommon.U6Root.Length + 1)))
                .Where(rel =>
                {
                    var top = rel.Split('\\')[0];
                    if (!IncludeTopDirs.TryGetValue(top, out var all) || !all) return false;
                    var ext = Path.GetExtension(rel).ToLowerInvariant();
                    if (ext == ".meta") return false;
                    return true;
                })
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rel in files)
            {
                var item = new PlanItem { u6Rel = rel };
                _u6PathToGuid.TryGetValue(rel, out item.u6Guid);
                var ext = Path.GetExtension(rel).ToLowerInvariant();

                // 明确不属于移植范围的内容
                if (ext == ".js" || ext == ".boo" || rel.EndsWith(".js.old", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".cs.old", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(rel) == "PostprocessBuildPlayer")
                {
                    item.action = Action.SkipSameBytes;
                    item.reason = "非 C# 脚本/构建后处理，跳过";
                    Plan.Add(item);
                    continue;
                }

                item.targetRel = MapTarget(rel);
                AssertInsideAssets(item.targetRel, rel);
                var targetAbs = U6PortCommon.AbsPath(item.targetRel);
                var u6Abs = Path.Combine(U6PortCommon.U6Root, rel);

                // AB2 里「同一相对路径」的对应文件（用于同源判定）
                var mirroredAb2Abs = Path.Combine(Application.dataPath, rel);
                var mirroredAb2Guid = U6PortCommon.ReadMetaGuid(mirroredAb2Abs + ".meta");
                var mirroredExists = File.Exists(mirroredAb2Abs);

                var ab2GuidHere = U6PortCommon.ReadMetaGuid(targetAbs + ".meta");
                var targetExists = File.Exists(targetAbs);

                // --- CS0101 防护：与 AB2 已有脚本同名的 .cs 一律不移植 ---
                // Unity 把所有非 Editor 目录下的 .cs 编进同一个程序集，同名类型会在
                // Assembly-CSharp / Assembly-CSharp-Editor 里重复定义（CS0101/CS0111）。
                if (ext == ".cs" && _ab2ScriptNames.Contains(Path.GetFileNameWithoutExtension(rel)))
                {
                    item.action = Action.SkipSameBytes;
                    item.reason = "AB2 已有同名脚本（避免 CS0101 重复定义），跳过";
                    Plan.Add(item);
                    continue;
                }

                // --- 同源资产：AB2 已有同路径且同 guid → 完全跳过，不产生副本 ---
                if (mirroredExists && item.u6Guid != null && item.u6Guid == mirroredAb2Guid)
                {
                    item.finalGuid = mirroruredGuidOr(item, mirroredAb2Guid);
                    item.action = Action.SkipSameBytes;
                    item.reason = "AB2 同路径同 guid 已存在（引用可直接解析），跳过";
                    _finalGuidByU6Path[rel] = item.finalGuid;
                    Plan.Add(item);
                    continue;
                }

                // --- 目标 U6Port 下已存在 ---
                if (targetExists)
                {
                    item.finalGuid = ab2GuidHere ?? item.u6Guid;
                    item.action = Action.SkipSameBytes;
                    item.reason = "U6Port 目标已存在，保持不动";
                    _finalGuidByU6Path[rel] = item.finalGuid;
                    Plan.Add(item);
                    continue;
                }

                // --- 需要写入 U6Port ---
                // guid 是否已被 AB2 的「另一路径」占用（同 guid 不同路径会导致引用指向错对象）
                bool guidTakenElsewhere = item.u6Guid != null &&
                                          _ab2GuidMap.TryGetValue(item.u6Guid, out var owner) &&
                                          !string.Equals(U6PortCommon.Norm(owner.Replace(".meta", "")),
                                                         U6PortCommon.Norm(rel), StringComparison.OrdinalIgnoreCase);

                if (ext == ".cs")
                {
                    item.finalGuid = item.u6Guid ?? U6PortCommon.NewGuid();
                    item.action = Action.ConvertScript;
                    item.reason = "U6 独有脚本，GBK->UTF-8 后写入 U6Port/Scripts";
                }
                else if (item.u6Guid == null)
                {
                    item.finalGuid = null;
                    item.action = Action.CopyNew;
                    item.reason = "无 meta（无 guid），直接拷贝";
                }
                else if (!guidTakenElsewhere)
                {
                    // 关键路径：保留 U6 guid。
                    // AB2 里那 58 个 InternalErrorShader 材质引用的正是这些 guid，
                    // 原样保留后它们会立刻从粉色恢复。
                    item.finalGuid = item.u6Guid;
                    item.action = Action.CopyAsIs;
                    item.reason = mirroredExists
                        ? "AB2 同路径已存在但 guid 不同；保留 U6 guid 写入 U6Port（修复按 guid 引用它的材质）"
                        : "AB2 缺失；保留 U6 guid 写入 U6Port";
                }
                else
                {
                    // 同一 guid 在 AB2 里出现在「另一条路径」= 同源资产被移动/重命名过。
                    // guid 引用与路径无关，U6 场景/材质里对该 guid 的引用会直接解析到
                    // AB2 现有那一份，因此不应再造一个带新 guid 的重复副本。
                    item.finalGuid = item.u6Guid;
                    item.action = Action.SkipSameBytes;
                    item.reason = "同 guid 已在 AB2 另一路径存在（同源资产被移动/重命名），引用自动解析，跳过";
                }
                _finalGuidByU6Path[rel] = item.finalGuid;
                Plan.Add(item);
            }
        }

        /// <summary>同源跳过时确定最终 guid（AB2 侧优先）。</summary>
        static string mirroruredGuidOr(PlanItem item, string ab2Guid)
        {
            return ab2Guid ?? item.u6Guid;
        }

        // --- guid 重新映射表 --------------------------------------------------
        /// <summary>
        /// 构建「引用改写表」：U6 guid -> 最终 guid。
        /// 只有「被重新生成」的 guid 才需要改写；其余保持不变。
        /// </summary>
        public static Dictionary<string, string> BuildRemapTable()
        {
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in _u6GuidMap)
            {
                var u6Rel = kv.Value;
                if (!_finalGuidByU6Path.TryGetValue(u6Rel, out var final) || final == null) continue;
                if (final == kv.Key) continue;
                // 该 guid 在 AB2 中已被占用（别的路径），必须改写
                remap[kv.Key] = final;
            }
            return remap;
        }

        /// <summary>把一个文本里所有 guid 引用按 remap 表改写。</summary>
        public static string RewriteGuids(string text, Dictionary<string, string> remap)
        {
            if (remap.Count == 0) return text;
            return U6PortCommon.GuidRef.Replace(text, m =>
            {
                var g = m.Groups[1].Value;
                return remap.TryGetValue(g, out var ng) ? "guid: " + ng : m.Value;
            });
        }

        // --- 执行 -----------------------------------------------------------
        public class Result
        {
            public int planned;
            public int copied;
            public int convertedScripts;
            public int skipped;
            public int guidRemapped;
            public int metaWritten;
            public int failed;
            public List<string> failures = new List<string>();
            public Dictionary<string, int> byCategory = new Dictionary<string, int>();
        }

        public static Result Execute()
        {
            var r = new Result();
            if (Plan.Count == 0) BuildPlan();
            var remap = BuildRemapTable();
            r.planned = Plan.Count;
            r.guidRemapped = remap.Count;

            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var textExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".prefab", ".unity", ".asset", ".mat", ".controller", ".anim", ".overrideController", ".lighting", ".physicMaterial", ".cubemap", ".json", ".mask" };

            int idx = 0;
            foreach (var it in Plan)
            {
                idx++;
                if (idx % 200 == 0)
                {
                    EditorUtility.DisplayProgressBar("U6 port", it.u6Rel, idx / (float)Plan.Count);
                }
                switch (it.action)
                {
                    case Action.SkipSameBytes:
                        r.skipped++;
                        continue;
                    case Action.ConvertScript:
                        {
                            try
                            {
                                var src = Path.Combine(U6PortCommon.U6Root, it.u6Rel);
                                AssertInsideAssets(it.targetRel, it.u6Rel);
                                var dst = U6PortCommon.AbsPath(it.targetRel);
                                var code = U6PortCommon.ReadTextAuto(src, out bool wasGb);
                                if (remap.Count > 0) { /* 脚本内容不含 guid 引用 */ }
                                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                                U6PortCommon.WriteUtf8(dst, code);
                                U6PortCommon.EnsureMeta(dst, it.finalGuid);
                                dirs.Add(U6PortCommon.Slash(Path.GetDirectoryName(it.targetRel)));  // targetRel 已带 Assets/ 前缀
                                r.convertedScripts++;
                                if (wasGb) Debug.Log("[ABPort] GBK->UTF8: " + it.u6Rel + " -> " + it.targetRel);
                            }
                            catch (Exception e)
                            {
                                r.failed++; r.failures.Add(it.u6Rel + " : " + e.Message);
                            }
                            continue;
                        }
                    default: break;
                }

                try
                {
                    var src = Path.Combine(U6PortCommon.U6Root, it.u6Rel);
                    AssertInsideAssets(it.targetRel, it.u6Rel);
                    var dst = U6PortCommon.AbsPath(it.targetRel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    var ext = Path.GetExtension(it.u6Rel).ToLowerInvariant();

                    if (textExts.Contains(ext) && remap.Count > 0)
                    {
                        var txt = File.ReadAllText(src, Encoding.UTF8);
                        txt = RewriteGuids(txt, remap);
                        U6PortCommon.WriteUtf8(dst, txt);
                    }
                    else
                    {
                        File.Copy(src, dst, false);
                    }

                    if (it.finalGuid != null)
                    {
                        var hadMeta = File.Exists(dst + ".meta");
                        U6PortCommon.EnsureMeta(dst, it.finalGuid);
                        if (!hadMeta) r.metaWritten++;
                    }

                    dirs.Add(U6PortCommon.Slash(Path.GetDirectoryName(it.targetRel)));  // targetRel 已带 Assets/ 前缀
                    if (it.action == Action.CopyRemap) r.copied++;
                    else r.copied++;

                    var cat = ext.TrimStart('.');
                    if (!r.byCategory.ContainsKey(cat)) r.byCategory[cat] = 0;
                    r.byCategory[cat]++;
                }
                catch (Exception e)
                {
                    r.failed++;
                    r.failures.Add(it.u6Rel + " : " + e.Message);
                }
            }
            EditorUtility.ClearProgressBar();

            // 目录 meta
            foreach (var d in dirs.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x.Length))
                U6PortCommon.EnsureFolderMeta(U6PortCommon.Slash(d));

            return r;
        }

        // --- 入口 -----------------------------------------------------------
        /// <summary>
        /// Dry-run：只做规划，不写任何文件。打印前 20 条目标完整路径 + 越界校验 + 统计，
        /// 供人工确认后再执行 Run()。
        /// </summary>
        public static void DryRun()
        {
            Directory.CreateDirectory(U6PortCommon.LogDir);
            Debug.Log("[ABPort] === U6 asset port DRY-RUN ===");
            Debug.Log("[ABPort] project    : " + Application.dataPath);
            BuildPlan();

            int copy = Plan.Count(x => x.action == Action.CopyAsIs || x.action == Action.CopyNew);
            int remapCount = Plan.Count(x => x.action == Action.CopyRemap);
            int skip = Plan.Count(x => x.action == Action.SkipSameBytes);
            int csCount = Plan.Count(x => x.action == Action.ConvertScript);

            // 越界统计
            int bad = 0;
            var badSamples = new List<string>();
            foreach (var it in Plan.Where(x => x.action != Action.SkipSameBytes))
            {
                try { AssertInsideAssets(it.targetRel, it.u6Rel); }
                catch (Exception e)
                {
                    bad++;
                    if (badSamples.Count < 10) badSamples.Add(it.u6Rel + " -> " + it.targetRel + " (" + e.Message + ")");
                }
            }

            long bytes = 0;
            foreach (var it in Plan.Where(x => x.action != Action.SkipSameBytes))
            {
                var p = Path.Combine(U6PortCommon.U6Root, it.u6Rel);
                if (File.Exists(p)) bytes += new FileInfo(p).Length;
            }

            Debug.Log($"[ABPort] DRY-RUN plan: total={Plan.Count} copy={copy} remap={remapCount} script={csCount} skip={skip}");
            Debug.Log($"[ABPort] DRY-RUN bytes to write = {bytes} ({bytes / 1024.0 / 1024.0:F1} MB)");

            var sample = Plan.Where(x => x.action != Action.SkipSameBytes).Take(20).ToList();
            for (int i = 0; i < sample.Count; i++)
            {
                var it = sample[i];
                var full = U6PortCommon.AbsPath(it.targetRel);
                Debug.Log($"[ABPort] DRY[{i + 1:00}] {it.action,-12} {it.u6Rel}  ->  {full}");
            }

            if (bad > 0)
            {
                Debug.LogError($"[ABPort] DRY-RUN: {bad} target(s) OUTSIDE Assets/ !");
                foreach (var s in badSamples) Debug.LogError("[ABPort]   " + s);
                Debug.Log("[ABPort] DRYRUN RESULT: FAIL");
                return;
            }
            Debug.Log("[ABPort] DRY-RUN: all targets inside Assets/ - safe to execute Run()");
            Debug.Log("[ABPort] DRYRUN RESULT: PASS");
        }

        public static void Run()
        {
            Directory.CreateDirectory(U6PortCommon.LogDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log("[ABPort] === U6 asset port begin ===");
            Debug.Log("[ABPort] project: " + Application.dataPath);

            BuildPlan();
            int copy = Plan.Count(x => x.action == Action.CopyAsIs || x.action == Action.CopyNew);
            int remapCount = Plan.Count(x => x.action == Action.CopyRemap);
            int skip = Plan.Count(x => x.action == Action.SkipSameBytes);
            int csCount = Plan.Count(x => x.action == Action.ConvertScript);
            Debug.Log($"[ABPort] plan: total={Plan.Count} copy={copy} remap={remapCount} script={csCount} skip={skip}");

            AssetDatabase.StartAssetEditing();
            Result r;
            try { r = Execute(); }
            finally { AssetDatabase.StopAssetEditing(); }

            // 写移植报告
            var sb = new StringBuilder();
            sb.AppendLine("# U6 -> AB2 资产移植报告");
            sb.AppendLine("生成时间：" + U6PortCommon.NowStamp());
            sb.AppendLine();
            sb.AppendLine("## 计划");
            sb.AppendLine($"- 计划条目: {r.planned}");
            sb.AppendLine($"- 已拷贝: {r.copied}");
            sb.AppendLine($"- 脚本转码(C#/UTF-8): {r.convertedScripts}");
            sb.AppendLine($"- 跳过(目标已存在): {r.skipped}");
            sb.AppendLine($"- guid 重新生成并改写引用: {r.guidRemapped}");
            sb.AppendLine($"- 新写 .meta: {r.metaWritten}");
            sb.AppendLine($"- 失败: {r.failed}");
            sb.AppendLine();
            sb.AppendLine("## 按扩展名");
            foreach (var kv in r.byCategory.OrderByDescending(x => x.Value))
                sb.AppendLine($"- .{kv.Key}: {kv.Value}");
            if (r.failures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 失败明细");
                foreach (var f in r.failures) sb.AppendLine("- " + f);
            }
            var reportPath = Path.Combine(U6PortCommon.LogDir, "u6_port_assets_report.md");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[ABPort] copied={r.copied} scripts={r.convertedScripts} skipped={r.skipped} remapped={r.guidRemapped} failed={r.failed}");
            Debug.Log("[ABPort] report -> " + reportPath);
            Debug.Log($"[ABPort] elapsed {sw.ElapsedMilliseconds} ms");
            Debug.Log(r.failed == 0 ? "[ABPort] PORT RESULT: PASS" : "[ABPort] PORT RESULT: FAIL");
        }
    }
}
#endif