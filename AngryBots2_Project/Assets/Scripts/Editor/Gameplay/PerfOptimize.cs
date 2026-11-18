using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 整体性能优化（可重复执行，幂等）：
	/// 1) 关闭模型 Read/Write（每份网格内存翻倍，运行时代码已不再读取网格数据）
	/// 2) 把仍未压缩的普通贴图改为压缩（lightmap 保持原样）
	/// 3) 附加光阴影图集 4096 → 2048（167 盏投影灯时显存/带宽明显）
	/// 4) 大场景里小半径点光/聚光关掉实时阴影（保留平行光与大半径灯的阴影）
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.PerfOptimize.Run -logFile Logs/perfoptimize.log
	/// </summary>
	public static class PerfOptimize
	{
		const string UrpAssetPath = "Assets/RenderPipelines/LightweightAsset_HighQuality.asset";

		static readonly string[] ShadowBudgetScenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
		};

		[MenuItem("ABGameplay/11. Perf Optimize")]
		public static void Run()
		{
			DisableMeshReadWrite();
			CompressTextures();
			ReduceShadowAtlas();
			TrimAdditionalLightShadows();

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[PerfOptimize] DONE");
			EditorApplication.Exit(0);
		}

		// ---------------------------------------------------------------- 1
		static void DisableMeshReadWrite()
		{
			int changed = 0;
			var guids = AssetDatabase.FindAssets("t:Model");
			foreach (var guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;

				var importer = AssetImporter.GetAtPath(path) as ModelImporter;
				if (importer == null || !importer.isReadable) continue;

				importer.isReadable = false;
				importer.SaveAndReimport();
				changed++;
			}
			Debug.Log("[PerfOptimize] MESH readableOff=" + changed + " / totalModels=" + guids.Length);
		}

		// ---------------------------------------------------------------- 2
		static void CompressTextures()
		{
			int changed = 0;
			int skippedLightmap = 0;
			var guids = AssetDatabase.FindAssets("t:Texture2D");
			foreach (var guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;

				var importer = AssetImporter.GetAtPath(path) as TextureImporter;
				if (importer == null) continue;
				if (importer.textureCompression != TextureImporterCompression.Uncompressed) continue;

				// lightmap / 方向图必须是未压缩 HDR
				if (path.IndexOf("Lightmap", System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					skippedLightmap++;
					continue;
				}

				importer.textureCompression = TextureImporterCompression.Compressed;
				importer.SaveAndReimport();
				changed++;
			}
			Debug.Log("[PerfOptimize] TEXTURE compressed=" + changed + " lightmapKept=" + skippedLightmap);
		}

		// ---------------------------------------------------------------- 3
		static void ReduceShadowAtlas()
		{
			var asset = AssetDatabase.LoadAssetAtPath<Object>(UrpAssetPath);
			if (asset == null)
			{
				Debug.LogWarning("[PerfOptimize] 找不到 " + UrpAssetPath);
				return;
			}

			var serialized = new SerializedObject(asset);
			var atlas = serialized.FindProperty("m_ShadowAtlasResolution");
			if (atlas == null)
			{
				Debug.LogWarning("[PerfOptimize] URP 资产里没有 m_ShadowAtlasResolution");
				return;
			}

			if (atlas.intValue > 2048)
			{
				int before = atlas.intValue;
				atlas.intValue = 2048;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(asset);
				Debug.Log("[PerfOptimize] SHADOW_ATLAS " + before + " -> 2048");
			}
			else
			{
				Debug.Log("[PerfOptimize] SHADOW_ATLAS 已是 " + atlas.intValue);
			}
		}

		// ---------------------------------------------------------------- 4
		static void TrimAdditionalLightShadows()
		{
			foreach (var path in ShadowBudgetScenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid())
				{
					Debug.LogWarning("[PerfOptimize] 打不开 " + path);
					continue;
				}

				int trimmed = 0;
				int kept = 0;
				foreach (var light in Object.FindObjectsOfType<Light>(true))
				{
					if (light == null) continue;

					if (light.type == LightType.Directional)
					{
						kept++;
						continue;
					}

					// 小半径点光/聚光主要提供局部染色，实时阴影几乎看不出，成本却按灯累加
					if (light.shadows != LightShadows.None && light.range <= 10f)
					{
						light.shadows = LightShadows.None;
						trimmed++;
					}
					else if (light.shadows != LightShadows.None)
					{
						kept++;
					}
				}

				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
				Debug.Log("[PerfOptimize] LIGHT_SHADOWS " + System.IO.Path.GetFileName(path)
					+ " trimmed=" + trimmed + " kept=" + kept);
			}
		}
	}
}
