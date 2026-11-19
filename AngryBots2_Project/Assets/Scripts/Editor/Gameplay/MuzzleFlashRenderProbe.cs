using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 枪口特效多时间点离屏渲染：亮灰底 + 依次推进 0.05/0.10/0.20s，任何压暗面片都会显现。
	/// 用法：-executeMethod ABGameplay.MuzzleFlashRenderProbe.RunTimeline
	/// </summary>
	public static class MuzzleFlashRenderProbe
	{
		const string PrefabPath = "Assets/Prefabs/Particles/Particles_Gun_MuzzleFlash_Prefab.prefab";
		const string OutputDir = "Logs";

		[MenuItem("ABGameplay/15b. Muzzle Flash Timeline Probe")]
		public static void RunTimeline()
		{
			RenderTimeline();
			Debug.Log("[Probe] DONE");
			EditorApplication.Exit(0);
		}

		static void RenderTimeline()
		{
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			if (prefab == null) { Debug.LogError("[Probe] 找不到预制体"); return; }

			var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
			instance.transform.position = Vector3.zero;

			var cameraObject = new GameObject("ProbeCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.75f, 0.75f, 0.75f, 1f);
			camera.transform.position = new Vector3(0f, 0f, -1.5f);
			camera.transform.rotation = Quaternion.identity;
			camera.fieldOfView = 60f;
			camera.allowHDR = false;

			foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);

			float[] marks = { 0.05f, 0.10f, 0.20f };
			float simulated = 0f;
			int index = 0;

			foreach (var mark in marks)
			{
				while (simulated < mark - 0.0001f)
				{
					float step = Mathf.Min(0.02f, mark - simulated);
					foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
					{
						if (!ps.gameObject.activeInHierarchy) continue;
						ps.Simulate(step, true, true);
					}
					simulated += step;
				}

				Capture(camera, "muzzle_t" + index + "_" + Mathf.RoundToInt(mark * 100f) + "ms.png");
				int total = 0;
				foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) total += ps.particleCount;
				Debug.Log("[Probe] t=" + mark + "s particles=" + total);
				index++;
			}
		}

		static void Capture(Camera camera, string fileName)
		{
			var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
			camera.targetTexture = rt;
			camera.Render();

			RenderTexture.active = rt;
			var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
			tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
			tex.Apply();
			RenderTexture.active = null;
			camera.targetTexture = null;

			Directory.CreateDirectory(OutputDir);
			string path = Path.Combine(OutputDir, fileName);
			File.WriteAllBytes(path, tex.EncodeToPNG());
			Debug.Log("[Probe] WROTE " + path);

			Object.DestroyImmediate(tex);
			Object.DestroyImmediate(rt);
		}
	}
}
