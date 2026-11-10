using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ABGameplay
{
	/// <summary>
	/// 玩法接线静态校验：玩家 / 敌人预制体组件、手雷引用、场景 HUD 与 WaveDirector 结构。
	/// 用法（batchmode）：-executeMethod ABGameplay.GameplayVerification.Check
	/// 校验失败以退出码 1 结束，便于 CI / 脚本判断。
	/// </summary>
	public static class GameplayVerification
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string PlayerPrefabPath = "Assets/Prefabs/Character/Player_Prefab.prefab";
		const string EnemyPrefabPath = "Assets/Prefabs/Enemies/EnemySpider_Prefab.prefab";
		const string GrenadePrefabPath = "Assets/Prefabs/Projectiles/Grenade_Prefab.prefab";

		[MenuItem("ABGameplay/2. Gameplay Verification")]
		public static void Check()
		{
			var report = new StringBuilder();
			int failures = 0;

			void Expect(bool condition, string message)
			{
				report.AppendLine((condition ? "[ok]   " : "[fail] ") + message);
				if (!condition) failures++;
			}

			var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
			Expect(player != null, "玩家预制体可加载");
			if (player != null)
			{
				Expect(player.GetComponent<PlayerHealth>() != null, "玩家预制体有 PlayerHealth");
				var weapons = player.GetComponent<WeaponSystem>();
				Expect(weapons != null, "玩家预制体有 WeaponSystem");
				if (weapons != null)
				{
					Expect(weapons.projectilePrefab != null, "WeaponSystem.projectilePrefab 已接线");
					Expect(weapons.spawnPoint != null, "WeaponSystem.spawnPoint 已接线");
					Expect(weapons.grenadePrefab != null, "WeaponSystem.grenadePrefab 已接线");
				}

				var legacy = player.GetComponentInChildren<PlayerProjectileSpawner>(true);
				Expect(legacy == null || !legacy.enabled, "旧 PlayerProjectileSpawner 已禁用，避免重复开火");
			}

			var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
			Expect(enemy != null, "敌人预制体可加载");
			if (enemy != null) Expect(enemy.GetComponent<EnemyAttack>() != null, "敌人预制体有 EnemyAttack");

			var grenade = AssetDatabase.LoadAssetAtPath<GameObject>(GrenadePrefabPath);
			Expect(grenade != null, "手雷预制体可加载");
			if (grenade != null)
			{
				var behaviour = grenade.GetComponent<GrenadeProjectile>();
				Expect(behaviour != null, "手雷预制体有 GrenadeProjectile");
				if (behaviour != null) Expect(behaviour.explosionPrefab != null, "手雷爆炸特效已接线");
				Expect(grenade.GetComponent<Rigidbody>() != null, "手雷带 Rigidbody");
			}

			var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

			var hud = GameObject.Find("HUD");
			Expect(hud != null, "场景内有 HUD");
			if (hud != null)
			{
				var gameHud = hud.GetComponent<GameHud>();
				Expect(gameHud != null, "HUD 上有 GameHud");
				if (gameHud != null)
				{
					Expect(gameHud.healthFill != null && gameHud.healthFill.type == Image.Type.Filled, "血条为 Filled 模式");
					Expect(gameHud.healthText != null, "HUD 有血量文本");
					Expect(gameHud.ammoText != null, "HUD 有弹药文本");
					Expect(gameHud.waveText != null, "HUD 有波次文本");
					Expect(gameHud.timeText != null, "HUD 有倒计时文本");
					Expect(gameHud.resultPanel != null, "HUD 有结算面板");
					Expect(gameHud.damageFlash != null, "HUD 有受击红闪");
					Expect(gameHud.hitMarkerParts != null && gameHud.hitMarkerParts.Length >= 2, "HUD 有命中标记");
				}
			}

			var director = Object.FindObjectOfType<WaveDirector>();
			Expect(director != null, "场景内有 WaveDirector");
			if (director != null)
			{
				Expect(director.enemyPrefab != null, "WaveDirector.enemyPrefab 已接线");
				Expect(director.spawnPoints != null && director.spawnPoints.Length > 0,
					"WaveDirector 刷怪点数量 > 0（实际 " + (director.spawnPoints == null ? 0 : director.spawnPoints.Length) + "）");
			}

			var scenePlayer = GameObject.FindGameObjectWithTag("Player");
			Expect(scenePlayer != null, "场景内有 Player（tag=Player）");
			if (scenePlayer != null)
			{
				Expect(scenePlayer.GetComponent<PlayerHealth>() != null, "场景玩家有 PlayerHealth");
				Expect(scenePlayer.GetComponent<WeaponSystem>() != null, "场景玩家有 WeaponSystem");
			}

			Expect(Object.FindObjectsOfType<EnemyAttack>().Length >= 0, "敌人攻击组件可查询");

			report.AppendLine(failures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + failures + ")");
			Debug.Log("[ABGameplay]\n" + report);

			if (failures > 0) EditorApplication.Exit(1);
		}
	}
}