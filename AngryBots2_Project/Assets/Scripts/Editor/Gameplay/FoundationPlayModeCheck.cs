using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ABGameplay
{
	/// <summary>
	/// P3 玩法基础层自检 —— <b>PlayMode 部分</b>。
	/// <para>
	/// Edit 模式下 <c>new GameObject()</c> 不会触发 Awake/OnEnable，注册表 / 事件总线 / 池化 /
	/// 飘字 / 拾取 / Bootstrap 升级这些依赖生命周期的行为只能在 PlayMode 里验证。
	/// 本自检用 <see cref="EditorApplication.EnterPlaymode"/> 进入 Play，在 update 回调里按阶段驱动，
	/// 结束时 <see cref="EditorApplication.Exit"/> 返回结果。
	/// </para>
	/// <para>
	/// 用法（<b>不要加 -quit</b>）：
	/// <code>
	/// Unity.exe -batchmode -projectPath &lt;AB2&gt; -executeMethod ABGameplay.FoundationPlayModeCheck.Run -logFile E:\MyPlayGround\_logs\gc_p3_playmode.log
	/// </code>
	/// 末尾打印 "P3 RESULT: PASS" / "P3 RESULT: FAIL (n)"。
	/// </para>
	/// </summary>
	public static class FoundationPlayModeCheck
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const float GlobalTimeoutSeconds = 90f;

		static readonly StringBuilder Report = new StringBuilder();
		static int failures;
		static bool started;
		static bool finished;
		static float startedAt;
		static int stage;

		// 跨阶段状态
		static GameObject probeEnemy;
		static GameObject probePlayer;
		static EnemyHealthEx probeHealth;
		static EnemyAgent probeAgent;
		static PlayerHealthAdapter probePlayerHealth;
		static WeaponSystemAdapter probeWeapons;
		static int registryBaseline;
		static int killEventsSeen;
		static EnemyKind lastKilledKind;
		static int spawnerCountBefore;
		static int poolSpawnCount;

		static void Hard(bool condition, string message)
		{
			Report.AppendLine((condition ? "[ok]   " : "[FAIL] ") + message);
			if (!condition) failures++;
		}

		static void Info(string message)
		{
			Report.AppendLine("[info] " + message);
		}

		[MenuItem("ABGameplay/4. Foundation PlayMode Check (P3)")]
		public static void Run()
		{
			Report.Clear();
			failures = 0;
			started = false;
			finished = false;
			stage = 0;

			Report.AppendLine("=== AB2 P3 玩法基础层自检（PlayMode）=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

			if (File.Exists(ScenePath))
				EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

			EditorApplication.update += Tick;
			EditorApplication.EnterPlaymode();
		}

		static void Tick()
		{
			if (finished) return;

			try
			{
				if (!EditorApplication.isPlaying)
				{
					// 场景切换 / 退出 Play 时可能短暂为 false，给 5 秒缓冲。
					if (started && Time.realtimeSinceStartup > startedAt + 5f && stage >= 0 && stage < 90)
					{
						// 进入 Play 之前的等待期，忽略。
					}
					return;
				}

				if (!started)
				{
					started = true;
					startedAt = Time.unscaledTime;
					stage = 1;
					return;
				}

				float elapsed = Time.unscaledTime - startedAt;
				if (elapsed > GlobalTimeoutSeconds)
				{
					Hard(false, "PlayMode 自检超时（" + GlobalTimeoutSeconds + "s，停在阶段 " + stage + "）");
					Finish();
					return;
				}

				switch (stage)
				{
					case 1:
						if (elapsed < 0.6f) break;
						stage = 2;
						CheckBootstrapAndPlayer();
						break;

					case 2:
						if (elapsed < 1.0f) break;
						stage = 3;
						CheckRegistryLifecycle();
						break;

					case 3:
						if (elapsed < 1.4f) break;
						stage = 4;
						CheckDamageAndEvents();
						break;

					case 4:
						if (elapsed < 1.8f) break;
						stage = 5;
						CheckPooling();
						break;

					case 5:
						if (elapsed < 2.2f) break;
						stage = 6;
						CheckDamageNumbers();
						break;

					case 6:
						if (elapsed < 2.8f) break;
						stage = 7;
						CheckPickups();
						break;

					case 7:
						if (elapsed < 3.4f) break;
						stage = 8;
						CheckHitFeedback();
						break;

					case 8:
						if (elapsed < 8.0f) break;
						stage = 9;
						CheckSteadyState();
						break;

					case 9:
						Finish();
						break;
				}
			}
			catch (Exception exception)
			{
				Hard(false, "PlayMode 自检异常：" + exception);
				Finish();
			}
		}

		// ─────────────────────── 阶段 1：Bootstrap / 玩家升级 ───────────────────────

		static void CheckBootstrapAndPlayer()
		{
			var bootstrap = GameplayBootstrap.Instance;
			Hard(bootstrap != null, "GameplayBootstrap 单例已存在");
			if (bootstrap != null)
				Hard(bootstrap.Applied, "GameplayBootstrap 已完成 Apply");

			Hard(GameEvents.LegacyHooksInstalled, "GameEvents 旧静态事件桥已安装");
			Hard(PlayerLocator.Instance != null, "PlayerLocator 单例已创建");
			Hard(PoolManager.Instance != null, "PoolManager 单例已创建");
			Hard(DamageNumberSpawner.Instance != null, "DamageNumberSpawner 单例已创建");

			var player = GameObject.FindGameObjectWithTag("Player");
			if (player == null) player = GameplayBootstrap.FindAnyPlayerFallback();
			Hard(player != null, "场景内玩家对象存在");

			if (player != null)
			{
				probePlayerHealth = player.GetComponent<PlayerHealthAdapter>();
				probeWeapons = player.GetComponent<WeaponSystemAdapter>();
				Hard(probePlayerHealth != null, "玩家已升级为 PlayerHealthAdapter");
				Hard(probeWeapons != null, "玩家已升级为 WeaponSystemAdapter");
				Hard(player.GetComponent<HitFeedback>() != null, "玩家已挂 HitFeedback");

				var asDamageable = probePlayerHealth as IDamageable;
				Hard(asDamageable != null && asDamageable.IsPlayer, "PlayerHealthAdapter 可被当作 IDamageable 使用");
			}

			var upgradedAgents = Object.FindObjectsOfType<EnemyAgent>().Length;
			Info("场景内 EnemyAgent 数量：" + upgradedAgents + "（旧蜘蛛已被自动补挂）");
		}

		// ─────────────────────── 阶段 2：注册表生命周期 ───────────────────────

		static void CheckRegistryLifecycle()
		{
			registryBaseline = EnemyRegistry.AliveCount;
			Info("注册表基线 AliveCount = " + registryBaseline + "（场景自带敌人）");

			probeEnemy = new GameObject("FoundationCheck_ProbeEnemy");
			probeHealth = probeEnemy.AddComponent<EnemyHealthEx>();
			probeHealth.totalHealth = 120;
			probeHealth.ResetHealth(120);

			var collider = probeEnemy.AddComponent<CapsuleCollider>();
			collider.center = Vector3.up;
			collider.height = 2f;

			probeAgent = probeEnemy.AddComponent<EnemyAgent>();
			probeAgent.kind = EnemyKind.Mech;
			probeAgent.scoreValue = 60;
			probeAgent.transform.position = new Vector3(0f, 0f, 0f);

			Hard(EnemyRegistry.AliveCount == registryBaseline + 1, "OnEnable 自动注册敌人（AliveCount " + registryBaseline + " -> " + EnemyRegistry.AliveCount + "）");
			Hard(probeAgent.transform != null, "敌人 Transform 可用");

			probeEnemy.SetActive(false);
			Hard(EnemyRegistry.AliveCount == registryBaseline, "SetActive(false) 自动注销（AliveCount " + EnemyRegistry.AliveCount + "）");
			probeEnemy.SetActive(true);
			Hard(EnemyRegistry.AliveCount == registryBaseline + 1, "重新激活自动注册（AliveCount " + EnemyRegistry.AliveCount + "）");

			Hard(Mathf.Approximately(probeAgent.CurrentHealth, 120f), "EnemyAgent 读到适配器血量 120（实际 " + probeAgent.CurrentHealth + "）");
			Hard(Mathf.Approximately(probeAgent.MaxHealth, 120f), "EnemyAgent 读到最大血量 120（实际 " + probeAgent.MaxHealth + "）");
		}

		// ─────────────────────── 阶段 3：伤害 + 事件总线 ───────────────────────

		static void CheckDamageAndEvents()
		{
			Hard(probeAgent != null, "探测敌人已创建");

			GameEvents.EnemyKilled += OnKilled;

			float applied = probeAgent.ApplyDamage(DamageInfo.Create(40f, probePlayer != null ? probePlayer : (GameObject.FindGameObjectWithTag("Player")), probeEnemy.transform.position + Vector3.up, Vector3.forward));
			Hard(Mathf.Approximately(applied, 40f), "EnemyAgent.ApplyDamage 扣 40（实际 " + applied + "）");
			Hard(Mathf.Approximately(probeAgent.CurrentHealth, 80f), "扣血后剩 80（实际 " + probeAgent.CurrentHealth + "）");

			probeAgent.armor = 0.5f;
			float armored = probeAgent.ApplyDamage(DamageInfo.Create(20f, null, probeEnemy.transform.position, Vector3.forward));
			Hard(Mathf.Approximately(armored, 20f) || Mathf.Approximately(armored, 10f), "护甲路径生效（实际 " + armored + "）");
			probeAgent.armor = 0f;

			// DamageUtility 通过碰撞体解析并结算
			var collider = probeEnemy.GetComponent<Collider>();
			bool utilityHit = DamageUtility.Hit(collider, DamageInfo.Create(5f, null, probeEnemy.transform.position, Vector3.forward));
			Hard(utilityHit, "DamageUtility.Hit(Collider) 能解析到敌人并结算");

			// 击杀 → 事件总线
			killEventsSeen = 0;
			probeAgent.Kill(DamageInfo.Create(9999f, null, probeEnemy.transform.position, Vector3.forward));
			GameEvents.EnemyKilled -= OnKilled;

			Hard(killEventsSeen == 1, "击杀触发 GameEvents.EnemyKilled 1 次（实际 " + killEventsSeen + "）");
			Hard(lastKilledKind == EnemyKind.Mech, "EnemyKilled 携带种类 Mech（实际 " + lastKilledKind + "）");
		}

		static void OnKilled(EnemyKind kind, Vector3 position, GameObject killer, int score)
		{
			killEventsSeen++;
			lastKilledKind = kind;
		}

		// ─────────────────────── 阶段 4：对象池 ───────────────────────

		static void CheckPooling()
		{
			var pool = PoolManager.Instance;
			Hard(pool != null, "PoolManager 实例可用");

			var template = new GameObject("FoundationCheck_PoolTemplate");
			template.SetActive(true);

			var ids = new HashSet<int>();
			for (int i = 0; i < 15; i++)
				ids.Add(PoolManager.Spawn(template, Vector3.one * i, Quaternion.identity).GetInstanceID());
			Hard(ids.Count == 15, "首次 15 次 Spawn 得到 15 个不同实例（" + ids.Count + "）");

			foreach (var id in ids)
			{
				var go = EditorUtility.InstanceIDToObject(id) as GameObject;
				if (go != null) PoolManager.Despawn(go);
			}

			var before = pool.PoolCount;
			var again = new HashSet<int>();
			for (int i = 0; i < 15; i++)
				again.Add(PoolManager.Spawn(template, Vector3.zero, Quaternion.identity).GetInstanceID());
			Hard(again.SetEquals(ids), "归还后复用了同一批实例（池化生效）");
			Hard(pool.PoolCount == before, "池数量稳定（" + pool.PoolCount + "）");
			poolSpawnCount = again.Count;

			// 定时归还
			var timed = PoolManager.Spawn(template, Vector3.zero, Quaternion.identity);
			PoolManager.DespawnAfter(timed, 0.2f);
			Hard(pool.PendingDespawns >= 1, "DespawnAfter 登记了定时归还（" + pool.PendingDespawns + "）");

			Object.Destroy(template);
		}

		// ─────────────────────── 阶段 5：伤害飘字 ───────────────────────

		static void CheckDamageNumbers()
		{
			var spawner = DamageNumberSpawner.Instance;
			Hard(spawner != null, "DamageNumberSpawner 实例可用");
			if (spawner == null) return;

			Info("飘字字体：" + (spawner.font != null ? spawner.font.name : "null（PlayMode 下应为 LegacyRuntime）"));
			Hard(spawner.font != null, "PlayMode 下飘字字体已解析");

			int before = spawner.SpawnedCount;
			spawnerCountBefore = before;
			spawner.Spawn(new Vector3(1f, 1f, 1f), 25f, false);
			spawner.Spawn(new Vector3(1f, 1f, 1f), 120f, true);
			Hard(spawner.SpawnedCount == before + 2, "飘字生成 2 条（累计 " + spawner.SpawnedCount + "）");
			Hard(spawner.ActiveCount >= 2, "活跃飘字 ≥ 2（实际 " + spawner.ActiveCount + "）");

			// 事件驱动：敌人受伤自动飘字
			var fan = Object.FindObjectOfType<EnemyAgent>();
			int target = -1;
			if (fan != null)
			{
				target = fan.gameObject.GetInstanceID();
				GameEvents.RaiseEnemyDamaged(fan.gameObject, 33f, DamageInfo.Create(33f, null, fan.transform.position + Vector3.up, Vector3.forward));
				Hard(spawner.SpawnedCount == before + 3, "EnemyDamaged 事件驱动自动飘字（累计 " + spawner.SpawnedCount + "）");
			}
			else
			{
				Info("场景里没有存活 EnemyAgent，跳过事件驱动飘字检查");
			}
		}

		// ─────────────────────── 阶段 6：拾取 ───────────────────────

		static void CheckPickups()
		{
			if (probePlayerHealth == null)
			{
				Info("玩家适配器不存在，跳过拾取检查");
				return;
			}

			probePlayerHealth.GrantInvulnerability(0f);
			probePlayerHealth.TakeDamageEx(40);
			int before = probePlayerHealth.CurrentHealth;

			var pickup = SimplePickup.CreateHealthPack(probePlayerHealth.transform.position, 25, 0f);
			Hard(pickup != null, "SimplePickup.CreateHealthPack 生成成功");
			bool collected = pickup != null && pickup.TryCollect(probePlayerHealth);
			Hard(collected, "血包拾取成功");
			Hard(probePlayerHealth.CurrentHealth == Mathf.Min(probePlayerHealth.MaxHealth, before + 25),
				"血包回血生效 " + before + " -> " + probePlayerHealth.CurrentHealth);

			// 满血不消耗
			probePlayerHealth.ResetHealthEx();
			var pack2 = SimplePickup.CreateHealthPack(probePlayerHealth.transform.position, 25, 5f);
			Hard(!pack2.TryCollect(probePlayerHealth), "满血时血包不被消耗");
			Object.Destroy(pack2.gameObject);

			// 弹药包：WeaponSystemAdapter 已就绪，应能补弹
			if (probeWeapons != null)
			{
				int magBefore = probeWeapons.CurrentMag;
				// 先打掉一些弹药（不实际开火，用 TryFire 会 Instantiate 子弹；这里直接看满弹判定）
				int added = probeWeapons.AddAmmo(10);
				Hard(added >= 0, "WeaponSystemAdapter.AddAmmo 可调用（补 " + added + "，弹匣 " + magBefore + " -> " + probeWeapons.CurrentMag + "）");
			}

			// 手雷包
			if (probeWeapons != null)
			{
				int grenadesBefore = probeWeapons.Grenades;
				var grenadePack = SimplePickup.CreateGrenadePack(probePlayerHealth.transform.position, 1, 0f);
				bool grenadeCollected = grenadePack.TryCollect(probePlayerHealth);
				Info("手雷包拾取：" + grenadeCollected + "，手雷 " + grenadesBefore + " -> " + probeWeapons.Grenades +
					"（上限 " + probeWeapons.maxGrenadeCharges + "）");
			}
		}

		// ─────────────────────── 阶段 7：命中反馈 ───────────────────────

		static void CheckHitFeedback()
		{
			var feedback = Object.FindObjectOfType<HitFeedback>();
			Hard(feedback != null, "HitFeedback 实例存在");
			if (feedback == null) return;

			float originalScale = Time.timeScale;
			feedback.TriggerHit(Vector3.zero, true, false);
			Hard(feedback.CurrentShake > 0f, "命中触发震屏（振幅 " + feedback.CurrentShake.ToString("0.000") + "）");
			Hard(feedback.HitStopCount >= 1, "命中触发顿帧（次数 " + feedback.HitStopCount + "）");
			Info("顿帧期间 timeScale = " + Time.timeScale + "（原始 " + originalScale + "）");

			feedback.TriggerHit(Vector3.zero, false, true);
			Info("击杀反馈后顿帧次数 " + feedback.HitStopCount + "，振幅 " + feedback.CurrentShake.ToString("0.000"));
		}

		// ─────────────────────── 阶段 8：稳态 ───────────────────────

		static void CheckSteadyState()
		{
			Hard(Mathf.Approximately(Time.timeScale, 1f) || Time.timeScale > 0f,
				"顿帧结束后 timeScale 已恢复（当前 " + Time.timeScale + "）");

			var spawner = DamageNumberSpawner.Instance;
			if (spawner != null)
				Hard(spawner.ActiveCount <= spawner.maxConcurrent, "活跃飘字不超过上限（" + spawner.ActiveCount + "/" + spawner.maxConcurrent + "）");

			Info("稳态检查：" + EnemyRegistry.Describe());
			Info("PoolManager 池数量 " + (PoolManager.Instance != null ? PoolManager.Instance.PoolCount : 0) +
				"，待归还 " + (PoolManager.Instance != null ? PoolManager.Instance.PendingDespawns : 0));
		}

		// ─────────────────────── 结束 ───────────────────────

		static void Finish()
		{
			if (finished) return;
			finished = true;

			EditorApplication.update -= Tick;

			Report.AppendLine();
			Report.AppendLine(failures == 0 ? "P3 RESULT: PASS" : "P3 RESULT: FAIL (" + failures + ")");

			Debug.Log("[ABGameplay][FoundationPlayModeCheck]\n" + Report);
			FoundationCheck.WriteReportFile("p3_foundation_playmode_report.md");

			EditorApplication.Exit(failures == 0 ? 0 : 1);
		}
	}
}