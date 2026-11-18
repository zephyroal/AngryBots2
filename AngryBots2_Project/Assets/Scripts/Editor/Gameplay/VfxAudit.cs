using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// VFX 渲染审计（批处理入口）：统计所有粒子渲染器用的 shader，并列出仍在用 URP/Lit 的透明 VFX 材质。
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.VfxAudit.Run -logFile Logs/vfxaudit.log
	/// 只读，不修改任何资产。
	/// </summary>
	public static class VfxAudit
	{
		static readonly string[] Scenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
		};

		[MenuItem("ABGameplay/8. VFX Audit")]
		public static void Run()
		{
			var byShader = new Dictionary<string, int>();
			int totalRenderers = 0;
			int litRenderers = 0;
			int sceneRenderers = 0;

			var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
			foreach (var guid in prefabGuids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;
				GameObject root = null;
				try { root = PrefabUtility.LoadPrefabContents(path); }
				catch { root = null; }
				if (root == null) continue;

				try
				{
					foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
					{
						totalRenderers++;
						string shaderName = ShaderNameOf(renderer);
						Count(byShader, shaderName);
						if (IsLit(shaderName))
						{
							litRenderers++;
							Debug.Log("[VfxAudit] OFFENDER prefab=" + path + " object=" + renderer.gameObject.name
								+ " material=" + MaterialNameOf(renderer) + " shader=" + shaderName);
						}
					}
				}
				finally { PrefabUtility.UnloadPrefabContents(root); }
			}

			foreach (var scenePath in Scenes)
			{
				var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
				if (!scene.IsValid()) continue;
				foreach (var renderer in Object.FindObjectsOfType<ParticleSystemRenderer>(true))
				{
					totalRenderers++;
					sceneRenderers++;
					string shaderName = ShaderNameOf(renderer);
					Count(byShader, shaderName);
					if (IsLit(shaderName))
					{
						litRenderers++;
						Debug.Log("[VfxAudit] OFFENDER scene=" + scenePath + " object=" + renderer.gameObject.name
							+ " material=" + MaterialNameOf(renderer) + " shader=" + shaderName);
					}
				}
			}

			Debug.Log("[VfxAudit] SUMMARY particleRenderers=" + totalRenderers + " (sceneInstances=" + sceneRenderers + ")"
				+ " litRenderers=" + litRenderers);
			foreach (var pair in byShader)
			{
				Debug.Log("[VfxAudit] SHADER " + pair.Key + " = " + pair.Value);
			}

			AuditTransparentMaterials();
			Debug.Log("[VfxAudit] DONE");
			EditorApplication.Exit(0);
		}

		/// <summary>仍在 URP/Lit 上的透明材质（半截迁移的残留），属于可以直接换非光照 shader 的候选。</summary>
		static void AuditTransparentMaterials()
		{
			int candidates = 0;
			foreach (var guid in AssetDatabase.FindAssets("t:Material"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var material = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (material == null || material.shader == null) continue;
				if (!IsLit(material.shader.name)) continue;

				bool transparent = material.renderQueue >= 3000 || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
				if (!transparent) continue;

				candidates++;
				Debug.Log("[VfxAudit] TRANSPARENT_LIT " + path + " queue=" + material.renderQueue
					+ " shader=" + material.shader.name);
			}
			Debug.Log("[VfxAudit] SUMMARY transparentLitMaterials=" + candidates);
		}

		static bool IsLit(string shaderName)
		{
			return !string.IsNullOrEmpty(shaderName) && shaderName.StartsWith("Universal Render Pipeline");
		}

		/// <summary>只扫预制体（不打开大场景），把没走 AngryBots 粒子/FX shader 的渲染器全部列出来。</summary>
		[MenuItem("ABGameplay/9. VFX Suspects (prefabs only)")]
		public static void RunPrefabsOnly()
		{
			int suspects = 0;
			foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/")) continue;

				GameObject root = null;
				try { root = PrefabUtility.LoadPrefabContents(path); }
				catch { root = null; }
				if (root == null) continue;

				try
				{
					foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
					{
						string shaderName = ShaderNameOf(renderer);
						if (shaderName.StartsWith("AngryBots/")) continue;
						suspects++;
						Debug.Log("[VfxAudit] SUSPECT prefab=" + path + " object=" + renderer.gameObject.name
							+ " material=" + MaterialNameOf(renderer) + " shader=" + shaderName);
					}
				}
				finally { PrefabUtility.UnloadPrefabContents(root); }
			}

			Debug.Log("[VfxAudit] SUMMARY suspects=" + suspects);
			Debug.Log("[VfxAudit] DONE");
			EditorApplication.Exit(0);
		}

		static string ShaderNameOf(ParticleSystemRenderer renderer)
		{
			var material = renderer.sharedMaterial;
			return material != null && material.shader != null ? material.shader.name : "null";
		}

		static string MaterialNameOf(ParticleSystemRenderer renderer)
		{
			return renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null";
		}

		static void Count(Dictionary<string, int> map, string key)
		{
			map.TryGetValue(key, out int value);
			map[key] = value + 1;
		}
	}
}
