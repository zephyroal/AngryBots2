using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// Play 模式取景：用真实 Game View 截图（含 HUD），统计平均亮度，用于确认运行时画面亮度。
	/// 用法（GUI 模式，不要加 -batchmode/-quit）：-executeMethod ABGameplay.VisualPlayProbe.Run
	/// 进入 Play 会 domain reload，静态状态靠 SessionState 续跑。
	/// </summary>
	public static class VisualPlayProbe
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string OutDir = @"E:\MyPlayGround\_logs\shots";
		const string ShotPath = OutDir + @"\play_scene_now.png";
		const string SessionKey = "ABGameplay.VisualPlayProbe.Active";

		static float startedAt;
		static int stage;

		public static void Run()
		{
			Directory.CreateDirectory(OutDir);
			SessionState.SetBool(SessionKey, true);
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
			EditorApplication.EnterPlaymode();
		}

		[InitializeOnLoadMethod]
		static void ResumeAfterDomainReload()
		{
			if (!SessionState.GetBool(SessionKey, false)) return;
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
		}

		static void Tick()
		{
			if (!EditorApplication.isPlaying) return;

			if (startedAt <= 0f)
			{
				startedAt = Time.realtimeSinceStartup;
				stage = 0;
				return;
			}

			float elapsed = Time.realtimeSinceStartup - startedAt;

			if (stage == 0 && elapsed > 2.5f)
			{
				stage = 1;
				if (File.Exists(ShotPath)) File.Delete(ShotPath);
				ScreenCapture.CaptureScreenshot(ShotPath);
				Debug.Log("[VP] capture requested");
			}
			else if (stage == 1 && elapsed > 4f)
			{
				stage = 2;
				bool exists = File.Exists(ShotPath);
				Debug.Log("[VP] shot exists=" + exists + " path=" + ShotPath);

				if (exists)
				{
					try
					{
						var bytes = File.ReadAllBytes(ShotPath);
						var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
						tex.LoadImage(bytes);
						var pixels = tex.GetPixels32();
						long sum = 0;
						for (int i = 0; i < pixels.Length; i++)
							sum += (pixels[i].r * 299 + pixels[i].g * 587 + pixels[i].b * 114) / 1000;
						Debug.Log("[VP] size=" + tex.width + "x" + tex.height +
							" meanLuma=" + ((float)sum / pixels.Length).ToString("0.0"));
						Object.DestroyImmediate(tex);
					}
					catch (System.Exception e) { Debug.LogError("[VP] read failed: " + e); }
				}

				SessionState.SetBool(SessionKey, false);
				EditorApplication.update -= Tick;
				EditorApplication.Exit(0);
			}
		}
	}
}
