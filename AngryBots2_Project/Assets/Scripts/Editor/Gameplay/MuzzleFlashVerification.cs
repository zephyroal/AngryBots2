using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ABGameplay
{
	/// <summary>
	/// 枪口火焰接线自检（批处理入口）。
	/// 用法：Tuanjie.exe -batchmode -projectPath &lt;工程&gt; -executeMethod ABGameplay.MuzzleFlashVerification.Run -logFile Logs/muzzleflash.log
	/// 校验点：粒子引用能否解析、粒子材质是否走 AngryBots 粒子 shader、AB1 网格火焰的渲染器开关、layer 名称。
	/// </summary>
	public static class MuzzleFlashVerification
	{
		const string PlayerGameplayPrefab = "Assets/Prefabs/Character/Player_Prefab.prefab";
		const string PlayerAb1Prefab = "Assets/Prefabs/Player/PlayerPrefab.prefab";
		const string PlayerAb1LegacyPrefab = "Assets/Prefabs/Player/Player.prefab";

		const string MuzzlePrefabPath = "Assets/Prefabs/Particles/Particles_Gun_MuzzleFlash_Prefab.prefab";

		const string Additive = "AngryBots/Particle/Additive";
		const string AlphaBlend = "AngryBots/Particle/AlphaBlend";

		static readonly KeyValuePair<string, string>[] ParticleMaterials =
		{
			Pair("Flame_01", Additive),
			Pair("Particle_01", Additive),
			Pair("Particle_03", Additive),
			Pair("Smoke_Order_01", AlphaBlend),
			Pair("MuzzleFlash_Core", Additive),
			Pair("Flame_02_Alpha", AlphaBlend),
			Pair("Glow_04", Additive),
			Pair("Glow_05", Additive),
			Pair("Glow_08", Additive),
			Pair("Glow_12", Additive),
			Pair("Intense_Center", Additive),
			Pair("Lightning_01", Additive),
			Pair("Lightning_02", Additive),
			Pair("Particle_04", Additive),
			Pair("particle04", Additive),
			Pair("Smoke_01_Add(soft)", Additive),
			Pair("Smoke_01_Alpha", AlphaBlend),
			Pair("Smoke_03", AlphaBlend),
			Pair("Smoke_Order02_Alpha", AlphaBlend),
			Pair("Stone", AlphaBlend),
			Pair("Dark_Clouds", AlphaBlend),
			Pair("Refraction_LWRP", Additive),
		};

		static KeyValuePair<string, string> Pair(string materialName, string shaderName)
		{
			return new KeyValuePair<string, string>("Assets/VFX (Shared)/Materials/" + materialName + ".mat", shaderName);
		}

		static int fails;

		[MenuItem("ABGameplay/6. Muzzle Flash Verification")]
		public static void Run()
		{
			fails = 0;

			CheckGameplayPlayer();
			CheckAb1Player(PlayerAb1Prefab, "muzzleFlash");
			CheckAb1Player(PlayerAb1LegacyPrefab, "muzzleFlash");
			CheckMaterials();
			CheckLayer();
			CheckMuzzlePrefabPerf();
			CheckImpactPrefabs();
			CheckScenes();

			Debug.Log("[MuzzleFlash] DONE fails=" + fails);
			EditorApplication.Exit(fails == 0 ? 0 : 1);
		}

		/// <summary>波次玩法玩家：WeaponSystem 必须能解析到枪口粒子，且粒子材质走项目粒子 shader。</summary>
		static void CheckGameplayPlayer()
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGameplayPrefab);
			if (prefab == null)
			{
				Fail("找不到 " + PlayerGameplayPrefab);
				return;
			}

			var weapons = prefab.GetComponent<WeaponSystem>();
			if (weapons == null)
			{
				Fail("Player_Prefab 上没有 WeaponSystem");
				return;
			}

			int count = weapons.MuzzleFlashSystemCount;
			Ok(count > 0, "枪口粒子系统解析数量 = " + count);

			var root = weapons.ResolvedMuzzleFlashRoot;
			Ok(root != null, "枪口粒子根 = " + (root != null ? root.name : "null"));
			if (root == null) return;

			int litCount = 0;
			foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
			{
				var renderer = ps.GetComponent<ParticleSystemRenderer>();
				var material = renderer != null ? renderer.sharedMaterial : null;
				if (material == null)
				{
					Fail(ps.name + " 没有材质");
					continue;
				}
				if (material.shader == null || !material.shader.name.StartsWith("AngryBots/Particle/"))
				{
					litCount++;
					Fail(ps.name + " 材质 " + material.name + " 的 shader = " + (material.shader != null ? material.shader.name : "null"));
				}
			}
			if (litCount == 0) Ok(true, "全部枪口粒子材质走 AngryBots 粒子 shader");
		}

		/// <summary>AB1 网格火焰：网格材质、阴影/探针开关、AutoFire 引用。</summary>
		static void CheckAb1Player(string prefabPath, string muzzleName)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (prefab == null)
			{
				Fail("找不到 " + prefabPath);
				return;
			}

			Transform muzzle = null;
			foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
			{
				if (t.name == muzzleName) { muzzle = t; break; }
			}
			if (muzzle == null)
			{
				Fail(prefabPath + " 里没有 " + muzzleName);
				return;
			}

			var renderer = muzzle.GetComponent<MeshRenderer>();
			Ok(renderer != null, prefabPath + " 枪口网格渲染器存在");
			if (renderer != null)
			{
				Ok(renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
					&& renderer.sharedMaterial.shader.name == "AngryBots/FX/Additive",
					"枪口网格材质 shader = " + (renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
						? renderer.sharedMaterial.shader.name : "null"));
				Ok(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off, "枪口网格不投阴影");
				Ok(!renderer.receiveShadows, "枪口网格不接收阴影");
				Ok(renderer.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off, "枪口网格不用光照探针");
			}

			Ok(muzzle.gameObject.layer == 12, "枪口 mesh 所在 layer = " + muzzle.gameObject.layer);
			Ok(LayerMask.LayerToName(muzzle.gameObject.layer) == "FX", "layer 名称 = " + LayerMask.LayerToName(muzzle.gameObject.layer));

			var muzzleLight = muzzle.GetComponent<Light>();
			Ok(muzzleLight != null, prefabPath + " 枪口点光存在");
			if (muzzleLight != null)
			{
				Ok(muzzleLight.range <= 6f, "枪口点光 range = " + muzzleLight.range);
				Ok(muzzleLight.intensity <= 2f, "枪口点光 intensity = " + muzzleLight.intensity);
			}

			var autoFire = prefab.GetComponentInChildren<AutoFire>(true);
			Ok(autoFire != null, prefabPath + " 上的 AutoFire 存在");
			if (autoFire != null)
			{
				Ok(autoFire.muzzleFlashFront == muzzle.gameObject, "AutoFire.muzzleFlashFront 指向枪口 mesh");
				Ok(autoFire.muzzleFlashDuration > 0f, "AutoFire 逐发闪光时长 = " + autoFire.muzzleFlashDuration);
			}
		}

		static void CheckMaterials()
		{
			int additive = 0;
			int alphaBlend = 0;

			foreach (var entry in ParticleMaterials)
			{
				var material = AssetDatabase.LoadAssetAtPath<Material>(entry.Key);
				if (material == null)
				{
					Fail("找不到材质 " + entry.Key);
					continue;
				}

				var shaderName = material.shader != null ? material.shader.name : "null";
				Ok(shaderName == entry.Value, System.IO.Path.GetFileName(entry.Key) + " shader = " + shaderName + "（期望 " + entry.Value + "）");

				if (shaderName == Additive) additive++;
				if (shaderName == AlphaBlend) alphaBlend++;

				var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
				Ok(texture != null, System.IO.Path.GetFileName(entry.Key) + " 贴图 = " + (texture != null ? texture.name : "null"));
			}

			Debug.Log("[MuzzleFlash] INFO 粒子材质 additive=" + additive + " alphaBlend=" + alphaBlend);
		}

		/// <summary>枪口粒子预制体的渲染开销：粒子上限、剔除模式、阴影/探针/运动矢量、draw call 数量。</summary>
		static void CheckMuzzlePrefabPerf()
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzlePrefabPath);
			if (prefab == null)
			{
				Fail("找不到 " + MuzzlePrefabPath);
				return;
			}

			var renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
			Ok(renderers.Length <= 10, "枪口粒子渲染器总数 = " + renderers.Length);

			int activeRenderers = 0;
			foreach (var renderer in renderers)
			{
				if (renderer.gameObject.activeInHierarchy) activeRenderers++;
			}
			Ok(activeRenderers <= 7, "枪口粒子实际 draw call（激活层）= " + activeRenderers);

			int systems = 0;
			foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
			{
				systems++;
				var main = ps.main;
				Ok(main.maxParticles <= 64, ps.name + " maxParticles = " + main.maxParticles);
				Ok(main.cullingMode == ParticleSystemCullingMode.Automatic, ps.name + " cullingMode = " + main.cullingMode);

				var renderer = ps.GetComponent<ParticleSystemRenderer>();
				Ok(renderer != null && renderer.shadowCastingMode == ShadowCastingMode.Off, ps.name + " 不投阴影");
				Ok(renderer != null && renderer.lightProbeUsage == LightProbeUsage.Off, ps.name + " 不用光照探针");
				Ok(renderer == null || !renderer.receiveShadows, ps.name + " 不接收阴影");
			}

			Debug.Log("[MuzzleFlash] INFO 枪口粒子系统数 = " + systems);
		}

		static void CheckLayer()
		{
			Ok(LayerMask.LayerToName(12) == "FX", "TagManager layer 12 = " + LayerMask.LayerToName(12));
		}

		/// <summary>子弹命中特效原先挂着内置 Default-Particle（Legacy 粒子 shader，URP 下不渲染）。</summary>
		static void CheckImpactPrefabs()
		{
			var paths = new[]
			{
				"Assets/Prefabs/Particles/Particles_Bullet_Impact_Enemy_Prefab.prefab",
				"Assets/Prefabs/Particles/Particles_Bullet_Impact_Wall_Prefab.prefab",
			};

			foreach (var path in paths)
			{
				var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					Fail("找不到 " + path);
					continue;
				}

				foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
				{
					var material = renderer.sharedMaterial;
					string shaderName = material != null && material.shader != null ? material.shader.name : "null";
					Ok(shaderName.StartsWith("AngryBots/"),
						System.IO.Path.GetFileName(path) + " / " + renderer.gameObject.name + " shader = " + shaderName);
				}
			}
		}

		/// <summary>场景实例可能带 override，必须单独验一次。</summary>
		static void CheckScenes()
		{
			CheckGameplayScene("Assets/Scenes/Scenes/_CompletedLevel.unity");
			CheckAb1Scene("Assets/AngryBots.unity");
			CheckAb1Scene("Assets/U6Port/Scenes/AngryBots.unity");
		}

		static void CheckGameplayScene(string scenePath)
		{
			var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			if (!scene.IsValid())
			{
				Fail("打不开场景 " + scenePath);
				return;
			}

			var weapons = Object.FindObjectOfType<WeaponSystem>(true);
			Ok(weapons != null, scenePath + " 里找到 WeaponSystem");
			if (weapons == null) return;

			int count = weapons.MuzzleFlashSystemCount;
			Ok(count > 0, scenePath + " 枪口粒子系统数量 = " + count + "（根 = "
				+ (weapons.ResolvedMuzzleFlashRoot != null ? weapons.ResolvedMuzzleFlashRoot.name : "null") + "）");
		}

		static void CheckAb1Scene(string scenePath)
		{
			var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			if (!scene.IsValid())
			{
				Fail("打不开场景 " + scenePath);
				return;
			}

			var autoFire = Object.FindObjectOfType<AutoFire>(true);
			Ok(autoFire != null, scenePath + " 里找到 AutoFire");
			if (autoFire == null) return;

			Ok(autoFire.muzzleFlashFront != null, scenePath + " AutoFire.muzzleFlashFront 已接");
			if (autoFire.muzzleFlashFront == null) return;

			var renderer = autoFire.muzzleFlashFront.GetComponent<MeshRenderer>();
			Ok(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
				&& renderer.sharedMaterial.shader.name == "AngryBots/FX/Additive",
				scenePath + " 枪口网格材质 shader = " + (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
					? renderer.sharedMaterial.shader.name : "null"));
		}

		static void Ok(bool condition, string message)
		{
			if (condition) Debug.Log("[MuzzleFlash] PASS " + message);
			else Fail(message);
		}

		static void Fail(string message)
		{
			fails++;
			Debug.LogError("[MuzzleFlash] FAIL " + message);
		}
	}
}
