using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 遮挡剔除烘焙：给静态物件补 Occluder/Occludee 标记，显式设置 Umbra 参数后同步烘焙。
	/// 用法：Tuanjie.exe -batchmode -buildTarget StandaloneWindows64 -projectPath &lt;工程&gt;
	///        -executeMethod ABGameplay.OcclusionBake.Run -logFile Logs/occlusion.log
	/// </summary>
	public static class OcclusionBakeGui
	{
		static readonly string[] Scenes =
		{
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
		};

		[MenuItem("ABGameplay/20. Bake Occlusion")]
		public static void Run()
		{
			foreach (var path in Scenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid())
				{
					Debug.LogWarning("[Occ] 打不开 " + path);
					continue;
				}

				int flagged = 0;
				int occluders = 0;
				foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>(true))
				{
					if (renderer == null) continue;
					var target = renderer.gameObject;
					var flags = GameObjectUtility.GetStaticEditorFlags(target);
					if (flags == 0) continue;

					var want = flags | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
					if (want != flags)
					{
						GameObjectUtility.SetStaticEditorFlags(target, want);
						flagged++;
					}
					if ((want & StaticEditorFlags.OccluderStatic) != 0) occluders++;
				}

				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);

				// 显式给一组保守的 Umbra 参数，避免场景里缺失设置时烘焙被跳过
				try
				{
					StaticOcclusionCulling.smallestOccluder = 2f;
					StaticOcclusionCulling.smallestHole = 0.25f;
					StaticOcclusionCulling.backfaceThreshold = 100f;
				}
				catch (System.Exception exception)
				{
					Debug.LogWarning("[Occ] 设置烘焙参数失败：" + exception.Message);
				}

				double started = EditorApplication.timeSinceStartup;
				bool ok;
				try
				{
					ok = StaticOcclusionCulling.Compute();
				}
				catch (System.Exception exception)
				{
					Debug.LogError("[Occ] 烘焙异常 " + path + " : " + exception.Message);
					continue;
				}

				EditorSceneManager.SaveScene(scene);
				Debug.Log("[Occ] BAKE " + Path.GetFileName(path)
					+ " ok=" + ok
					+ " occluders=" + occluders
					+ " flaggedNew=" + flagged
					+ " umbraKB=" + (StaticOcclusionCulling.umbraDataSize / 1024)
					+ " seconds=" + (EditorApplication.timeSinceStartup - started).ToString("0.0"));
			}

			AssetDatabase.SaveAssets();
			Debug.Log("[Occ] DONE");
			EditorApplication.Exit(0);
		}
	}
}
