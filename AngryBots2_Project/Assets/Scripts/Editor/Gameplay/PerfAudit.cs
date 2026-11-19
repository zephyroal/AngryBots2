using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ABGameplay
{
	/// <summary>
	/// 整体性能审计（只读）：管线/画质设置、场景渲染复杂度、贴图/音频/网格预算。
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.PerfAudit.Run -logFile Logs/perfaudit.log
	/// </summary>
	public static class PerfAudit
	{
		static readonly string[] Scenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
		};

		[MenuItem("ABGameplay/10. Perf Audit")]
		public static void Run()
		{
			AuditPipeline();
			AuditScenes();
			AuditTextures();
			AuditAudio();
			AuditMeshes();
			Debug.Log("[PerfAudit] DONE");
			EditorApplication.Exit(0);
		}

		static void AuditPipeline()
		{
			var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
			if (urp == null)
			{
				Debug.LogWarning("[PerfAudit] 当前默认管线不是 URP");
			}
			else
			{
				Debug.Log("[PerfAudit] PIPELINE msaa=" + urp.msaaSampleCount
					+ " hdr=" + urp.supportsHDR
					+ " renderScale=" + urp.renderScale.ToString("0.##")
					+ " shadowDistance=" + urp.shadowDistance
					+ " cascades=" + urp.shadowCascadeCount
					+ " shadowmap=" + urp.mainLightShadowmapResolution
					+ " addLights=" + urp.additionalLightsRenderingMode
					+ " addLightPerObject=" + urp.maxAdditionalLightsCount
					+ " depthTex=" + urp.supportsCameraDepthTexture
					+ " opaqueTex=" + urp.supportsCameraOpaqueTexture
					+ " srpBatcher=" + urp.useSRPBatcher);
			}

			Debug.Log("[PerfAudit] QUALITY level=" + QualitySettings.GetQualityLevel()
				+ " vSync=" + QualitySettings.vSyncCount
				+ " antiAliasing=" + QualitySettings.antiAliasing
				+ " shadowDistance=" + QualitySettings.shadowDistance
				+ " pixelLightCount=" + QualitySettings.pixelLightCount
				+ " lodBias=" + QualitySettings.lodBias
				+ " realtimeReflectionProbes=" + QualitySettings.realtimeReflectionProbes);

			Debug.Log("[PerfAudit] TIMING fixedDeltaTime=" + Time.fixedDeltaTime.ToString("0.###")
				+ " maxDeltaTime=" + Time.maximumDeltaTime);
		}

		static void AuditScenes()
		{
			foreach (var path in Scenes)
			{
				var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				if (!scene.IsValid())
				{
					Debug.LogWarning("[PerfAudit] 打不开 " + path);
					continue;
				}

				var renderers = Object.FindObjectsOfType<Renderer>(true);
				var lights = Object.FindObjectsOfType<Light>(true);
				var particles = Object.FindObjectsOfType<ParticleSystem>(true);
				var lodGroups = Object.FindObjectsOfType<LODGroup>(true);
				var canvases = Object.FindObjectsOfType<Canvas>(true);

				int staticFlags = 0;
				int shadowCasters = 0;
				var materials = new HashSet<Material>();
				var shaders = new HashSet<Shader>();
				int triangulationEstimate = 0;
				int missingMaterials = 0;
				int missingMeshes = 0;
				int missingScripts = 0;

				foreach (var renderer in renderers)
				{
					if (renderer == null) continue;
					if (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) != 0) staticFlags++;
					if (renderer.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;

					var shared = renderer.sharedMaterial;
					if (shared != null)
					{
						materials.Add(shared);
						if (shared.shader != null) shaders.Add(shared.shader);
					}
					else
					{
						missingMaterials++;
					}

					var filter = renderer.GetComponent<MeshFilter>();
					if (filter != null && filter.sharedMesh != null)
					{
						triangulationEstimate += filter.sharedMesh.triangles.Length / 3;
					}
					else if (filter != null)
					{
						missingMeshes++;
					}
				}

				foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
				{
					if (behaviour == null) missingScripts++;
				}

				int realtimeShadowLights = 0;
				int realtimeLights = 0;
				foreach (var light in lights)
				{
					if (light == null) continue;
					if (light.lightmapBakeType == LightmapBakeType.Mixed || light.lightmapBakeType == LightmapBakeType.Realtime) realtimeLights++;
					if (light.shadows != LightShadows.None) realtimeShadowLights++;
				}

				Debug.Log("[PerfAudit] SCENE " + Path.GetFileName(path)
					+ " renderers=" + renderers.Length
					+ " staticFlagged=" + staticFlags
					+ " shadowCasters=" + shadowCasters
					+ " uniqueMaterials=" + materials.Count
					+ " uniqueShaders=" + shaders.Count
					+ " tris=" + triangulationEstimate
					+ " lights=" + lights.Length + "(realtime=" + realtimeLights + ",shadow=" + realtimeShadowLights + ")"
					+ " particles=" + particles.Length
					+ " lodGroups=" + lodGroups.Length
					+ " canvases=" + canvases.Length
					+ " missingMat=" + missingMaterials
					+ " missingMesh=" + missingMeshes
					+ " missingScript=" + missingScripts
					+ " umbraKB=" + (StaticOcclusionCulling.umbraDataSize / 1024));
			}
		}

		static void AuditTextures()
		{
			var guids = AssetDatabase.FindAssets("t:Texture2D");
			int count = 0;
			int readable = 0;
			int big = 0;
			long estimatedBytes = 0;
			var top = new List<KeyValuePair<long, string>>();

			foreach (var guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;

				var importer = AssetImporter.GetAtPath(path) as TextureImporter;
				if (importer == null) continue;
				count++;

				if (importer.isReadable) readable++;
				int max = Mathf.Max(1, importer.maxTextureSize);
				if (max >= 2048) big++;

				bool compressed = importer.textureCompression != TextureImporterCompression.Uncompressed;
				long bytes = (long)max * max * (compressed ? 1 : 4);
				estimatedBytes += bytes;
				if (compressed) estimatedBytes -= bytes / 2; // 压缩纹理按半量估算

				top.Add(new KeyValuePair<long, string>(bytes, path + " max=" + max + " comp=" + importer.textureCompression + " readable=" + importer.isReadable));
			}

			top.Sort((a, b) => b.Key.CompareTo(a.Key));
			Debug.Log("[PerfAudit] TEXTURES count=" + count
				+ " readable=" + readable
				+ " maxSize>=2048=" + big
				+ " estMB=" + (estimatedBytes / (1024f * 1024f)).ToString("0.0"));
			for (int i = 0; i < Mathf.Min(10, top.Count); i++)
			{
				Debug.Log("[PerfAudit] TEXTURE_TOP " + top[i].Value);
			}
		}

		static void AuditAudio()
		{
			int count = 0;
			long bytes = 0;
			int uncompressed = 0;
			foreach (var guid in AssetDatabase.FindAssets("t:AudioClip"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;
				count++;

				var importer = AssetImporter.GetAtPath(path) as AudioImporter;
				if (importer == null) continue;
				var settings = importer.defaultSampleSettings;
				if (settings.compressionFormat == AudioCompressionFormat.PCM) uncompressed++;

				var info = new FileInfo(path);
				if (info.Exists) bytes += info.Length;
			}
			Debug.Log("[PerfAudit] AUDIO count=" + count + " rawMB=" + (bytes / (1024f * 1024f)).ToString("0.0") + " pcm=" + uncompressed);
		}

		static void AuditMeshes()
		{
			int models = 0;
			int readable = 0;
			foreach (var guid in AssetDatabase.FindAssets("t:Model"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;
				models++;

				var importer = AssetImporter.GetAtPath(path) as ModelImporter;
				if (importer != null && importer.isReadable) readable++;
			}
			Debug.Log("[PerfAudit] MESHES models=" + models + " readable=" + readable);
		}
	}
}
