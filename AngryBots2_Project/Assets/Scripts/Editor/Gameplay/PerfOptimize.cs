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
	/// 5) 超大贴图 maxSize 下压到 1024（lightmap / skybox / UI 除外）
	/// 6) 静态小道具与半透明物关掉投影（静态投影体数量收敛）
	/// 遮挡剔除烘焙见 <see cref="BakeOcclusion"/>（单独入口，耗时长）。
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.PerfOptimize.Run -logFile Logs/perfoptimize.log
	/// </summary>
	public static class PerfOptimize
	{
		const string UrpAssetPath = "Assets/RenderPipelines/LightweightAsset_HighQuality.asset";

		const int MaxTextureSize = 1024;

		/// <summary>静态小道具判定：世界包围盒最大边小于该值即不投影。</summary>
		const float SmallShadowCasterSize = 0.6f;

		static readonly string[] ShadowBudgetScenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
		};

		static readonly string[] AllScenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
		};

		[MenuItem("ABGameplay/11. Perf Optimize")]
		public static void Run()
		{
			DisableMeshReadWrite();
			CompressTextures();
			ReduceShadowAtlas();
			TrimAdditionalLightShadows();
			CapTextureSizes();
			TrimSmallShadowCasters();

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

		// ---------------------------------------------------------------- 5
		static void CapTextureSizes()
		{
			int changed = 0;
			int skipped = 0;
			foreach (var guid in AssetDatabase.FindAssets("t:Texture2D"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;

				var importer = AssetImporter.GetAtPath(path) as TextureImporter;
				if (importer == null || importer.maxTextureSize <= MaxTextureSize) continue;

				if (IsSizeCapExcluded(path))
				{
					skipped++;
					continue;
				}

				importer.maxTextureSize = MaxTextureSize;
				importer.SaveAndReimport();
				changed++;
			}
			Debug.Log("[PerfOptimize] TEXTURE_SIZE cap=" + MaxTextureSize + " changed=" + changed + " skipped=" + skipped);
		}

		static bool IsSizeCapExcluded(string path)
		{
			return path.IndexOf("Lightmap", System.StringComparison.OrdinalIgnoreCase) >= 0
				|| path.IndexOf("Skybox", System.StringComparison.OrdinalIgnoreCase) >= 0
				|| path.IndexOf("/UI/", System.StringComparison.OrdinalIgnoreCase) >= 0
				|| path.IndexOf("/GUI/", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		// ---------------------------------------------------------------- 6
		static void TrimSmallShadowCasters()
		{
			foreach (var path in AllScenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid())
				{
					Debug.LogWarning("[PerfOptimize] 打不开 " + path);
					continue;
				}

				int small = 0;
				int transparent = 0;
				foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>(true))
				{
					if (renderer == null || !renderer.enabled) continue;
					if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;

					// 只动静态环境：动态角色/敌人的影子必须保留
					if (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) == 0) continue;

					var material = renderer.sharedMaterial;
					if (material != null && material.renderQueue >= 3000)
					{
						renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
						transparent++;
						continue;
					}

					Vector3 size = renderer.bounds.size;
					float maxSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
					if (maxSide < SmallShadowCasterSize)
					{
						renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
						small++;
					}
				}

				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
				Debug.Log("[PerfOptimize] SHADOW_CASTERS " + System.IO.Path.GetFileName(path)
					+ " small=" + small + " transparent=" + transparent);
			}
		}

		// ---------------------------------------------------------------- 遮挡剔除
		/// <summary>
		/// 给静态物件补 Occluder/Occludee 标记并烘焙 Umbra 遮挡数据（同步烘焙，耗时较长）。
		/// 用法：-executeMethod ABGameplay.PerfOptimize.BakeOcclusion
		/// </summary>
		[MenuItem("ABGameplay/12. Bake Occlusion")]
		public static void BakeOcclusion()
		{
			foreach (var path in AllScenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid())
				{
					Debug.LogWarning("[PerfOptimize] 打不开 " + path);
					continue;
				}

				int flagged = 0;
				foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>(true))
				{
					if (renderer == null) continue;
					var target = renderer.gameObject;
					var flags = GameObjectUtility.GetStaticEditorFlags(target);
					if (flags == 0) continue;

					var want = flags | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
					if (want == flags) continue;

					GameObjectUtility.SetStaticEditorFlags(target, want);
					flagged++;
				}

				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);

				float started = Time.realtimeSinceStartup;
				bool ok;
				try
				{
					ok = StaticOcclusionCulling.Compute();
				}
				catch (System.Exception exception)
				{
					Debug.LogError("[PerfOptimize] OCCLUSION 烘焙异常 " + path + " : " + exception.Message);
					continue;
				}

				EditorSceneManager.SaveScene(scene);
				Debug.Log("[PerfOptimize] OCCLUSION " + System.IO.Path.GetFileName(path)
					+ " ok=" + ok + " flagged=" + flagged
					+ " umbraKB=" + (StaticOcclusionCulling.umbraDataSize / 1024)
					+ " seconds=" + (Time.realtimeSinceStartup - started).ToString("0.0"));
			}

			AssetDatabase.SaveAssets();
			Debug.Log("[PerfOptimize] OCCLUSION_DONE");
			EditorApplication.Exit(0);
		}
	}
}
