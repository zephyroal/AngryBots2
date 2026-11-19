using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 引用完整性审计 / 修复：找出并清掉指向已不存在资产的悬空 GUID，修补历史遗留的空网格引用。
	/// 用法：
	///   -executeMethod ABGameplay.RefIntegrity.Run   仅审计
	///   -executeMethod ABGameplay.RefIntegrity.Fix   修复后审计
	/// </summary>
	public static class RefIntegrity
	{
		const string PlayerPrefab = "Assets/Prefabs/Player/Player.prefab";
		const string MuzzlePrefab = "Assets/Prefabs/Particles/Particles_Gun_MuzzleFlash_Prefab.prefab";

		static readonly string[] Scenes =
		{
			"Assets/AngryBots.unity",
			"Assets/U6Port/Scenes/AngryBots.unity",
			"Assets/U6Port/Scenes/3-4_Pain.unity",
			"Assets/Scenes/Scenes/_CompletedLevel.unity",
		};

		static readonly Regex GuidPattern = new Regex("guid: ([0-9a-f]{32})", RegexOptions.Compiled);

		[MenuItem("ABGameplay/13. Ref Integrity Audit")]
		public static void Run()
		{
			int dangling = AuditDanglingGuids();
			AuditScenes();
			Debug.Log("[RefIntegrity] DONE dangling=" + dangling);
			EditorApplication.Exit(dangling == 0 ? 0 : 1);
		}

		[MenuItem("ABGameplay/14. Ref Integrity Fix")]
		public static void Fix()
		{
			FixBatteryPackGlow();
			FixMuzzleParticleMesh();
			FixLightingDataReference();

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			int dangling = AuditDanglingGuids();
			AuditScenes();
			Debug.Log("[RefIntegrity] FIX_DONE dangling=" + dangling);
			EditorApplication.Exit(0);
		}

		// ------------------------------------------------------------ 审计
		static int AuditDanglingGuids()
		{
			var missing = new Dictionary<string, List<string>>();
			string[] extensions = { ".unity", ".prefab", ".mat", ".asset", ".controller", ".anim" };

			foreach (var fullPath in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
			{
				string ext = Path.GetExtension(fullPath).ToLowerInvariant();
				if (System.Array.IndexOf(extensions, ext) < 0) continue;

				string text;
				try { text = File.ReadAllText(fullPath); }
				catch { continue; }

				foreach (Match match in GuidPattern.Matches(text))
				{
					string guid = match.Groups[1].Value;
					if (guid.StartsWith("0000000000000000")) continue;
					if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) continue;

					List<string> files;
					if (!missing.TryGetValue(guid, out files)) missing[guid] = files = new List<string>();
					string assetPath = fullPath.Replace('\\', '/');
					if (files.Count < 3 && !files.Contains(assetPath)) files.Add(assetPath);
				}
			}

			foreach (var pair in missing)
			{
				Debug.LogWarning("[RefIntegrity] DANGLING " + pair.Key + " <- " + string.Join(" | ", pair.Value.ToArray()));
			}
			Debug.Log("[RefIntegrity] SUMMARY danglingGuids=" + missing.Count);
			return missing.Count;
		}

		static void AuditScenes()
		{
			foreach (var scenePath in Scenes)
			{
				var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
				if (!scene.IsValid()) continue;

				int nullMaterials = 0;
				int nullMeshes = 0;
				int missingScripts = 0;

				foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
				{
					if (renderer != null && renderer.sharedMaterial == null) nullMaterials++;
				}

				foreach (var filter in Object.FindObjectsOfType<MeshFilter>(true))
				{
					if (filter != null && filter.sharedMesh == null) nullMeshes++;
				}

				foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
				{
					if (behaviour == null) missingScripts++;
				}

				Debug.Log("[RefIntegrity] SCENE " + Path.GetFileName(scenePath)
					+ " nullMaterial=" + nullMaterials
					+ " nullMesh=" + nullMeshes
					+ " missingScript=" + missingScripts);
			}
		}

		// ------------------------------------------------------------ 修复
		/// <summary>玩家/关卡的 BatteryPackGlow 光晕平面历史上引用了一个不存在的网格，改回内置 Quad。</summary>
		static void FixBatteryPackGlow()
		{
			var quad = AssetDatabase.GetBuiltinExtraResource<Mesh>("Default-Models/Quad.fbx");
			if (quad == null)
			{
				Debug.LogWarning("[RefIntegrity] 取不到内置 Quad 网格");
				return;
			}

			int fixedCount = FixQuadInPrefab(PlayerPrefab, quad);

			var scene = EditorSceneManager.OpenScene("Assets/U6Port/Scenes/3-4_Pain.unity", OpenSceneMode.Single);
			if (scene.IsValid())
			{
				fixedCount += FixQuadInScene(quad);
				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
			}

			Debug.Log("[RefIntegrity] GLOW_FIXED " + fixedCount);
		}

		static int FixQuadInPrefab(string prefabPath, Mesh quad)
		{
			var root = PrefabUtility.LoadPrefabContents(prefabPath);
			if (root == null) return 0;

			try
			{
				int fixedCount = 0;
				foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
				{
					if (filter.sharedMesh != null) continue;
					if (filter.gameObject.name.IndexOf("Glow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
					filter.sharedMesh = quad;
					fixedCount++;
				}
				if (fixedCount > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
				return fixedCount;
			}
			finally { PrefabUtility.UnloadPrefabContents(root); }
		}

		static int FixQuadInScene(Mesh quad)
		{
			int fixedCount = 0;
			foreach (var filter in Object.FindObjectsOfType<MeshFilter>(true))
			{
				if (filter == null || filter.sharedMesh != null) continue;
				if (filter.gameObject.name.IndexOf("Glow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
				filter.sharedMesh = quad;
				fixedCount++;
			}
			return fixedCount;
		}

		/// <summary>枪口粒子渲染器是 Billboard 模式，但残留了指向已删除网格的 m_Mesh，清掉。</summary>
		static void FixMuzzleParticleMesh()
		{
			var root = PrefabUtility.LoadPrefabContents(MuzzlePrefab);
			if (root == null)
			{
				Debug.LogWarning("[RefIntegrity] 打不开 " + MuzzlePrefab);
				return;
			}

			try
			{
				int cleared = 0;
				foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
				{
					if (renderer.renderMode == ParticleSystemRenderMode.Mesh) continue;
					if (renderer.mesh == null) continue;
					renderer.mesh = null;
					cleared++;
				}
				if (cleared > 0) PrefabUtility.SaveAsPrefabAsset(root, MuzzlePrefab);
				Debug.Log("[RefIntegrity] PARTICLE_MESH_CLEARED " + cleared);
			}
			finally { PrefabUtility.UnloadPrefabContents(root); }
		}

		/// <summary>AngryBots 主场景引用的 LightingData 资产不存在（历史遗留），置空避免悬空引用。</summary>
		static void FixLightingDataReference()
		{
			var scene = EditorSceneManager.OpenScene("Assets/AngryBots.unity", OpenSceneMode.Single);
			if (!scene.IsValid()) return;

			Lightmapping.lightingDataAsset = null;
			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
			Debug.Log("[RefIntegrity] LIGHTING_DATA_CLEARED Assets/AngryBots.unity");
		}
	}
}
