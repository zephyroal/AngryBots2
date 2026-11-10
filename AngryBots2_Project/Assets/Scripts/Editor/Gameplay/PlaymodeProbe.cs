using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// Play Mode 冒烟测试：验证受伤 → HUD、开火耗弹、手雷投掷、清波后刷新下一波。
	/// 用法（batchmode，注意不要加 -quit）：
	///   -executeMethod ABGameplay.PlaymodeProbe.Run
	/// 结束时会以 EditorApplication.Exit(0/1) 返回结果。
	/// </summary>
	public static class PlaymodeProbe
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const float TimeoutSeconds = 40f;

		static readonly StringBuilder Report = new StringBuilder();
		static int failures;
		static float startedAt;
		static int stage;
		static PlayerHealth playerHealth;
		static WeaponSystem weapons;
		static WaveDirector director;
		static GameHud hud;
		static int rifleAmmoBefore;
		static int grenadesBefore;
		static int waveBefore;

		static void Expect(bool condition, string message)
		{
			Report.AppendLine((condition ? "[ok]   " : "[fail] ") + message);
			if (!condition) failures++;
		}

		public static void Run()
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			EditorApplication.update += Tick;
			EditorApplication.EnterPlaymode();
		}

		static void Tick()
		{
			if (!EditorApplication.isPlaying) return;

			if (startedAt <= 0f)
			{
				startedAt = Time.realtimeSinceStartup;
				stage = 0;
				return;
			}

			float elapsed = Time.realtimeSinceStartup - startedAt;

			try
			{
				if (stage == 0 && elapsed > 1.5f)
				{
					stage = 1;
					Setup();
				}
				else if (stage == 1 && elapsed > 2.0f)
				{
					stage = 2;
					RunCombatChecks();
				}
				else if (stage == 2 && elapsed > 2.5f)
				{
					stage = 3;
					ClearWave();
				}
				else if (stage == 3 && elapsed > 12f)
				{
					stage = 4;
					CheckNextWave();
					Finish();
				}
				else if (elapsed > TimeoutSeconds)
				{
					Expect(false, "冒烟测试超时（" + TimeoutSeconds + "s）");
					Finish();
				}
			}
			catch (System.Exception e)
			{
				Expect(false, "冒烟测试异常：" + e);
				Finish();
			}
		}

		static void Setup()
		{
			var player = GameObject.FindGameObjectWithTag("Player");
			Expect(player != null, "运行时找到 Player");
			if (player != null)
			{
				playerHealth = player.GetComponent<PlayerHealth>();
				weapons = player.GetComponent<WeaponSystem>();
			}
			Expect(playerHealth != null, "运行时 PlayerHealth 存在");
			Expect(weapons != null, "运行时 WeaponSystem 存在");

			director = Object.FindObjectOfType<WaveDirector>();
			Expect(director != null, "运行时 WaveDirector 存在");

			hud = Object.FindObjectOfType<GameHud>();
			Expect(hud != null, "运行时 GameHud 存在");

			if (director != null)
			{
				waveBefore = director.Wave;
				Expect(director.Wave == 1, "初始为第 1 波（实际 " + director.Wave + "）");
			}
			if (hud != null && playerHealth != null)
			{
				Expect(hud.healthText != null && hud.healthText.text.Contains(playerHealth.CurrentHealth.ToString()),
					"HUD 血量文本已同步（" + (hud.healthText != null ? hud.healthText.text : "null") + "）");
			}
		}

		static void RunCombatChecks()
		{
			if (playerHealth != null)
			{
				int before = playerHealth.CurrentHealth;
				playerHealth.TakeDamage(20);
				Expect(playerHealth.CurrentHealth == before - 20,
					"受伤扣血 " + before + " -> " + playerHealth.CurrentHealth);
				if (hud != null && hud.healthText != null)
					Expect(hud.healthText.text.StartsWith(playerHealth.CurrentHealth.ToString()),
						"HUD 血量跟随刷新（" + hud.healthText.text + "）");
			}

			if (weapons != null)
			{
				rifleAmmoBefore = weapons.RifleAmmo;
				weapons.TryFire(true);
				Expect(weapons.RifleAmmo == rifleAmmoBefore - 1,
					"开火消耗弹药 " + rifleAmmoBefore + " -> " + weapons.RifleAmmo);

				grenadesBefore = weapons.Grenades;
				weapons.ThrowGrenade();
				Expect(weapons.Grenades == grenadesBefore - 1,
					"手雷投掷消耗 " + grenadesBefore + " -> " + weapons.Grenades);
			}

			var enemies = Object.FindObjectsOfType<EnemyHealth>();
			Expect(enemies.Length > 0, "场景内存在敌人（" + enemies.Length + "）");
			if (enemies.Length > 0)
			{
				var attack = enemies[0].GetComponent<EnemyAttack>();
				Expect(attack != null, "敌人挂有 EnemyAttack");
			}
		}

		static void ClearWave()
		{
			if (director == null) return;
			foreach (var enemy in Object.FindObjectsOfType<EnemyHealth>())
			{
				if (enemy != null && !enemy.IsDead) enemy.RemoveHealth(99999);
			}
			Debug.Log("[PlaymodeProbe] wave cleared, waiting for next wave");
		}

		static void CheckNextWave()
		{
			if (director == null) return;
			Expect(director.Wave > waveBefore, "清波后进入下一波（" + waveBefore + " -> " + director.Wave + "）");
			var enemies = Object.FindObjectsOfType<EnemyHealth>();
			Expect(enemies.Length > 0, "下一波敌人已生成（" + enemies.Length + "）");
		}

		static void Finish()
		{
			EditorApplication.update -= Tick;
			Report.AppendLine(failures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + failures + ")");
			Debug.Log("[ABGameplay][PlaymodeProbe]\n" + Report);

			EditorApplication.Exit(failures == 0 ? 0 : 1);
		}
	}
}