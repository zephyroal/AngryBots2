using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ABGameplay
{
	/// <summary>
	/// 扩展玩法接线：
	/// 1) 玩家预制体挂 PlayerHealth / WeaponSystem，并接管 PlayerProjectileSpawner
	/// 2) 敌人预制体挂 EnemyAttack
	/// 3) 生成手雷预制体
	/// 4) 场景里挂 WaveDirector（波次生存）与 HUD（血条 / 弹药 / 波次 / 倒计时 / 结算）
	/// 用法（batchmode）：-executeMethod ABGameplay.GameplayExtensionSetup.Run
	/// </summary>
	public static class GameplayExtensionSetup
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string PlayerPrefabPath = "Assets/Prefabs/Character/Player_Prefab.prefab";
		const string EnemyPrefabPath = "Assets/Prefabs/Enemies/EnemySpider_Prefab.prefab";
		const string GrenadePrefabPath = "Assets/Prefabs/Projectiles/Grenade_Prefab.prefab";
		const string ExplosionPrefabPath = "Assets/Prefabs/Particles/Particles_EnemySpider_Explode_Prefab.prefab";

		[MenuItem("ABGameplay/1. Gameplay Extension Setup")]
		public static void Run()
		{
			EnsureGrenadePrefab();
			var grenade = AssetDatabase.LoadAssetAtPath<GameObject>(GrenadePrefabPath);
			SetupPlayerPrefab(grenade);
			SetupEnemyPrefab();
			SetupScene(grenade);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[ABGameplay] GameplayExtensionSetup done");
		}

		static void SetupPlayerPrefab(GameObject grenadePrefab)
		{
			var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
			try
			{
				var health = root.GetComponent<PlayerHealth>();
				if (health == null) health = root.AddComponent<PlayerHealth>();

				var weapons = root.GetComponent<WeaponSystem>();
				if (weapons == null) weapons = root.AddComponent<WeaponSystem>();

				var legacySpawner = root.GetComponentInChildren<PlayerProjectileSpawner>(true);
				if (legacySpawner != null)
				{
					weapons.projectilePrefab = legacySpawner.projectilePrefab;
					weapons.spawnPoint = legacySpawner.spawnPoint;
					// 枪口粒子是嵌套预制体内的组件，外部资产无法直接引用；只保留有效引用，其余运行时按名字解析。
					if (weapons.muzzleParticles == null) weapons.muzzleParticles = legacySpawner.spawnParticles;
					weapons.fireAudio = legacySpawner.spawnAudioSource;
					legacySpawner.enabled = false;
					Debug.Log("[ABGameplay] WeaponSystem wired from " + legacySpawner.name);
				}
				else
				{
					Debug.LogWarning("[ABGameplay] Player prefab 里没有 PlayerProjectileSpawner");
				}

				if (weapons.muzzleFlashRoot == null)
				{
					foreach (var t in root.GetComponentsInChildren<Transform>(true))
					{
						if (t.name.IndexOf("muzzleflash", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
						if (t.GetComponentInChildren<ParticleSystem>(true) == null) continue;
						weapons.muzzleFlashRoot = t;
						Debug.Log("[ABGameplay] muzzleFlashRoot = " + t.name);
						break;
					}
				}

				weapons.grenadePrefab = grenadePrefab;
				PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		static void SetupEnemyPrefab()
		{
			var root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
			try
			{
				var attack = root.GetComponent<EnemyAttack>();
				if (attack == null) attack = root.AddComponent<EnemyAttack>();
				attack.damage = 10;
				attack.attackRange = 2.2f;
				attack.attackInterval = 1.3f;
				attack.attackWindup = 0.25f;
				PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		static void EnsureGrenadePrefab()
		{
			if (AssetDatabase.LoadAssetAtPath<GameObject>(GrenadePrefabPath) != null) return;

			var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			temp.name = "Grenade_Prefab";
			temp.transform.localScale = Vector3.one * 0.3f;

			var collider = temp.GetComponent<SphereCollider>();
			collider.isTrigger = false;

			var body = temp.AddComponent<Rigidbody>();
			body.mass = 0.6f;
			body.drag = 0.15f;

			var grenade = temp.AddComponent<GrenadeProjectile>();
			grenade.damage = 150;
			grenade.radius = 4f;
			grenade.fuseTime = 1.6f;
			grenade.explosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);

			var renderer = temp.GetComponent<Renderer>();
			var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			material.name = "Grenade_Material";
			material.color = new Color(0.25f, 0.55f, 0.35f);
			material.SetFloat("_Metallic", 0.6f);
			material.SetFloat("_Smoothness", 0.5f);
			renderer.sharedMaterial = material;

			PrefabUtility.SaveAsPrefabAsset(temp, GrenadePrefabPath);
			Object.DestroyImmediate(temp);
			AssetDatabase.ImportAsset(GrenadePrefabPath, ImportAssetOptions.ForceUpdate);
			Debug.Log("[ABGameplay] grenade prefab created: " + GrenadePrefabPath);
		}

		static void SetupScene(GameObject grenadePrefab)
		{
			var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

			var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
			var existing = Object.FindObjectsOfType<EnemyHealth>()
				.Where(e => e != null)
				.ToList();

			// 波次生成点：用关卡里现有蜘蛛的站位，保证在 NavMesh 上且玩家可达。
			var directorObject = GameObject.Find("WaveDirector");
			if (directorObject == null) directorObject = new GameObject("WaveDirector");
			directorObject.transform.position = existing.Count > 0 ? existing[0].transform.position : Vector3.zero;

			var director = directorObject.GetComponent<WaveDirector>();
			if (director == null) director = directorObject.AddComponent<WaveDirector>();

			var pointRoot = directorObject.transform.Find("SpawnPoints");
			if (pointRoot == null)
			{
				var root = new GameObject("SpawnPoints");
				root.transform.SetParent(directorObject.transform, false);
				pointRoot = root.transform;
			}

			for (int i = 0; i < existing.Count; i++)
			{
				string name = "SpawnPoint_" + i;
				var point = pointRoot.Find(name);
				if (point == null)
				{
					var go = new GameObject(name);
					go.transform.SetParent(pointRoot, false);
					point = go.transform;
				}
				point.position = existing[i].transform.position;
			}

			director.enemyPrefab = enemyPrefab;
			director.spawnPoints = pointRoot.Cast<Transform>().ToArray();
			director.firstWaveSize = 3;
			director.extraEnemiesPerWave = 1;
			director.wavesToWin = 5;
			director.waveTimeLimit = 90f;
			director.waveDelay = 3f;
			director.grenadeRefillPerWave = 1;

			var player = GameObject.FindGameObjectWithTag("Player");
			if (player != null)
			{
				var weapons = player.GetComponent<WeaponSystem>();
				if (weapons != null) weapons.grenadePrefab = grenadePrefab;
			}

			BuildHud();

			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
			Debug.Log("[ABGameplay] scene wired: director + hud (" + director.spawnPoints.Length + " spawn points)");
		}

		static GameHud BuildHud()
		{
			var existing = GameObject.Find("HUD");
			if (existing != null) Object.DestroyImmediate(existing);

			var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			var canvas = canvasObject.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 20;

			var scaler = canvasObject.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

			var barBg = CreateImage(canvasObject.transform, "HealthBarBg", new Color(0f, 0f, 0f, 0.55f),
				new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 36f), new Vector2(360f, 26f));
			var fill = CreateImage(barBg.transform, "HealthFill", new Color(0.2f, 0.85f, 0.35f, 1f),
				new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
			var fillRect = fill.rectTransform;
			fillRect.offsetMin = new Vector2(2f, 2f);
			fillRect.offsetMax = new Vector2(-2f, -2f);
			fill.type = Image.Type.Filled;
			fill.fillMethod = Image.FillMethod.Horizontal;
			fill.fillOrigin = (int)Image.OriginHorizontal.Left;
			fill.fillAmount = 1f;

			var healthText = CreateText(canvasObject.transform, "HealthText", font, "100 / 100", 18,
				new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 68f), new Vector2(360f, 24f), TextAnchor.MiddleLeft);

			var ammoText = CreateText(canvasObject.transform, "AmmoText", font, "", 18,
				new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(720f, 70f), TextAnchor.LowerRight);

			var waveText = CreateText(canvasObject.transform, "WaveText", font, "WAVE 1 / 5", 22,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(900f, 34f), TextAnchor.UpperCenter);
			var timeText = CreateText(canvasObject.transform, "TimeText", font, "TIME  90", 22,
				new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -28f), new Vector2(260f, 34f), TextAnchor.UpperRight);
			var hintText = CreateText(canvasObject.transform, "HintText", font, "", 30,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 40f), TextAnchor.UpperCenter);
			hintText.color = new Color(1f, 0.85f, 0.4f);

			CreateImage(canvasObject.transform, "Crosshair", new Color(1f, 1f, 1f, 0.75f),
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));

			// 命中 / 击杀标记（中心两条交叉短线）
			var hitMarkerA = CreateImage(canvasObject.transform, "HitMarkerA", new Color(1f, 1f, 1f, 0f),
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 3f));
			hitMarkerA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
			var hitMarkerB = CreateImage(canvasObject.transform, "HitMarkerB", new Color(1f, 1f, 1f, 0f),
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 3f));
			hitMarkerB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

			var waveBanner = CreateText(canvasObject.transform, "WaveBanner", font, "", 56,
				new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(900f, 80f), TextAnchor.MiddleCenter);
			waveBanner.color = new Color(1f, 0.9f, 0.55f, 0f);

			var flash = CreateImage(canvasObject.transform, "DamageFlash", new Color(1f, 0.1f, 0.05f, 0f),
				new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
			flash.raycastTarget = false;
			flash.rectTransform.offsetMin = Vector2.zero;
			flash.rectTransform.offsetMax = Vector2.zero;

			var resultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
			resultPanel.transform.SetParent(canvasObject.transform, false);
			var panelRect = resultPanel.GetComponent<RectTransform>();
			panelRect.anchorMin = Vector2.zero;
			panelRect.anchorMax = Vector2.one;
			panelRect.offsetMin = Vector2.zero;
			panelRect.offsetMax = Vector2.zero;
			var panelImage = resultPanel.GetComponent<Image>();
			panelImage.color = new Color(0f, 0f, 0f, 0.75f);
			var resultText = CreateText(resultPanel.transform, "ResultText", font, "", 44,
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 240f), TextAnchor.MiddleCenter);
			resultText.raycastTarget = false;
			resultPanel.SetActive(false);

			var gameHud = canvasObject.AddComponent<GameHud>();
			gameHud.healthFill = fill;
			gameHud.healthText = healthText;
			gameHud.ammoText = ammoText;
			gameHud.waveText = waveText;
			gameHud.timeText = timeText;
			gameHud.resultPanel = resultPanel;
			gameHud.resultText = resultText;
			gameHud.damageFlash = flash;
			gameHud.hintText = hintText;
			gameHud.hitMarkerParts = new[] { hitMarkerA, hitMarkerB };
			gameHud.waveBanner = waveBanner;

			return gameHud;
		}

		static Image CreateImage(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			var rect = go.GetComponent<RectTransform>();
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			var image = go.GetComponent<Image>();
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		static Text CreateText(Transform parent, string name, Font font, string content, int fontSize,
			Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, TextAnchor alignment)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Text));
			go.transform.SetParent(parent, false);
			var rect = go.GetComponent<RectTransform>();
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			var text = go.GetComponent<Text>();
			text.font = font;
			text.fontSize = fontSize;
			text.text = content;
			text.alignment = alignment;
			text.color = Color.white;
			text.raycastTarget = false;
			return text;
		}
	}
}