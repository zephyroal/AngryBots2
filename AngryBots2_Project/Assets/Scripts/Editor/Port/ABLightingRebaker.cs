// ABLightingRebaker.cs
// -----------------------------------------------------------------------------
// 用 AB2 的 Linear 色彩空间重烘 U6 战役场景的 lightmap（U6 原烘焙是 Gamma 空间）。
// 用法：-executeMethod ABPort.ABLightingRebaker.RebakeAll
// 低采样设置优先保证 batchmode 可在合理时间内完成；可在 Inspector 手动重烘更高质量。
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ABPort
{
    public static class ABLightingRebaker
    {
        const string LogDir = @"E:\MyPlayGround\_logs";

        // 小场景先烘，AngryBots 最后（对象/灯光最多）。
        static readonly string[] Scenes =
        {
            "Assets/U6Port/Scenes/Default.unity",
            "Assets/U6Port/Scenes/Escape.unity",
            "Assets/U6Port/Scenes/3-4_Birth.unity",
            "Assets/U6Port/Scenes/3-4_Pain.unity",
            "Assets/U6Port/Scenes/AngryBots.unity",
        };

        [MenuItem("ABPort/Rebake Campaign Lighting (Linear)")]
        public static void RebakeAll()
        {
            Directory.CreateDirectory(LogDir);
            var report = new StringBuilder();
            report.AppendLine("=== 战役场景 lightmap 重烘（Linear）=== " + DateTime.Now);

            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

            foreach (var path in Scenes)
            {
                if (!File.Exists(path))
                {
                    report.AppendLine("[skip] " + path);
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                ApplyLowQualitySettings();

                var watch = Stopwatch.StartNew();
                report.AppendLine();
                report.AppendLine("## " + Path.GetFileName(path) + " 开始 " + DateTime.Now.ToString("HH:mm:ss"));
                Debug.Log("[ABLighting] 开始烘焙 " + path);

                bool ok = Lightmapping.Bake();
                watch.Stop();

                report.AppendLine("  结果=" + (ok ? "OK" : "FAILED") + "  用时=" + watch.Elapsed.TotalMinutes.ToString("0.0") + " 分钟");

                if (ok)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene, path);
                    report.AppendLine("  已保存场景");
                }

                File.WriteAllText(Path.Combine(LogDir, "ab_lighting_rebake.md"), report.ToString(), new UTF8Encoding(false));
            }

            report.AppendLine();
            report.AppendLine("LIGHTING RESULT: DONE");
            File.WriteAllText(Path.Combine(LogDir, "ab_lighting_rebake.md"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        static void ApplyLowQualitySettings()
        {
            // 2022.3 仍提供 LightmapEditorSettings 静态配置入口。
            LightmapEditorSettings.lightmapper = LightmapEditorSettings.Lightmapper.ProgressiveCPU;
            LightmapEditorSettings.directSampleCount = 4;
            LightmapEditorSettings.indirectSampleCount = 32;
            LightmapEditorSettings.environmentSampleCount = 32;
            LightmapEditorSettings.bounces = 1;
            LightmapEditorSettings.filteringMode = LightmapEditorSettings.FilterMode.None;
            LightmapEditorSettings.textureCompression = false;

            // 分辨率/图集大小在 2022 移到 LightingSettings 资产上。
            var settings = Lightmapping.lightingSettings;
            if (settings != null)
            {
                settings.lightmapResolution = 12f;
                settings.lightmapMaxSize = 1024;
                EditorUtility.SetDirty(settings);
            }
        }
    }
}
#endif