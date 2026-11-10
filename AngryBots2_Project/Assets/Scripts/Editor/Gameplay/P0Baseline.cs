using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABGameplay
{
	/// <summary>
	/// P0 基线快照：在任何移植/改造动作之前，记录 AngryBots2 的可复现状态。
	/// 只读，不修改任何资产。
	/// 用法（batchmode）: -executeMethod ABGameplay.P0Baseline.Run
	/// </summary>
	public static class P0Baseline
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string LogDir = @"E:\MyPlayGround\_logs";
		const string ShotDir = @"E:\MyPlayGround\_logs\shots";

		static readonly StringBuilder Report = new StringBuilder();
		static readonly List<string> Failures = new List<string>();

		public static void Run()
		{
			Directory.CreateDirectory(LogDir);
			Directory.CreateDirectory(ShotDir);

			Report.AppendLine("# P0 Baseline Report");
			Report.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Report.AppendLine();

			try
			{
				ReportAssetStats();
				ReportBrokenMaterials();
				ReportSceneStats();
				CaptureScreenshot();
			}
			catch (Exception e)
			{
				Failures.Add("Exception: " + e);
				Debug.LogException(e);
			}

			WriteReport();

			if (Failures.Count == 0)
			{
				Debug.Log("P0 RESULT: PASS");
			}
			else
			{
				Debug.Log("P0 RESULT: FAIL");
				foreach (var f in Failures) Debug.LogError("[P0] " + f);
			}
		}

		static void ReportAssetStats()
		{
			var mats = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
			var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
			var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
			var shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" });
			var graphs = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(p => p.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
				.Distinct().Count();
			var scripts = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
			var textures = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
			var audio = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });

			Report.AppendLine("## Asset counts");
			Report.AppendLine("- Materials: " + mats.Length);
			Report.AppendLine("- Prefabs: " + prefabs.Length);
			Report.AppendLine("- Scenes: " + scenes.Length);
			Report.AppendLine("- Shaders (.shader + generated): " + shaders.Length);
			Report.AppendLine("- ShaderGraph assets: " + graphs);
			Report.AppendLine("- MonoScripts: " + scripts.Length);
			Report.AppendLine("- Textures: " + textures.Length);
			Report.AppendLine("- AudioClips: " + audio.Length);
			Report.AppendLine();

			Debug.Log("[P0] Materials=" + mats.Length + " Prefabs=" + prefabs.Length +
				" Scenes=" + scenes.Length + " Shaders=" + shaders.Length +
				" ShaderGraphs=" + graphs + " Scripts=" + scripts.Length);

			// 按 shader 分组
			var groups = new Dictionary<string, int>();
			var nullShader = 0;
			foreach (var guid in mats)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (mat == null) { nullShader++; continue; }
				string key = mat.shader == null ? "<NULL SHADER>" : mat.shader.name;
				if (string.IsNullOrEmpty(key) || key == "Hidden/InternalErrorShader") key = "<MISSING: " + key + ">";
				groups[key] = groups.TryGetValue(key, out var c) ? c + 1 : 1;
			}

			Report.AppendLine("## Materials grouped by shader");
			foreach (var kv in groups.OrderByDescending(k => k.Value))
				Report.AppendLine("- " + kv.Value + "\t" + kv.Key);
			if (nullShader > 0) Report.AppendLine("- " + nullShader + "\t<UNLOADABLE MATERIAL>");
			Report.AppendLine();
		}

		static void ReportBrokenMaterials()
		{
			var mats = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
			var broken = new List<string>();

			foreach (var guid in mats)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (mat == null) { broken.Add(path + "  [unloadable]"); continue; }
				if (mat.shader == null)
				{
					broken.Add(path + "  [null shader]");
					continue;
				}
				var n = mat.shader.name;
				if (n == "Hidden/InternalErrorShader" || n.Contains("InternalError"))
					broken.Add(path + "  [InternalErrorShader]");
			}

			Report.AppendLine("## Broken / missing-shader materials (" + broken.Count + ")");
			foreach (var b in broken.OrderBy(x => x)) Report.AppendLine("- " + b);
			Report.AppendLine();
			Debug.Log("[P0] broken materials: " + broken.Count);
		}

		static void ReportSceneStats()
		{
			if (!File.Exists(ScenePath))
			{
				Failures.Add("scene missing: " + ScenePath);
				return;
			}

			var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			var roots = scene.GetRootGameObjects();
			int gameObjects = roots.Sum(r => r.GetComponentsInChildren<Transform>(true).Length);
			var enemies = UnityEngine.Object.FindObjectsOfType<EnemyHealth>();
			var player = GameObject.FindGameObjectWithTag("Player");
			var director = UnityEngine.Object.FindObjectOfType<WaveDirector>();
			var hud = UnityEngine.Object.FindObjectOfType<GameHud>();
			var lights = UnityEngine.Object.FindObjectsOfType<Light>();
			var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
			var navMeshSurfaces = UnityEngine.Object.FindObjectsOfType<Unity.AI.Navigation.NavMeshSurface>();

			Report.AppendLine("## Scene: " + ScenePath);
			Report.AppendLine("- Root objects: " + roots.Length);
			Report.AppendLine("- Total GameObjects: " + gameObjects);
			Report.AppendLine("- EnemyHealth instances: " + enemies.Length);
			Report.AppendLine("- Player (tag): " + (player != null ? player.name : "<none>"));
			Report.AppendLine("- WaveDirector: " + (director != null ? "present" : "<none>"));
			Report.AppendLine("- GameHud: " + (hud != null ? "present" : "<none>"));
			Report.AppendLine("- Lights: " + lights.Length);
			Report.AppendLine("- MeshRenderers: " + renderers.Length);
			Report.AppendLine("- NavMeshSurfaces: " + navMeshSurfaces.Length);
			Report.AppendLine();

			Debug.Log("[P0] scene GOs=" + gameObjects + " enemies=" + enemies.Length +
				" player=" + (player != null) + " director=" + (director != null) + " hud=" + (hud != null));

			// 这些是「差异记录」而非致命失败：P0 阶段只要求如实记录基线。
			// 若缺失，会在上面 Report 中留下痕迹，供后续阶段对照。
			Debug.Log("[P0] scene sanity: player=" + (player != null) + " director=" + (director != null) + " hud=" + (hud != null));
		}

		static void CaptureScreenshot()
		{
			var cam = Camera.main;
			if (cam == null)
			{
				Failures.Add("no main camera for screenshot");
				return;
			}

			const int w = 1920, h = 1080;
			var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
			rt.antiAliasing = 1;
			var prevTarget = cam.targetTexture;
			var prevActive = RenderTexture.active;

			try
			{
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;

				var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
				tex.Apply();

				var outPath = Path.Combine(ShotDir, "ab2_p0_baseline.png");
				File.WriteAllBytes(outPath, tex.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(tex);

				Report.AppendLine("## Screenshot");
				Report.AppendLine("- " + outPath + " (camera: " + cam.name + ")");
				Debug.Log("[P0] screenshot -> " + outPath);
			}
			finally
			{
				cam.targetTexture = prevTarget;
				RenderTexture.active = prevActive;
				rt.Release();
				UnityEngine.Object.DestroyImmediate(rt);
			}
		}

		static void WriteReport()
		{
			var path = Path.Combine(LogDir, "ab2_p0_baseline_report.md");
			File.WriteAllText(path, Report.ToString(), new UTF8Encoding(false));
			Debug.Log("[P0] report -> " + path);
		}
	}
}