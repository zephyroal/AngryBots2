// U6SceneValidator.cs
// -----------------------------------------------------------------------------
// U6 战役场景移植验证 + NavMesh 烘焙 + Build Settings 注册。
// 用法（batchmode）:
//   -executeMethod ABPort.U6SceneValidator.ValidateAll
//   -executeMethod ABPort.U6SceneValidator.BakeNavMeshAll
//   -executeMethod ABPort.U6SceneValidator.RegisterBuildSettings
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace ABPort
{
    public static class U6SceneValidator
    {
        const string SceneDir = "Assets/U6Port/Scenes";
        static readonly string[] Scenes =
        {
            SceneDir + "/Default.unity",
            SceneDir + "/3-4_Birth.unity",
            SceneDir + "/3-4_Pain.unity",
            SceneDir + "/AngryBots.unity",
            SceneDir + "/Escape.unity",
        };

        const string LogDir = @"E:\MyPlayGround\_logs";

        [MenuItem("ABPort/Validate U6 Scenes")]
        public static void ValidateAll()
        {
            Directory.CreateDirectory(LogDir);
            var report = new StringBuilder();
            int failures = 0;

            report.AppendLine("=== U6 场景移植验证 ===" + DateTime.Now);
            foreach (var path in Scenes)
            {
                if (!File.Exists(path))
                {
                    report.AppendLine("[FAIL] 场景不存在: " + path);
                    failures++;
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                report.AppendLine();
                report.AppendLine("## " + Path.GetFileName(path));

                // 1) 缺失脚本
                var missingScripts = new List<string>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var comps = t.GetComponents<Component>();
                        bool missing = comps.Any(c => c == null);
                        if (missing) missingScripts.Add(GetPath(t));
                    }
                }
                report.AppendLine("  missing-script objects: " + missingScripts.Count);
                if (missingScripts.Count > 0)
                {
                    failures++;
                    foreach (var m in missingScripts.Take(20)) report.AppendLine("    - " + m);
                }

                // 2) 材质 / shader 支持
                var shaders = new Dictionary<string, int>();
                var unsupported = new Dictionary<string, int>();
                foreach (var r in Object.FindObjectsOfType<Renderer>(true))
                {
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null || mat.shader == null) continue;
                        string name = mat.shader.name;
                        shaders[name] = shaders.TryGetValue(name, out var c) ? c + 1 : 1;
                        if (!IsUrpSupported(mat.shader))
                            unsupported[name] = unsupported.TryGetValue(name, out var u) ? u + 1 : 1;
                    }
                }
                report.AppendLine("  distinct shaders: " + shaders.Count);
                report.AppendLine("  unsupported shader names: " + unsupported.Count);
                if (unsupported.Count > 0)
                {
                    failures++;
                    foreach (var kv in unsupported.OrderByDescending(k => k.Value))
                        report.AppendLine("    ! " + kv.Key + "  x" + kv.Value);
                }

                // 3) NavMesh
                var tri = NavMesh.CalculateTriangulation();
                report.AppendLine("  navmesh vertices: " + tri.vertices.Length + ", indices: " + tri.indices.Length);
                if (tri.vertices.Length == 0)
                {
                    report.AppendLine("  [warn] 无 NavMesh（需要 BakeNavMeshAll）");
                }

                // 4) 统计
                var roots = scene.GetRootGameObjects();
                int objects = 0, renderers = 0, lights = 0;
                foreach (var root in roots)
                {
                    objects += root.GetComponentsInChildren<Transform>(true).Length;
                    renderers += root.GetComponentsInChildren<Renderer>(true).Length;
                    lights += root.GetComponentsInChildren<Light>(true).Length;
                }
                report.AppendLine("  objects=" + objects + " renderers=" + renderers + " lights=" + lights);

                // 5) 玩家 / 敌人 / 关键脚本
                report.AppendLine("  PlayerHealth=" + Object.FindObjectsOfType<PlayerHealth>(true).Length
                    + " EnemyHealth=" + Object.FindObjectsOfType<EnemyHealth>(true).Length
                    + " EnemyPatrol=" + Object.FindObjectsOfType<EnemyPatrol>(true).Length
                    + " WaveDirector=" + Object.FindObjectsOfType<WaveDirector>(true).Length);
            }

            report.AppendLine();
            report.AppendLine(failures == 0 ? "U6 SCENE RESULT: PASS" : "U6 SCENE RESULT: FAIL (" + failures + ")");
            var outPath = Path.Combine(LogDir, "u6_scene_validation.md");
            File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
            if (failures > 0) throw new Exception("[ABPort] scene validation failed: " + failures);
        }

        static bool IsUrpSupported(Shader shader)
        {
            string n = shader.name;
            if (string.IsNullOrEmpty(n)) return false;
            if (n.Contains("InternalErrorShader")) return false;
            if (n.StartsWith("Universal Render Pipeline/")) return true;
            if (n.StartsWith("AngryBots/FX/")) return true;             // 已改写为 URP HLSL
            if (n.StartsWith("AngryBots/")) return true;                // Assets/Shaders 下已改写为 URP
            if (n.StartsWith("AngryBots/Urp")) return true;
            if (n.StartsWith("Self-Illumin/AngryBots/")) return true;   // 已改写为 URP
            if (n.StartsWith("Graphs/")) return true;                  // Shader Graph（URP target）
            if (n.StartsWith("TextMeshPro/")) return true;
            if (n.StartsWith("GUI/") || n.StartsWith("UI/") || n.StartsWith("Sprites/")) return true;
            if (n.StartsWith("Skybox/")) return true;
            if (n.StartsWith("Hidden/")) return true;
            return false;
        }

        static string GetPath(Transform t)
        {
            var stack = new List<string>();
            while (t != null) { stack.Add(t.name); t = t.parent; }
            stack.Reverse();
            return string.Join("/", stack);
        }

        [MenuItem("ABPort/Fix Missing Materials")]
        public static void FixMissingMaterials()
        {
            const string DefaultMatPath = "Assets/U6Port/Materials/ABDefaultUrp.mat";
            var defaultMat = AssetDatabase.LoadAssetAtPath<Material>(DefaultMatPath);
            if (defaultMat == null)
            {
                defaultMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                defaultMat.name = "ABDefaultUrp";
                defaultMat.color = new Color(0.55f, 0.55f, 0.55f, 1f);
                AssetDatabase.CreateAsset(defaultMat, DefaultMatPath);
                Debug.Log("[ABPort] 创建默认材质 " + DefaultMatPath);
            }

            var report = new StringBuilder();
            report.AppendLine("=== 修复空/内置材质槽 ===" + DateTime.Now);

            foreach (var path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int fixedSlots = 0;

                foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var mat = materials[i];
                        bool builtinDefault = mat != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat));
                        bool legacyDefault = mat != null && mat.shader != null &&
                            mat.shader.name == "Legacy Shaders/Diffuse" && mat.mainTexture == null;
                        if (mat == null || builtinDefault || legacyDefault || (mat != null && mat.name == "Default-Material"))
                        {
                            materials[i] = defaultMat;
                            changed = true;
                            fixedSlots++;
                        }
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }

                report.AppendLine(Path.GetFileName(path) + ": fixed slots=" + fixedSlots);
                if (fixedSlots > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene, path);
                }
            }

            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(LogDir, "u6_scene_material_fix.md"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        [MenuItem("ABPort/Bake NavMesh All")]
        public static void BakeNavMeshAll()
        {
            Directory.CreateDirectory(LogDir);
            var report = new StringBuilder();
            report.AppendLine("=== 战役场景 NavMesh 烘焙 ===" + DateTime.Now);

            foreach (var path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                // 清掉旧的运行时 NavMesh 组件（如果有）
                UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

                var tri = NavMesh.CalculateTriangulation();
                report.AppendLine(Path.GetFileName(path) + ": vertices=" + tri.vertices.Length + " indices=" + tri.indices.Length);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, path);
            }

            var outPath = Path.Combine(LogDir, "u6_navmesh_bake.md");
            File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        [MenuItem("ABPort/Register U6 Scenes In Build Settings")]
        public static void RegisterBuildSettings()
        {
            var list = EditorBuildSettings.scenes.ToList();
            foreach (var path in Scenes)
            {
                if (!File.Exists(path)) continue;
                if (list.Any(s => s.path == path)) continue;
                list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[ABPort] build settings scenes=" + EditorBuildSettings.scenes.Length);
        }

        /// <summary>一次性完成：修材质槽 → 烘 NavMesh → 注册 Build Settings → 验证。</summary>
        [MenuItem("ABPort/Port U6 Scenes (All Steps)")]
        public static void RunAll()
        {
            FixMissingMaterials();
            BakeNavMeshAll();
            RegisterBuildSettings();
            ValidateAll();
        }

        [MenuItem("ABPort/Clean Missing Scripts")]
        public static void CleanupMissingScripts()
        {
            var report = new StringBuilder();
            report.AppendLine("=== 清理缺失脚本组件 ===" + DateTime.Now);

            var all = new List<string>(Scenes) { "Assets/Scenes/Scenes/_CompletedLevel.unity" };
            foreach (var path in all)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int removed = 0;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = transform.gameObject;
                        if (!go.GetComponents<Component>().Any(c => c == null)) continue;
                        removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    }
                }

                report.AppendLine(Path.GetFileName(path) + ": removed=" + removed);
                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene, path);
                }
            }

            File.WriteAllText(Path.Combine(LogDir, "ab_missing_scripts_cleanup.md"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        [MenuItem("ABPort/Fix Materials And Validate")]
        public static void FixAndValidate()
        {
            FixMissingMaterials();
            ValidateAll();
        }
    }
}
#endif