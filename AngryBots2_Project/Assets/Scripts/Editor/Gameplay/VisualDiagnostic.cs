using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 视觉诊断：把玩法场景与爆炸特效离屏渲染成 PNG，用于人工比对亮度与透明边缘。
	/// 用法：-executeMethod ABGameplay.VisualDiagnostic.Run
	/// </summary>
	public static class VisualDiagnostic
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string OutDir = @"E:\MyPlayGround\_logs\shots";
		const string ExplodePrefab = "Assets/Prefabs/Particles/Particles_EnemySpider_Explode_Prefab.prefab";

		public static void Run()
		{
			Directory.CreateDirectory(OutDir);

			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			var cam = Camera.main;
			if (cam == null) cam = Object.FindObjectOfType<Camera>();
			if (cam == null) { Debug.LogError("[VD] no camera"); EditorApplication.Exit(1); return; }

			Debug.Log("[VD] camera=" + cam.name + " pos=" + cam.transform.position + " rot=" + cam.transform.eulerAngles);
			Capture(cam, "diag_scene_now.png");

			// A/B：关掉所有走粒子加法 shader 的渲染器，量化它们对整帧亮度的贡献
			int disabled = 0;
			foreach (var r in Object.FindObjectsOfType<ParticleSystemRenderer>(true))
			{
				if (r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
				string sn = r.sharedMaterial.shader.name;
				if (sn != "AngryBots/Particle/Additive" && sn != "AngryBots/Particle/AdditiveBlend") continue;
				r.enabled = false;
				disabled++;
			}
			Debug.Log("[VD] additive particle renderers disabled=" + disabled);
			Capture(cam, "diag_scene_no_additive.png");
			foreach (var r in Object.FindObjectsOfType<ParticleSystemRenderer>(true)) r.enabled = true;

			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExplodePrefab);
			if (prefab == null) { Debug.LogError("[VD] explosion prefab missing"); EditorApplication.Exit(1); return; }

			var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
			instance.transform.position = cam.transform.position + cam.transform.forward * 7f;
			instance.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

			int simulated = 0;
			foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
			{
				if (!ps.gameObject.activeInHierarchy) continue;
				ps.Simulate(0.12f, false, true);
				ps.Pause(true);
				simulated++;
			}
			Debug.Log("[VD] explosion systems simulated=" + simulated);

			Capture(cam, "diag_explosion.png");

			Object.DestroyImmediate(instance);
			EditorApplication.Exit(0);
		}

		/// <summary>AB1 场景（Assets/AngryBots.unity）当前画面，用于和历史截图对比整体亮度。</summary>
		public static void RunAb1()
		{
			Directory.CreateDirectory(OutDir);
			EditorSceneManager.OpenScene("Assets/AngryBots.unity", OpenSceneMode.Single);

			var cam = Camera.main;
			if (cam == null) cam = Object.FindObjectOfType<Camera>();
			if (cam == null) { Debug.LogError("[VD] no camera in AB1"); EditorApplication.Exit(1); return; }

			Capture(cam, "diag_ab1_now.png");
			EditorApplication.Exit(0);
		}

		static void Capture(Camera cam, string fileName)
		{
			const int w = 1920, h = 1080;
			var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
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

				string path = Path.Combine(OutDir, fileName);
				File.WriteAllBytes(path, tex.EncodeToPNG());

				// 平均亮度（0~255），便于和历史基线对比
				var pixels = tex.GetPixels32();
				long sum = 0;
				for (int i = 0; i < pixels.Length; i++)
					sum += (pixels[i].r * 299 + pixels[i].g * 587 + pixels[i].b * 114) / 1000;
				float mean = (float)sum / pixels.Length;

				Debug.Log("[VD] " + fileName + " meanLuma=" + mean.ToString("0.0") + " -> " + path);
				Object.DestroyImmediate(tex);
			}
			finally
			{
				cam.targetTexture = prevTarget;
				RenderTexture.active = prevActive;
				rt.Release();
				Object.DestroyImmediate(rt);
			}
		}
	}
}
