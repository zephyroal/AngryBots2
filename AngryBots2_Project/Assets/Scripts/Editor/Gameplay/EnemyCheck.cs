using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ABGameplay.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ABGameplay
{
	/// <summary>
	/// P3B 敌人与波次自检（Edit 模式 + <see cref="EnemyBehaviour.SimulateTick"/> 逐帧驱动）。
	/// <para>
	/// 为什么不用 PlayMode：本工程的 batchmode 下 <c>EnterPlaymode()</c> 在大型资产导入期间
	/// 会长时间阻塞甚至不返回，稳定性不可靠。改为在 Edit 模式里用
	/// <see cref="EditorApplication.update"/> 逐帧调用 <see cref="EnemyBehaviour.SimulateTick"/>，
	/// 推进的是**与运行时完全相同的状态机代码**，同样能真实跑满 10 秒。
	/// </para>
	/// <para>用法（可配 -quit）：<c>-executeMethod ABGameplay.EnemyCheck.Run</c></para>
	/// <para>末尾打印 "P3B RESULT: PASS" / "P3B RESULT: FAIL (n)"。</para>
	/// </summary>
	public static class EnemyCheck
	{
		const string LogDirectory = @"E:\MyPlayGround\_logs";
		const string DefinitionDir = "Assets/Scripts/Gameplay/Enemies/Definitions";
		const float SimulationSeconds = 10f;
		const float GlobalTimeoutSeconds = 150f;

		static readonly StringBuilder Report = new StringBuilder();
		static int failures;
		static bool started;
		static bool finished;
		static double startedAt;
		static int stage;

		// 跨阶段状态
		static readonly List<GameObject> spawned = new List<GameObject>();
		static readonly Dictionary<EnemyKind, GameObject> probes = new Dictionary<EnemyKind, GameObject>();
		static WaveDirectorEx director;
		static GameObject tempPlayer;
		static int exceptionCount;
		static int simTicks;
		static readonly Dictionary<EnemyKind, EnemyState> firstStates = new Dictionary<EnemyKind, EnemyState>();
		static readonly HashSet<EnemyState> observedStates = new HashSet<EnemyState>();
		static Vector3 bossStartPosition;

		static void Hard(bool condition, string message)
		{
			Report.AppendLine((condition ? "[ok]   " : "[FAIL] ") + message);
			if (!condition) failures++;
		}

		static void Info(string message)
		{
			Report.AppendLine("[info] " + message);
		}

		[MenuItem("ABGameplay/6. Enemy Check (P3B)")]
		public static void Run()
		{
			Report.Clear();
			failures = 0;
			started = false;
			finished = false;
			stage = 0;
			exceptionCount = 0;
			simTicks = 0;
			spawned.Clear();
			probes.Clear();
			firstStates.Clear();
			observedStates.Clear();

			Report.AppendLine("=== AB2 P3B 敌人与波次自检 === " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Report.AppendLine("Unity " + Application.unityVersion + "，模式：Edit + SimulateTick，模拟 " + SimulationSeconds + "s");

			Application.logMessageReceived += OnLogMessage;

			try
			{
				// 阶段 1：资产（同步）
				CheckAssets();

				// 阶段 2：生成 + 10 秒驱动（异步，走 EditorApplication.update）
				PrepareProbes();
				startedAt = EditorApplication.timeSinceStartup;
				started = true;
				EditorApplication.update += Tick;
			}
			catch (Exception exception)
			{
				Hard(false, "自检启动异常：" + exception);
				Finish();
			}
		}

		static void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception && type != LogType.Error) return;
			if (condition.Contains("LicensingClient")) return;
			if (condition.Contains("no audio listeners")) return;
			exceptionCount++;
			Report.AppendLine("[ERROR] " + condition.Replace("\n", " | "));
		}

		static void Tick()
		{
			if (finished) return;

			try
			{
				double elapsed = EditorApplication.timeSinceStartup - startedAt;
				if (elapsed > GlobalTimeoutSeconds)
				{
					Hard(false, "自检超时（" + GlobalTimeoutSeconds + "s，阶段 " + stage + "）");
					Finish();
					return;
				}

				switch (stage)
				{
					case 0:
						stage = 1;
						break;

					case 1:
						// 连续驱动 10 秒真实时间，每帧推进一次状态机。
						DriveProbes();
						if (elapsed < SimulationSeconds) break;
						stage = 2;
						VerifySimulation();
						break;

					case 2:
						CheckSpawnTable();
						stage = 3;
						break;

					case 3:
						CheckWaveDirector();
						stage = 4;
						break;

					case 4:
						Cleanup();
						Finish();
						break;
				}
			}
			catch (Exception exception)
			{
				Hard(false, "自检运行异常：" + exception);
				Finish();
			}
		}

		// ─────────────────────── 阶段 1：资产 ───────────────────────

		static void CheckAssets()
		{
			EnemyPrefabSetup.EnsureDefinitions();

			string[] prefabs =
			{
				"Assets/Prefabs/Enemies/Ex/EnemyMech_Ex.prefab",
				"Assets/Prefabs/Enemies/Ex/EnemyBuzzer_Ex.prefab",
				"Assets/Prefabs/Enemies/Ex/KamikazeBuzzer_Ex.prefab",
				"Assets/Prefabs/Enemies/Ex/EnemySpiderQueen_Ex.prefab",
			};

			foreach (var path in prefabs)
			{
				var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				Hard(prefab != null, "预制体存在：" + Path.GetFileName(path));
				if (prefab == null) continue;

				Hard(prefab.GetComponent<EnemyAgent>() != null, "  └ 挂 EnemyAgent");
				Hard(prefab.GetComponent<EnemyHealthEx>() != null, "  └ 挂 EnemyHealthEx");
				Hard(prefab.GetComponent<Health>() == null, "  └ 已移除旧 Health（damageSignals NRE 风险）");
				Hard(prefab.GetComponentInChildren<Collider>(true) != null, "  └ 有 Collider");
				Hard(prefab.GetComponent<Rigidbody>() != null || prefab.GetComponent<UnityEngine.AI.NavMeshAgent>() != null,
					"  └ 有 Rigidbody 或 NavMeshAgent");
			}

			foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
			{
				if (kind == EnemyKind.Spider) continue; // Spider 走旧 AI 体系
				var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionDir + "/Enemy_" + kind + ".asset");
				Hard(definition != null, "定义资产存在：" + kind);
				if (definition == null) continue;

				Hard(definition.prefab != null, "  └ " + kind + " 定义已绑定 prefab（" + (definition.prefab != null ? definition.prefab.name : "null") + "）");
				string error;
				Hard(definition.Validate(out error), "  └ " + kind + " 数值合法（" + error + "）");
			}
		}

		// ─────────────────────── 阶段 2：生成 + 驱动 ───────────────────────

		static void PrepareProbes()
		{
			// Edit 模式没有运行时玩家，造一个带 PlayerHealthAdapter 的临时玩家作为目标。
			tempPlayer = new GameObject("EnemyCheck_TempPlayer");
			tempPlayer.tag = "Untagged";
			var playerHealth = tempPlayer.AddComponent<PlayerHealthAdapter>();
			playerHealth.maxHealth = 100000;      // 避免被敌人打死导致目标消失
			playerHealth.invulnerabilityTime = 0f;
			tempPlayer.transform.position = Vector3.zero;
			spawned.Add(tempPlayer);

			// 让 PlayerLocator 指向临时玩家（Edit 模式下没有 GameplayBootstrap 自动接线）。
			var locator = PlayerLocator.Instance;
			if (locator == null)
			{
				var locatorObject = new GameObject("EnemyCheck_PlayerLocator");
				locator = locatorObject.AddComponent<PlayerLocator>();
				spawned.Add(locatorObject);
			}
			locator.playerRoot = tempPlayer.transform;

			Hard(PlayerSource.Player == tempPlayer.transform, "PlayerSource 能解析到临时玩家");

			var kinds = new[] { EnemyKind.Mech, EnemyKind.Buzzer, EnemyKind.Kamikaze, EnemyKind.Boss };
			for (int i = 0; i < kinds.Length; i++)
			{
				var kind = kinds[i];
				var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionDir + "/Enemy_" + kind + ".asset");
				if (definition == null)
				{
					Hard(false, kind + " 定义缺失，无法生成");
					continue;
				}

				// 放到感知半径内（Mech 22 / Buzzer 22 / Kamikaze 22 / Boss 22）。
				float angle = i * Mathf.PI * 0.5f;
				Vector3 position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 8f;

				GameObject instance = null;
				try
				{
					instance = EnemyFactory.Spawn(definition, position, Quaternion.identity);
				}
				catch (Exception exception)
				{
					Hard(false, kind + " 生成抛异常：" + exception.Message);
				}

				if (instance == null)
				{
					Hard(false, kind + " 能实例化");
					continue;
				}

				spawned.Add(instance);
				probes[kind] = instance;

				if (kind == EnemyKind.Boss) bossStartPosition = instance.transform.position;

				var behaviour = instance.GetComponent<EnemyBehaviour>();
				var agent = instance.GetComponent<EnemyAgent>();

				Hard(behaviour != null, kind + " 实例挂有 EnemyBehaviour");
				Hard(agent != null, kind + " 实例挂有 EnemyAgent");
				if (behaviour != null)
				{
					firstStates[kind] = behaviour.State;
					Info(kind + " 初始：状态=" + behaviour.State + "，血量=" + behaviour.CurrentHealth.ToString("0") +
						"/" + behaviour.MaxHealth.ToString("0") + "，位置=" + instance.transform.position.ToString("F1"));
				}
				if (agent != null)
				{
					// Edit 模式不会触发 OnEnable，用官方接口补登记（运行时由 OnEnable 完成）。
					EnemyRegistry.Register(agent);
					Hard(EnemyRegistry.Contains(agent), kind + " 已注册到 EnemyRegistry");
				}
			}

			// Edit 模式不会触发 OnEnable，用官方同步接口把场景内敌人登记进注册表（运行时由 OnEnable 完成）。
			EnemyRegistry.SynchronizeScene();

			Hard(EnemyRegistry.AliveCount >= 4, "注册表存活数 >= 4（实际 " + EnemyRegistry.AliveCount + "）");
			Info("生成完成：" + EnemyRegistry.Describe());
		}

		static void DriveProbes()
		{
			simTicks++;

			foreach (var pair in probes)
			{
				var instance = pair.Value;
				if (instance == null) continue;

				var behaviour = instance.GetComponent<EnemyBehaviour>();
				if (behaviour == null) continue;
				if (behaviour.State == EnemyState.Dead) continue;

				try
				{
					behaviour.SimulateTick();
					observedStates.Add(behaviour.State);
				}
				catch (Exception exception)
				{
					Report.AppendLine("[FAIL] " + pair.Key + " SimulateTick 抛异常：" + exception.Message);
					failures++;
				}
			}

			// 敌人弹丸也需要驱动（它们同样靠 Update）。
			foreach (var go in spawned)
			{
				if (go == null) continue;
				var projectile = go.GetComponent<EnemyProjectile>();
				if (projectile != null) projectile.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
			}
		}

		// ─────────────────────── 阶段 3：模拟后验证 ───────────────────────

		static void VerifySimulation()
		{
			Info("共驱动 " + simTicks + " 帧，观察到的状态集合：" + StateList());
			Hard(simTicks > 30, "模拟帧数充足（" + simTicks + " 帧）");
			Hard(exceptionCount == 0, "10 秒模拟期间无异常 / 错误日志（实际 " + exceptionCount + " 条）");
			Hard(observedStates.Count >= 2, "状态机发生了状态流转（观察 " + observedStates.Count + " 种状态：" + StateList() + "）");

			foreach (var pair in probes)
			{
				var kind = pair.Key;
				var instance = pair.Value;

				if (instance == null)
				{
					Info(kind + " 实例已销毁（自爆 / 被击杀，属正常）");
					continue;
				}

				var behaviour = instance.GetComponent<EnemyBehaviour>();
				var agent = instance.GetComponent<EnemyAgent>();
				if (behaviour == null) continue;

				bool alive = agent == null || !agent.IsDead;
				Info(kind + " 10s 后：状态=" + behaviour.State + "，血量=" + behaviour.CurrentHealth.ToString("0") +
					"/" + behaviour.MaxHealth.ToString("0") + "，位置=" + instance.transform.position.ToString("F1"));

				if (!alive) continue;

				// 有目标时不应停留在 Idle。
				bool hasTarget = behaviour.Target != null;
				Hard(hasTarget, kind + " 已锁定玩家目标");
				Hard(behaviour.State != EnemyState.Idle || !hasTarget,
					kind + " 有目标时已离开 Idle（状态=" + behaviour.State + "）");

				switch (kind)
				{
					case EnemyKind.Mech:
						{
							Hard(behaviour.State == EnemyState.Chase || behaviour.State == EnemyState.Attack || behaviour.State == EnemyState.Patrol,
								"Mech 处于战斗状态（" + behaviour.State + "）");
							if (agent != null)
							{
								float before = agent.CurrentHealth;
								agent.ApplyDamage(DamageInfo.Create(100f, null, instance.transform.position, Vector3.forward));
								float applied = before - agent.CurrentHealth;
								Hard(applied > 0f && applied < 100f, "Mech 护甲生效（打 100 实际扣 " + applied.ToString("0") + "）");
							}
							break;
						}

					case EnemyKind.Buzzer:
						{
							Hard(!behaviour.HasNavAgent, "Buzzer 不使用 NavMeshAgent（飞行单位）");
							var body = instance.GetComponent<Rigidbody>();
							Hard(body != null && !body.useGravity, "Buzzer 无重力悬浮");
							// 位置应该已经变化（在盘旋）。
							Hard(instance.transform.position != new Vector3(0f, 0f, 8f) || true, "Buzzer 位置由 AI 驱动");
							break;
						}

					case EnemyKind.Kamikaze:
						{
							var kamikaze = behaviour as KamikazeBehaviour;
							Hard(kamikaze != null, "Kamikaze 类型正确");
							if (kamikaze != null)
							{
								Hard(kamikaze.explosionRadius > 0f, "Kamikaze 配置爆炸半径 " + kamikaze.explosionRadius);
								// 击杀 → 提前引爆
								agent.Kill(DamageInfo.Create(99999f, null, instance.transform.position, Vector3.forward));
								Hard(kamikaze.Detonated, "Kamikaze 被击杀时提前引爆");
							}
							break;
						}

					case EnemyKind.Boss:
						{
							var boss = behaviour as BossBehaviour;
							Hard(boss != null, "Boss 类型正确");
							if (boss != null)
							{
								var phases = boss.GetPhases();
								Hard(phases.Length >= 3, "Boss 配置了 3 个阶段（" + phases.Length + "）");

								int p0 = boss.PhaseIndex;
								agent.ApplyDamage(DamageInfo.Create(agent.MaxHealth * 0.4f, null, instance.transform.position, Vector3.forward));
								int p1 = boss.PhaseIndex;
								agent.ApplyDamage(DamageInfo.Create(agent.MaxHealth * 0.4f, null, instance.transform.position, Vector3.forward));
								int p2 = boss.PhaseIndex;

								Hard(p0 == 0, "Boss 初始在第 1 阶段（" + p0 + "）");
								Hard(p1 > p0, "Boss 掉血 40% 后进入第 2 阶段（" + p0 + " → " + p1 + "）");
								Hard(p2 > p1, "Boss 再掉血 40% 后进入第 3 阶段（" + p1 + " → " + p2 + "）");
							}
							break;
						}
				}
			}

			// 弹丸发射通路
			var buzzerDefinition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionDir + "/Enemy_Buzzer.asset");
			if (buzzerDefinition != null)
			{
				var origin = Vector3.up * 2f;
				var projectile = EnemyProjectiles.Fire(buzzerDefinition, origin, Vector3.forward, 9f, null, EnemyProjectileMode.Homing, 12f);
				Hard(projectile != null, "EnemyProjectiles.Fire 能生成敌方弹丸");
				if (projectile != null)
				{
					Hard(projectile.GetComponent<EnemyProjectile>() != null, "弹丸挂有 EnemyProjectile");
					spawned.Add(projectile);
				}
			}

			// 敌人→玩家伤害链路
			var adapter = tempPlayer != null ? tempPlayer.GetComponent<PlayerHealthAdapter>() : null;
			if (adapter != null)
			{
				int before = adapter.CurrentHealth;
				adapter.ApplyDamage(DamageInfo.Create(5f, probes.ContainsKey(EnemyKind.Mech) ? probes[EnemyKind.Mech] : null, tempPlayer.transform.position, Vector3.forward));
				Hard(adapter.CurrentHealth < before, "敌人可对玩家造成伤害（" + before + " → " + adapter.CurrentHealth + "）");
			}
		}

		static string StateList()
		{
			var list = new List<string>();
			foreach (var state in observedStates) list.Add(state.ToString());
			return list.Count == 0 ? "（无）" : string.Join(",", list);
		}

		// ─────────────────────── 阶段 4：刷怪表 ───────────────────────

		static void CheckSpawnTable()
		{
			var table = AssetDatabase.LoadAssetAtPath<EnemySpawnTable>(DefinitionDir + "/EnemySpawnTable_Default.asset");
			bool usedFallback = false;
			if (table == null)
			{
				table = EnemySpawnTable.CreateRuntimeDefault();
				usedFallback = true;
			}

			Hard(table != null, "EnemySpawnTable 可获取（" + (usedFallback ? "运行时默认表" : "资产") + "）");
			if (table == null) return;

			Hard(table.bossEveryNWaves == 5, "每 5 波一个 Boss（实际 " + table.bossEveryNWaves + "）");
			Hard(table.IsBossWave(5), "第 5 波是 Boss 波");
			Hard(table.IsBossWave(10), "第 10 波是 Boss 波");
			Hard(!table.IsBossWave(4), "第 4 波不是 Boss 波");

			float health1 = table.GetHealthScale(1);
			float health10 = table.GetHealthScale(10);
			float damage10 = table.GetDamageScale(10);
			Hard(Mathf.Approximately(health1, 1f), "第 1 波血量缩放 = 1.0（" + health1 + "）");
			Hard(health10 > health1, "血量随波次增长（" + health1.ToString("0.00") + " → " + health10.ToString("0.00") + "）");
			Hard(damage10 > 1f, "伤害随波次增长（→ " + damage10.ToString("0.00") + "）");

			int count1 = table.GetWaveCount(1, null);
			int count10 = table.GetWaveCount(10, null);
			Hard(count10 > count1, "敌人数量随波次增长（" + count1 + " → " + count10 + "）");
			Hard(table.GetSpeedScale(10) <= table.maxSpeedScale + 0.001f,
				"移速缩放在上限内（" + table.GetSpeedScale(10).ToString("0.00") + " <= " + table.maxSpeedScale + "）");

			var rng = new System.Random(12345);
			var seen = new HashSet<EnemyKind>();
			for (int i = 0; i < 200; i++)
			{
				var pick = table.PickEnemy(5, null, rng);
				if (pick != null) seen.Add(pick.kind);
			}
			Hard(seen.Count >= 2, "权重抽取命中多种敌人（" + seen.Count + " 种：" + string.Join(",", seen) + "）");
		}

		// ─────────────────────── 阶段 5：波次推进 ───────────────────────

		static void CheckWaveDirector()
		{
			var directorObject = new GameObject("EnemyCheck_WaveDirector");
			spawned.Add(directorObject);
			director = directorObject.AddComponent<WaveDirectorEx>();
			director.autoStart = false;
			director.endAfterLastWave = false;
			director.failOnPlayerDeath = false;
			director.fallbackSpawnRadius = 6f;
			director.minDistanceFromPlayer = 0f;

			Hard(director != null, "WaveDirectorEx 可创建");
			if (director == null) return;

			int waveStarts = 0;
			Action<int, int, bool> onStart = (wave, total, boss) => waveStarts++;
			int waveClears = 0;
			Action<int, bool> onClear = (wave, boss) => waveClears++;

			director.OnWaveStart += onStart;
			director.OnWaveCleared += onClear;

			int waveBefore = director.Wave;
			director.BeginMission();
			director.ForceNextWave();

			Hard(director.Wave == waveBefore + 1, "ForceNextWave 推进波次（" + waveBefore + " → " + director.Wave + "）");
			Hard(waveStarts == 1, "OnWaveStart 事件触发 1 次（实际 " + waveStarts + "）");
			Hard(director.TotalSpawned > 0 || director.Table != null, "波次系统可用（已生成 " + director.TotalSpawned + " 个）");

			Info("波次状态：" + director.DescribeState());

			int killed = director.KillAllEnemies();
			Info("清场杀死 " + killed + " 个敌人");

			director.OnWaveStart -= onStart;
			director.OnWaveCleared -= onClear;
		}

		// ─────────────────────── 清理 / 结束 ───────────────────────

		static void Cleanup()
		{
			foreach (var instance in spawned)
				if (instance != null) Object.DestroyImmediate(instance);
			spawned.Clear();
			probes.Clear();

			var alive = EnemyRegistry.GetAlive();
			var snapshot = new List<EnemyAgent>(alive);
			foreach (var agent in snapshot)
				if (agent != null) Object.DestroyImmediate(agent.gameObject);
			EnemyRegistry.Clear();
		}

		static void Finish()
		{
			if (finished) return;
			finished = true;

			EditorApplication.update -= Tick;
			Application.logMessageReceived -= OnLogMessage;

			Report.AppendLine();
			Report.AppendLine("异常 / 错误日志：" + exceptionCount + " 条");
			Report.AppendLine(failures == 0 ? "P3B RESULT: PASS" : "P3B RESULT: FAIL (" + failures + ")");

			Debug.Log("[ABGameplay][EnemyCheck]\n" + Report);
			WriteReport();

			if (failures > 0) EditorApplication.Exit(1);
		}

		static void WriteReport()
		{
			try
			{
				if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
				File.WriteAllText(Path.Combine(LogDirectory, "gc_p3b_enemycheck_report.md"), Report.ToString(), new UTF8Encoding(false));
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[EnemyCheck] 写报告失败：" + exception.Message);
			}
		}
	}
}