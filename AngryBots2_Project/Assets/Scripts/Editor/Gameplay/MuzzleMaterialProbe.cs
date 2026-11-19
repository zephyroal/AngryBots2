using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 材质亮度探针：把每个材质铺在灰底上渲染，输出全屏平均色。
	/// 若某材质把 0.75 的灰底压暗，说明它是乘法/深色 alpha 混合——枪口“黑底色块”的可疑来源。
	/// 用法：-executeMethod ABGameplay.MuzzleMaterialProbe.Run
	/// </summary>
	public static class MuzzleMaterialProbe
	{
		static readonly string[] Materials =
		{
			"Assets/Objects/Player/Materials/muzzleFlash-lambert21.mat",
			"Assets/VFX (Shared)/Materials/Flame_01.mat",
			"Assets/VFX (Shared)/Materials/Particle_01.mat",
			"Assets/VFX (Shared)/Materials/Particle_03.mat",
			"Assets/VFX (Shared)/Materials/MuzzleFlash_Core.mat",
			"Assets/VFX (Shared)/Materials/Smoke_Order_01.mat",
			"Assets/Materials/Decal.mat",
			"Assets/Materials/Rain.mat",
		};

		[MenuItem("ABGameplay/17. Muzzle Material Probe")]
		public static void Run()
		{
			foreach (var path in Materials) Probe(path);
			Debug.Log("[MatProbe] DONE");
			EditorApplication.Exit(0);
		}

		static void Probe(string materialPath)
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
			if (material == null) { Debug.LogWarning("[MatProbe] 找不到 " + materialPath); return; }

			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			var cameraObject = new GameObject("ProbeCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.75f, 0.75f, 0.75f, 1f);
			camera.transform.position = new Vector3(0f, 0f, -2f);
			camera.transform.rotation = Quaternion.identity;
			camera.fieldOfView = 60f;
			camera.allowHDR = false;

			var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			quad.transform.position = Vector3.zero;
			quad.transform.localScale = Vector3.one * 2f;
			var renderer = quad.GetComponent<Renderer>();
			renderer.sharedMaterial = material;

			var rt = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
			camera.targetTexture = rt;
			camera.Render();

			RenderTexture.active = rt;
			var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
			tex.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
			tex.Apply();
			RenderTexture.active = null;
			camera.targetTexture = null;

			var pixels = tex.GetPixels();
			float r = 0f, g = 0f, b = 0f;
			foreach (var pixel in pixels) { r += pixel.r; g += pixel.g; b += pixel.b; }
			float count = Mathf.Max(1, pixels.Length);
			Debug.Log("[MatProbe] " + Path.GetFileNameWithoutExtension(materialPath)
				+ " shader=" + (material.shader != null ? material.shader.name : "null")
				+ " avgRGB=" + (r / count).ToString("0.000") + "/" + (g / count).ToString("0.000") + "/" + (b / count).ToString("0.000"));

			Object.DestroyImmediate(tex);
			Object.DestroyImmediate(rt);
		}
	}
}
