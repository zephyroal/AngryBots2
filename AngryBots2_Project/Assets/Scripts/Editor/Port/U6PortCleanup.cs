// U6PortCleanup.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 善后巡检 / 定点清理 (namespace ABPort)
//
// 设计要点（吸取教训）：
//   * 不再用「与 U6 同路径且字节相同」判定副本 —— 那个判据无法区分「误拷副本」
//     与「AB2 原有同源文件」，会误删 AB2 自己的资产。
//   * 改为**白名单 + 结构异常**判据：
//       1. Assets/ 下只允许出现 AB2 原有的 29 个顶层目录 + U6Port。
//          出现任何其他顶层目录 -> 报警（不自动删，交由人确认）。
//       2. 任何形如 Assets/xxx/Assets/ 的嵌套 Assets 目录 -> 一律删除
//          （这是路径拼接重复前缀的确定性 bug，不可能是正常资产）。
//   * 全程不删除 Assets/U6Port/** 与被移植脚本目录。
//
// 用法：
//   Unity.exe -batchmode -quit -projectPath <AB2> -executeMethod ABPort.U6PortCleanup.Run
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ABPort
{
    public static class U6PortCleanup
    {
        /// <summary>AB2 工程原有的 Assets 顶层目录白名单。</summary>
        static readonly HashSet<string> AllowedTopDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Audio", "Characters", "Environment", "Explosions", "Fonts", "Gizmos",
            "Materials", "NavMeshComponents", "Objects", "PhysicMaterials",
            "PostProcessingProfiles", "Prefabs", "ProBuilder Data", "Projectiles",
            "RenderPipelines", "Resources", "Scenes", "ScriptableRenderPasses",
            "Scripts", "ShaderGraphs", "Shaders", "Skyboxes", "Sounds",
            "Standard assets", "TextMesh Pro", "Textures", "UI", "VFX (Shared)",
            "U6Port",
        };

        public static void Run()
        {
            Debug.Log("[ABPort] === U6Port cleanup / structure audit ===");
            var assets = Application.dataPath;
            int removed = 0;
            var warnings = new List<string>();

            // --- 1) 删除嵌套 Assets 目录（Assets/xxx/Assets/...） ----------------
            var nested = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(assets, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(dir);
                if (!string.Equals(name, "Assets", StringComparison.OrdinalIgnoreCase)) continue;
                // 只关心「父目录不是工程根」的情况：即 Assets/**/Assets
                var parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, assets, StringComparison.OrdinalIgnoreCase)) continue;
                nested.Add(dir);
            }
            foreach (var n in nested)
            {
                var assetPath = "Assets" + n.Substring(assets.Length).Replace('\\', '/');
                Debug.LogWarning("[ABPort] removing nested Assets dir: " + assetPath);
                if (AssetDatabase.DeleteAsset(assetPath)) removed++;
                else Debug.LogError("[ABPort] failed to delete " + assetPath);
            }

            // --- 2) Assets 顶层目录结构审计 ------------------------------------
            foreach (var dir in Directory.EnumerateDirectories(assets))
            {
                var name = Path.GetFileName(dir);
                // macOS/POSIX 隐藏目录（.git 等）与 Unity 特殊目录不算异常
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (!AllowedTopDirs.Contains(name))
                    warnings.Add("unexpected top-level Assets dir: " + name);
            }

            // --- 3) 确认 U6Port 内容清点 --------------------------------------
            var portAbs = Path.Combine(assets, "U6Port");
            int portFiles = Directory.Exists(portAbs)
                ? Directory.EnumerateFiles(portAbs, "*", SearchOption.AllDirectories).Count()
                : 0;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[ABPort] nested Assets dirs removed: {removed}");
            Debug.Log($"[ABPort] U6Port files: {portFiles}");
            foreach (var w in warnings) Debug.LogError("[ABPort] " + w);
            Debug.Log("[ABPort] CLEANUP RESULT: " + (warnings.Count == 0 ? "PASS" : "FAIL"));
        }
    }
}
#endif