using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ABGameplay
{
	/// <summary>
	/// 把 VFX 粒子材质从 URP/Lit 迁到项目自带的非光照粒子 shader，并收敛枪口粒子预制体的渲染开销。
	/// <para>
	/// URP/Lit 用在粒子上有三重代价：每粒子走完整 PBR 光照 + 接收阴影 + 不读顶点色（生命周期颜色失效）。
	/// 迁到 <c>AngryBots/Particle/*</c> 后是纯 unlit 加法/alpha 混合，观感和性能都更好。
	/// </para>
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.VfxMaterialMigration.Run
	/// </summary>
	public static class VfxMaterialMigration
	{
		const string AdditiveShader = "AngryBots/Particle/Additive";
		const string AlphaBlendShader = "AngryBots/Particle/AlphaBlend";
		const string MuzzlePrefabPath = "Assets/Prefabs/Particles/Particles_Gun_MuzzleFlash_Prefab.prefab";
		const int MaxMuzzleParticles = 64;

		/// <summary>材质路径 → 是否使用加法混合。名字带 Alpha 的烟雾/碎片走 alpha 混合，其余发光类走加法。</summary>
		static readonly KeyValuePair<string, bool>[] Targets =
		{
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Flame_02_Alpha.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Glow_04.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Glow_05.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Glow_08.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Glow_12.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Intense_Center.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Lightning_01.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Lightning_02.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Particle_04.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/particle04.mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Smoke_01_Add(soft).mat", true),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Smoke_01_Alpha.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Smoke_03.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Smoke_Order02_Alpha.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Stone.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Dark_Clouds.mat", false),
			new KeyValuePair<string, bool>("Assets/VFX (Shared)/Materials/Refraction_LWRP.mat", true),
		};

		/// <summary>这些预制体的粒子渲染器还挂着内置 Default-Particle（Legacy 粒子 shader，URP 下不渲染），统一重挂项目材质。</summary>
		static readonly string[] DefaultParticlePrefabs =
		{
			"Assets/Prefabs/Particles/Particles_Bullet_Impact_Enemy_Prefab.prefab",
			"Assets/Prefabs/Particles/Particles_Bullet_Impact_Wall_Prefab.prefab",
		};

		const string ImpactMaterialPath = "Assets/VFX (Shared)/Materials/Particle_01.mat";

		/// <summary>没有任何主贴图的材质（如折射材质）改用这张径向渐变，避免变成一整块纯白加法面片。</summary>
		const string FallbackTexturePath = "Assets/VFX (Shared)/Textures/Glow_12.psd";

		/// <summary>枪口 5 层火焰里重复度最高的两层，关掉可省 2 个 draw call 与对应 overdraw。</summary>
		static readonly string[] DisabledMuzzleLayers = { "Flame_03", "Flame_04" };

		static readonly string[] MuzzleLightPrefabs =
		{
			"Assets/Prefabs/Player/Player.prefab",
			"Assets/Prefabs/Player/PlayerPrefab.prefab",
		};

		[MenuItem("ABGameplay/7. Migrate VFX Materials")]
		public static void Run()
		{
			int migrated = 0;
			int failed = 0;

			foreach (var target in Targets)
			{
				var material = AssetDatabase.LoadAssetAtPath<Material>(target.Key);
				if (material == null)
				{
					Debug.LogError("[VfxMigrate] 找不到材质 " + target.Key);
					failed++;
					continue;
				}

				string shaderName = target.Value ? AdditiveShader : AlphaBlendShader;
				var shader = Shader.Find(shaderName);
				if (shader == null)
				{
					Debug.LogError("[VfxMigrate] 找不到 shader " + shaderName);
					failed++;
					continue;
				}

				var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
				if (texture == null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex");
				if (texture == null)
				{
					texture = AssetDatabase.LoadAssetAtPath<Texture>(FallbackTexturePath);
					Debug.Log("[VfxMigrate] " + material.name + " 没有主贴图，改用 " + FallbackTexturePath);
				}

				Color baseColor = Color.white;
				if (material.HasProperty("_BaseColor")) baseColor = material.GetColor("_BaseColor");
				else if (material.HasProperty("_Color")) baseColor = material.GetColor("_Color");

				// 加法混合只看 RGB（颜色由贴图与顶点色决定），alpha 混合需要一个可用的不透明度。
				Color tint = target.Value
					? new Color(1f, 1f, 1f, 1f)
					: new Color(1f, 1f, 1f, Mathf.Clamp(baseColor.a <= 0.01f ? 0.6f : baseColor.a, 0.2f, 1f));

				var oldKeywords = material.shaderKeywords;
				material.shader = shader;
				for (int i = 0; i < oldKeywords.Length; i++) material.DisableKeyword(oldKeywords[i]);

				material.SetTexture("_MainTex", texture);
				material.SetColor("_TintColor", tint);
				material.renderQueue = 3000;
				EditorUtility.SetDirty(material);

				migrated++;
				Debug.Log("[VfxMigrate] " + material.name + " -> " + shaderName
					+ " tex=" + (texture != null ? texture.name : "null")
					+ " tint=" + tint);
			}

			AssetDatabase.SaveAssets();
			TuneMuzzlePrefab();
			TuneMuzzleLights();
			int remapped = FixDefaultParticleRenderers();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log("[VfxMigrate] DONE migrated=" + migrated + " failed=" + failed + " remappedRenderers=" + remapped);
			EditorApplication.Exit(failed == 0 ? 0 : 1);
		}

		/// <summary>把仍挂着内置 Default-Particle / 非 AngryBots shader 的粒子渲染器重挂到项目材质（内置材质无法迁移，只能换）。</summary>
		static int FixDefaultParticleRenderers()
		{
			var fallback = AssetDatabase.LoadAssetAtPath<Material>(ImpactMaterialPath);
			if (fallback == null)
			{
				Debug.LogError("[VfxMigrate] 找不到 " + ImpactMaterialPath);
				return 0;
			}

			int remapped = 0;
			foreach (var path in DefaultParticlePrefabs)
			{
				var root = PrefabUtility.LoadPrefabContents(path);
				if (root == null)
				{
					Debug.LogError("[VfxMigrate] 找不到 " + path);
					continue;
				}

				try
				{
					bool dirty = false;
					foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
					{
						var material = renderer.sharedMaterial;
						string shaderName = material != null && material.shader != null ? material.shader.name : "null";
						if (shaderName.StartsWith("AngryBots/")) continue;

						renderer.sharedMaterial = fallback;
						dirty = true;
						remapped++;
						Debug.Log("[VfxMigrate] " + path + " / " + renderer.gameObject.name + " : " + shaderName + " -> " + fallback.name);
					}

					if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
				}
				finally { PrefabUtility.UnloadPrefabContents(root); }
			}

			return remapped;
		}

		/// <summary>枪口粒子：限制单系统粒子上限、关阴影/光照探针/运动矢量、改成 Automatic 剔除以免离屏追帧。</summary>
		static void TuneMuzzlePrefab()
		{
			var root = PrefabUtility.LoadPrefabContents(MuzzlePrefabPath);
			if (root == null)
			{
				Debug.LogError("[VfxMigrate] 找不到 " + MuzzlePrefabPath);
				return;
			}

			try
			{
				foreach (var layerName in DisabledMuzzleLayers)
				{
					var child = root.transform.Find(layerName);
					if (child == null)
					{
						Debug.LogWarning("[VfxMigrate] 枪口预制体里没有 " + layerName);
						continue;
					}
					if (child.gameObject.activeSelf)
					{
						child.gameObject.SetActive(false);
						Debug.Log("[VfxMigrate] 关闭枪口重叠火焰层 " + layerName);
					}
				}

				int systems = 0;
				int activeRenderers = 0;
				foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
				{
					var main = ps.main;
					if (main.maxParticles > MaxMuzzleParticles) main.maxParticles = MaxMuzzleParticles;
					main.cullingMode = ParticleSystemCullingMode.Automatic;

					var renderer = ps.GetComponent<ParticleSystemRenderer>();
					if (renderer != null)
					{
						renderer.shadowCastingMode = ShadowCastingMode.Off;
						renderer.receiveShadows = false;
						renderer.lightProbeUsage = LightProbeUsage.Off;
						renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
						renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
						if (ps.gameObject.activeInHierarchy) activeRenderers++;
					}
					systems++;
				}

				PrefabUtility.SaveAsPrefabAsset(root, MuzzlePrefabPath);
				Debug.Log("[VfxMigrate] muzzle prefab tuned: systems=" + systems
					+ " activeRenderers=" + activeRenderers + " maxParticles=" + MaxMuzzleParticles);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		/// <summary>枪口点光：收缩作用半径（减少受影响渲染对象）并降强度，避免开火时整片环境被照亮。</summary>
		static void TuneMuzzleLights()
		{
			foreach (var path in MuzzleLightPrefabs)
			{
				var root = PrefabUtility.LoadPrefabContents(path);
				if (root == null)
				{
					Debug.LogWarning("[VfxMigrate] 找不到 " + path);
					continue;
				}

				try
				{
					bool dirty = false;
					foreach (var t in root.GetComponentsInChildren<Transform>(true))
					{
						if (!string.Equals(t.name, "muzzleFlash", StringComparison.OrdinalIgnoreCase)) continue;

						var light = t.GetComponent<Light>();
						if (light == null) continue;

						light.range = 5f;
						light.intensity = 1.5f;
						light.shadows = LightShadows.None;
						dirty = true;
						Debug.Log("[VfxMigrate] " + path + " 枪口点光 range=" + light.range + " intensity=" + light.intensity);
					}

					if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
				}
				finally { PrefabUtility.UnloadPrefabContents(root); }
			}
		}
	}
}
