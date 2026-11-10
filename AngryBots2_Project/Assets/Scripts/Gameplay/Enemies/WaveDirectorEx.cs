using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// 波次导演（扩展版）：多生成点分组 + 波次编排 + 难度曲线 + 每 N 波 Boss 波。
	/// <para>
	/// 与旧 <c>WaveDirector</c> 并存：旧脚本一行未改，仍可被旧场景使用。
	/// 本组件额外提供 <see cref="OnWaveStart"/> / <see cref="OnWaveCleared"/> /
	/// <see cref="OnMissionEnded"/> 事件，并同时广播到 <see cref="GameEvents"/>。
	/// </para>
	/// <para>
	/// 存活敌人统计走 <see cref="EnemyRegistry"/>（O(1)），不再每帧 <c>FindObjectsOfType</c>。
	/// </para>
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class WaveDirectorEx : MonoBehaviour
	{
		/// <summary>场景单例（Boss 召唤随从时用来登记）。</summary>
		public static WaveDirectorEx Instance { get; private set; }

		[Header("Data")]
		[Tooltip("刷怪表。为空时自动生成默认表（无美术资产也能跑）。")]
		public EnemySpawnTable spawnTable;

		[Tooltip("为空的刷怪表补一个默认 Spider 预制体（便于在没有配置资产的场景里跑起来）。")]
		public GameObject fallbackEnemyPrefab;

		[Header("Spawn Points")]
		[Tooltip("生成点分组。为空时使用自身位置附近的随机点。")]
		public List<SpawnGroup> spawnGroups = new List<SpawnGroup>();

		[Tooltip("没有生成点时，在导演周围多大的半径内随机取点。")]
		public float fallbackSpawnRadius = 10f;

		[Tooltip("生成点必须落在 NavMesh 上的搜索半径。")]
		public float navMeshSampleRadius = 6f;

		[Tooltip("与玩家的最小生成距离，避免贴脸刷怪。")]
		public float minDistanceFromPlayer = 8f;

		[Header("Mission")]
		[Tooltip("自动开始第一波。")]
		public bool autoStart = true;

		[Tooltip("开局前等待时间。")]
		public float startDelay = 1.5f;

		[Tooltip("玩家死亡时任务失败。")]
		public bool failOnPlayerDeath = true;

		[Tooltip("打完最后一波后判定胜利。")]
		public bool endAfterLastWave = true;

		[Header("Runtime - Read Only")]
		[SerializeField] int currentWave;
		[SerializeField] int aliveEnemies;
		[SerializeField] float timeLeft;
		[SerializeField] bool missionOver;
		[SerializeField] bool victory;
		[SerializeField] float nextWaveCountdown;

		// ─── 事件 ───

		/// <summary>波次开始：(波次号, 本波敌人数, 是否 Boss 波)。</summary>
		public event Action<int, int, bool> OnWaveStart;

		/// <summary>波次清空：(波次号, 是否 Boss 波)。</summary>
		public event Action<int, bool> OnWaveCleared;

		/// <summary>任务结束：(是否胜利)。</summary>
		public event Action<bool> OnMissionEnded;

		/// <summary>单个敌人生成：(敌人, 波次号)。</summary>
		public event Action<EnemyAgent, int> OnEnemySpawned;

		// ─── 属性 ───

		/// <summary>当前波次（从 1 开始，0 表示还没开始）。</summary>
		public int Wave { get { return currentWave; } }

		/// <summary>当前存活敌人（来自注册表）。</summary>
		public int AliveEnemies { get { return aliveEnemies; } }

		/// <summary>当前波剩余时间。</summary>
		public float TimeLeft { get { return timeLeft; } }

		/// <summary>任务是否结束。</summary>
		public bool MissionOver { get { return missionOver; } }

		/// <summary>是否胜利。</summary>
		public bool Victory { get { return victory; } }

		/// <summary>下一波倒计时。</summary>
		public float NextWaveCountdown { get { return nextWaveCountdown; } }

		/// <summary>是否 Boss 波。</summary>
		public bool IsBossWave { get; private set; }

		/// <summary>总波数。</summary>
		public int TotalWaves { get { return Table != null ? Table.totalWaves : 10; } }

		/// <summary>累计击杀数（订阅注册表统计）。</summary>
		public int TotalKills { get; private set; }

		/// <summary>累计生成的敌人数。</summary>
		public int TotalSpawned { get; private set; }

		/// <summary>当前使用的刷怪表（必要时创建默认表）。</summary>
		public EnemySpawnTable Table
		{
			get
			{
				if (spawnTable == null)
				{
					spawnTable = EnemySpawnTable.CreateRuntimeDefault(fallbackEnemyPrefab);
					Debug.LogWarning("[WaveDirectorEx] 未配置 EnemySpawnTable，已生成默认表（" + spawnTable.name + "）");
				}
				return spawnTable;
			}
		}

		// ─── 内部状态 ───

		enum Phase { Idle, Countdown, Combat, Intermission, Finished }

		Phase phase = Phase.Idle;
		float phaseTimer;
		int waveTotal;           // 本波计划生成总数
		int waveSpawned;         // 本波已生成数
		bool waveClearedAnnounced;
		PlayerHealth playerHealth;
		Coroutine spawnRoutine;
		readonly System.Random rng = new System.Random();

		// ─── 生命周期 ───

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogWarning("[WaveDirectorEx] 场景里已有另一个实例，禁用重复组件");
				enabled = false;
				return;
			}
			Instance = this;
		}

		void Start()
		{
			var player = GameObject.FindGameObjectWithTag("Player");
			if (player == null) player = GameplayBootstrap.FindAnyPlayerFallback();
			if (player != null)
			{
				playerHealth = player.GetComponent<PlayerHealth>();
				if (playerHealth != null && failOnPlayerDeath) playerHealth.Died += OnPlayerDied;
			}

			PlayerLocator.GetPlayer();
			GameplayBootstrap.EnsureInstance();

			// 累计击杀：订阅注册表注销（死亡注销才算击杀）。
			EnemyRegistry.EnemyUnregistered += OnEnemyUnregisteredCounted;

			if (autoStart) BeginMission();
		}

		void OnDestroy()
		{
			if (Instance == this) Instance = null;
			if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
			EnemyRegistry.EnemyUnregistered -= OnEnemyUnregisteredCounted;
			StopSpawnRoutine();
		}

		void Update()
		{
			aliveEnemies = EnemyRegistry.AliveCount;

			if (missionOver) return;

			switch (phase)
			{
				case Phase.Countdown:
					phaseTimer -= Time.deltaTime;
					if (phaseTimer <= 0f) StartNextWave();
					break;

				case Phase.Combat:
					timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);

					if (waveClearedAnnounced) break;
					if (waveSpawned < waveTotal) break;
					if (aliveEnemies > 0) break;

					waveClearedAnnounced = true;
					OnWaveClearedInternal();
					break;

				case Phase.Intermission:
					phaseTimer -= Time.deltaTime;
					nextWaveCountdown = Mathf.Max(0f, phaseTimer);
					if (phaseTimer <= 0f) StartNextWave();
					break;
			}
		}

		// ─── 任务流程 ───

		/// <summary>开始任务（重置波次计数）。</summary>
		public void BeginMission()
		{
			if (missionOver) return;

			currentWave = 0;
			missionOver = false;
			victory = false;
			TotalKills = 0;
			TotalSpawned = 0;
			IsBossWave = false;

			phase = Phase.Countdown;
			phaseTimer = Mathf.Max(0f, Table.firstWaveDelay > 0f ? Table.firstWaveDelay : startDelay);
			nextWaveCountdown = phaseTimer;
		}

		/// <summary>手动推进到下一波（跳过倒计时）。</summary>
		public void ForceNextWave()
		{
			if (missionOver) return;
			phaseTimer = 0f;
			if (phase == Phase.Countdown || phase == Phase.Intermission) StartNextWave();
		}

		void StartNextWave()
		{
			currentWave++;
			int totalWaves = TotalWaves;

			if (endAfterLastWave && totalWaves > 0 && currentWave > totalWaves)
			{
				EndMission(true);
				return;
			}

			var table = Table;
			IsBossWave = table.IsBossWave(currentWave);
			waveClearedAnnounced = false;
			waveTotal = IsBossWave ? 1 : table.GetWaveCount(currentWave, null);
			waveSpawned = 0;
			timeLeft = table.waveTimeLimit > 0f ? table.waveTimeLimit : 90f;

			// 波次奖励：手雷补给 + 回血 + 短暂无敌。
			ApplyWaveRewards(table);

			phase = Phase.Combat;
			nextWaveCountdown = 0f;

			if (OnWaveStart != null) OnWaveStart(currentWave, waveTotal, IsBossWave);
			GameEvents.RaiseWaveStarted(currentWave, waveTotal, IsBossWave);

			Debug.Log("[WaveDirectorEx] 第 " + currentWave + " 波开始（" + waveTotal + " 个敌人" +
				(IsBossWave ? "，BOSS 波" : string.Empty) + "，限时 " + timeLeft.ToString("0") + "s）");

			StopSpawnRoutine();
			spawnRoutine = StartCoroutine(SpawnWaveRoutine(waveTotal, IsBossWave));
		}

		void ApplyWaveRewards(EnemySpawnTable table)
		{
			var player = PlayerSource.Player;
			if (player == null) return;

			var weapons = player.GetComponent<WeaponSystem>();
			if (weapons != null && table.grenadeRefillPerWave > 0) weapons.AddGrenades(table.grenadeRefillPerWave);

			if (playerHealth != null)
			{
				if (table.healPerWave > 0) playerHealth.Heal(table.healPerWave);
				if (table.spawnProtectionTime > 0f) playerHealth.GrantInvulnerability(table.spawnProtectionTime);
			}
		}

		IEnumerator SpawnWaveRoutine(int count, bool bossWave)
		{
			var table = Table;

			if (bossWave)
			{
				yield return new WaitForSeconds(0.5f);
				SpawnBoss(table);
				waveSpawned = 1;
				yield break;
			}

			float interval = table.spawnInterval > 0f ? table.spawnInterval : 0.4f;

			for (int i = 0; i < count; i++)
			{
				if (missionOver) yield break;

				var definition = table.PickEnemy(currentWave, null, rng);
				if (definition != null) SpawnEnemy(definition);

				waveSpawned = i + 1;
				yield return new WaitForSeconds(interval);
			}
		}

		void SpawnBoss(EnemySpawnTable table)
		{
			var bossDefinition = table.bossDefinition;
			if (bossDefinition == null)
			{
				Debug.LogWarning("[WaveDirectorEx] Boss 波但未配置 Boss 定义，改为刷普通敌人");
				var fallback = table.PickEnemy(currentWave, null, rng);
				if (fallback != null) SpawnEnemy(fallback);
				return;
			}

			SpawnEnemy(bossDefinition);
		}

		// ─── 刷怪 ───

		/// <summary>生成一个敌人，返回其实例。</summary>
		public GameObject SpawnEnemy(EnemyDefinition definition)
		{
			if (definition == null) return null;

			GameObject prefab = definition.prefab;
			if (prefab == null && definition.kind == EnemyKind.Spider) prefab = fallbackEnemyPrefab;

			if (prefab == null)
			{
				Debug.LogWarning("[WaveDirectorEx] 敌人定义 " + definition.displayName + " 没有绑定预制体，跳过生成");
				return null;
			}

			var table = Table;
			Vector3 position = PickSpawnPosition();
			Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

			var instance = Instantiate(prefab, position, rotation);
			instance.name = prefab.name + "_W" + currentWave;

			PrepareSpawnedEnemy(instance, definition, currentWave, table);

			waveSpawned++;
			TotalSpawned++;

			if (OnEnemySpawned != null)
			{
				var agent = instance.GetComponent<EnemyAgent>();
				if (agent != null) OnEnemySpawned(agent, currentWave);
			}

			return instance;
		}

		/// <summary>登记外部生成的敌人（Boss 召唤 / 关卡脚本直接刷怪）。</summary>
		public void RegisterExternalSpawn(GameObject instance)
		{
			if (instance == null) return;
			var agent = instance.GetComponent<EnemyAgent>();
			if (agent != null)
			{
				agent.SpawnWave = currentWave;
				// 外部召唤的随从计入本波存活，但不算本波的「计划生成数」（否则清波判定会被卡住）。
				if (agent.SpawnWave == currentWave) waveTotal = Mathf.Max(waveTotal, waveSpawned + AliveEnemies);
			}
			TotalSpawned++;
		}

		/// <summary>给刚生成的敌人套上定义数值与难度缩放。</summary>
		public static void PrepareSpawnedEnemy(GameObject instance, EnemyDefinition definition, int wave, EnemySpawnTable table)
		{
			if (instance == null || definition == null) return;

			var agent = instance.GetComponent<EnemyAgent>();
			if (agent == null) agent = instance.AddComponent<EnemyAgent>();

			agent.kind = definition.kind;
			agent.definition = definition;
			agent.scoreValue = definition.score;
			agent.armor = definition.armor;
			agent.SpawnWave = wave;

			float healthScale = table != null ? table.GetHealthScale(wave) : 1f;
			float damageScale = table != null ? table.GetDamageScale(wave) : 1f;
			float speedScale = table != null ? table.GetSpeedScale(wave) : 1f;

			// 血量：优先设置适配器的 max。
			agent.statScale = healthScale;
			var health = instance.GetComponent<EnemyHealthEx>();
			if (health != null) health.ResetHealth(Mathf.RoundToInt(definition.maxHealth * healthScale));
			else agent.EnsureStandaloneHealth();

			// 行为组件：设置定义与难度缩放。
			var behaviour = instance.GetComponent<EnemyBehaviour>();
			if (behaviour != null)
			{
				behaviour.definition = definition;
				behaviour.ApplySpawnParameters(wave, damageScale);

				var nav = instance.GetComponent<NavMeshAgent>();
				if (nav != null && nav.enabled)
				{
					nav.speed = definition.moveSpeed * speedScale;
					nav.angularSpeed = definition.turnSpeed;
				}
			}
		}

		Vector3 PickSpawnPosition()
		{
			Vector3 origin = transform.position;
			Transform groupPoint = null;

			// 按可用性随机挑一个分组。
			if (spawnGroups != null && spawnGroups.Count > 0)
			{
				var candidates = new List<SpawnGroup>();
				for (int i = 0; i < spawnGroups.Count; i++)
				{
					var group = spawnGroups[i];
					if (group == null || group.ValidPointCount == 0) continue;
					if (UnityEngine.Random.value > group.availability) continue;
					candidates.Add(group);
				}

				if (candidates.Count > 0)
				{
					var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
					groupPoint = chosen.PickPoint();
				}
			}

			if (groupPoint != null) origin = groupPoint.position;
			else
			{
				Vector2 disc = UnityEngine.Random.insideUnitCircle * fallbackSpawnRadius;
				origin += new Vector3(disc.x, 0f, disc.y);
			}

			// 与玩家保持最小距离。
			var player = PlayerSource.Player;
			if (player != null && minDistanceFromPlayer > 0f)
			{
				Vector3 toPlayer = player.position - origin;
				toPlayer.y = 0f;
				if (toPlayer.magnitude < minDistanceFromPlayer)
				{
					Vector3 push = toPlayer.sqrMagnitude > 0.001f ? -toPlayer.normalized : UnityEngine.Random.insideUnitSphere;
					push.y = 0f;
					origin += push.normalized * (minDistanceFromPlayer + 2f);
				}
			}

			// 贴合 NavMesh。
			if (NavMesh.SamplePosition(origin, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
				origin = hit.position;
			else if (Physics.Raycast(origin + Vector3.up * 30f, Vector3.down, out var groundHit, 60f))
				origin = groundHit.point;

			return origin;
		}

		// ─── 波次 / 任务结束 ───

		void OnWaveClearedInternal()
		{
			var table = Table;
			bool boss = IsBossWave;

			if (OnWaveCleared != null) OnWaveCleared(currentWave, boss);
			GameEvents.RaiseWaveCleared(currentWave, boss);

			Debug.Log("[WaveDirectorEx] 第 " + currentWave + " 波清空" + (boss ? "（BOSS）" : string.Empty));

			// 计时未超时：剩余时间折算为额外奖励（交给计分系统通过事件处理）。
			float timeBonus = Mathf.Max(0f, timeLeft);

			if (endAfterLastWave && TotalWaves > 0 && currentWave >= TotalWaves)
			{
				EndMission(true);
				return;
			}

			phase = Phase.Intermission;
			phaseTimer = Mathf.Max(0.5f, table.waveDelay);
			nextWaveCountdown = phaseTimer;

			// 时间奖励走事件总线（ScoreChanged 由计分系统消费）。
			int bonus = Mathf.RoundToInt(timeBonus * 2f);
			if (bonus > 0) GameEvents.RaiseScoreChanged(bonus, bonus);
		}

		/// <summary>结束任务。</summary>
		public void EndMission(bool won)
		{
			if (missionOver) return;

			missionOver = true;
			victory = won;
			phase = Phase.Finished;
			StopSpawnRoutine();

			Debug.Log("[WaveDirectorEx] 任务结束，胜利=" + won + "，波次=" + currentWave + "，击杀=" + TotalKills);

			if (OnMissionEnded != null) OnMissionEnded(won);
			GameEvents.RaiseMissionEnded(won);
		}

		/// <summary>重开当前场景。</summary>
		public void Restart()
		{
			var scene = SceneManager.GetActiveScene();
			SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
		}

		void OnPlayerDied()
		{
			EndMission(false);
			GameEvents.RaisePlayerDied();
		}

		void StopSpawnRoutine()
		{
			if (spawnRoutine != null)
			{
				StopCoroutine(spawnRoutine);
				spawnRoutine = null;
			}
		}

		// ─── 自检 / 调试辅助 ───

		void OnEnemyUnregisteredCounted(EnemyAgent agent)
		{
			if (agent != null && agent.IsDead) TotalKills++;
		}

		/// <summary>立即清空场上所有敌人（自检 / 调试用）。</summary>
		public int KillAllEnemies()
		{
			var alive = EnemyRegistry.GetAlive();
			var snapshot = new List<EnemyAgent>(alive);
			int killed = 0;
			foreach (var agent in snapshot)
			{
				if (agent == null || agent.IsDead) continue;
				agent.Kill(DamageInfo.Create(99999f, null, agent.transform.position, Vector3.forward));
				killed++;
			}
			return killed;
		}

		/// <summary>当前阶段的可读描述（自检用）。</summary>
		public string DescribeState()
		{
			return "WaveDirectorEx wave=" + currentWave + "/" + TotalWaves +
				" phase=" + phase + " alive=" + aliveEnemies +
				" spawned=" + waveSpawned + "/" + waveTotal +
				" boss=" + IsBossWave + " over=" + missionOver;
		}
	}
}