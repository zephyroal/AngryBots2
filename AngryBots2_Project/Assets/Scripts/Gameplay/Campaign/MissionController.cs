using System;
using ABGameplay.Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 关卡目标控制器：清场 / 护送 / 防守 / 限时四种目标模式。
	/// <para>
	/// 敌人来源统一交给 <see cref="WaveDirectorEx"/>；本组件只负责判定胜负、
	/// 目标进度与 UI 文案，以及把结果回传给 <see cref="CampaignManager"/>。
	/// </para>
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MissionController : MonoBehaviour
	{
		public static MissionController Instance { get; private set; }

		public MissionDefinition Definition { get; set; }
		public WaveDirectorEx Director { get; private set; }

		public bool Running { get; private set; }
		public bool Completed { get; private set; }
		public bool Failed { get; private set; }
		public float Progress { get; private set; }
		public string ObjectiveText { get; private set; }
		public int Kills { get; private set; }
		public float TimeLeft { get; private set; }

		/// <summary>(进度 0..1, 目标文案)</summary>
		public event Action<float, string> ObjectiveUpdated;

		/// <summary>关卡结束：(是否胜利)</summary>
		public event Action<bool> Finished;

		PlayerHealth playerHealth;
		Transform player;
		ScoreSystem score;
		EnemySpawnTable sourceTable;

		float missionStart;
		float escortOutsideTimer;
		float defendOutsideTimer;
		Vector3 defendCenter;
		Transform escortTarget;
		Vector3 escortDestination;
		bool subscribed;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		void OnDestroy()
		{
			if (Instance == this) Instance = null;
			Unsubscribe();
		}

		void Start()
		{
			if (!Running && !Completed && !Failed) Begin(Definition);
		}

		public void Begin(MissionDefinition definition)
		{
			Definition = definition != null
				? definition
				: (CampaignManager.Instance != null ? CampaignManager.Instance.Current : CampaignManager.DefaultMissionForScene(gameObject.scene.name));

			if (Definition == null)
			{
				Debug.LogWarning("[MissionController] 没有关卡定义，跳过目标系统");
				return;
			}

			Kills = 0;
			Completed = false;
			Failed = false;
			Running = true;
			Progress = 0f;
			missionStart = Time.time;
			TimeLeft = Definition.timeLimit;

			player = PlayerSource.Player;
			if (player == null)
			{
				var health = FindObjectOfType<PlayerHealth>();
				if (health != null) player = health.transform;
			}
			playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
			score = ScoreSystem.Instance != null ? ScoreSystem.Instance : ScoreSystem.EnsureInstance();

			Subscribe();
			SetupDirector();

			switch (Definition.mode)
			{
				case ObjectiveMode.Defend:
					defendCenter = player != null ? player.position : transform.position;
					break;

				case ObjectiveMode.Escort:
					SetupEscort();
					break;
			}

			PublishObjective(true);
			Debug.Log("[MissionController] 开始关卡: " + Definition.displayName + " 模式=" + Definition.mode);
		}

		void Subscribe()
		{
			if (subscribed) return;
			subscribed = true;
			GameEvents.EnemyKilled += OnEnemyKilled;
		}

		void Unsubscribe()
		{
			if (!subscribed) return;
			subscribed = false;
			GameEvents.EnemyKilled -= OnEnemyKilled;
			if (Director != null) Director.OnMissionEnded -= OnDirectorEnded;
		}

		void SetupDirector()
		{
			Director = FindObjectOfType<WaveDirectorEx>();
			var config = CampaignManager.Config;

			if (Director == null)
			{
				var go = new GameObject("[WaveDirectorEx]");
				if (player != null) go.transform.position = player.position + player.forward * 6f;
				Director = go.AddComponent<WaveDirectorEx>();
			}

			sourceTable = config != null && config.enemyTable != null ? config.enemyTable : null;
			EnemySpawnTable table;
			if (sourceTable != null)
			{
				table = Instantiate(sourceTable);
				table.name = sourceTable.name + "(Runtime)";
			}
			else
			{
				table = EnemySpawnTable.CreateRuntimeDefault(config != null ? config.fallbackEnemyPrefab : null);
			}

			if (Definition.mode == ObjectiveMode.ClearWaves)
			{
				table.totalWaves = Mathf.Max(1, Definition.totalWaves);
			}
			else
			{
				table.totalWaves = 0; // 无限波
			}

			Director.spawnTable = table;
			Director.fallbackEnemyPrefab = config != null ? config.fallbackEnemyPrefab : null;
			Director.autoStart = true;
			Director.endAfterLastWave = Definition.mode == ObjectiveMode.ClearWaves;
			Director.failOnPlayerDeath = true;
			Director.OnMissionEnded += OnDirectorEnded;
		}

		void SetupEscort()
		{
			Vector3 origin = player != null ? player.position : transform.position;
			Vector3 forward = player != null ? player.forward : Vector3.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

			var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			go.name = "[Escort VIP]";
			go.transform.position = origin + forward.normalized * 3f + Vector3.up * 0.1f;

			var collider = go.GetComponent<Collider>();
			if (collider != null) collider.isTrigger = true;

			var agent = go.AddComponent<NavMeshAgent>();
			agent.radius = 0.45f;
			agent.height = 1.8f;
			agent.speed = 2.2f;
			agent.stoppingDistance = 1.2f;
			escortTarget = go.transform;
			escortTarget.gameObject.AddComponent<EscortMarker>();

			Vector3 destination = origin + forward.normalized * Mathf.Max(10f, Definition.escortDistance);
			if (NavMesh.SamplePosition(destination, out var hit, 12f, NavMesh.AllAreas))
				destination = hit.position;

			escortDestination = destination;
			if (agent.isOnNavMesh) agent.SetDestination(destination);
		}

		void Update()
		{
			if (!Running) return;
			TimeLeft = Mathf.Max(0f, Definition.timeLimit - (Time.time - missionStart));
			EvaluateObjective();
			PublishObjective(false);
		}

		/// <summary>执行一次目标判定（Update 调用；自检也可直接调用）。</summary>
		public void EvaluateObjective()
		{
			if (!Running) return;

			switch (Definition.mode)
			{
				case ObjectiveMode.ClearWaves:
					UpdateClearWaves();
					break;
				case ObjectiveMode.Timed:
					UpdateTimed();
					break;
				case ObjectiveMode.Defend:
					UpdateDefend();
					break;
				case ObjectiveMode.Escort:
					UpdateEscort();
					break;
			}
		}

		void UpdateClearWaves()
		{
			Progress = Director != null && Director.TotalWaves > 0
				? Mathf.Clamp01((float)Mathf.Max(0, Director.Wave - 1) / Director.TotalWaves)
				: 0f;
		}

		void UpdateTimed()
		{
			Progress = Definition.targetKills > 0 ? Mathf.Clamp01((float)Kills / Definition.targetKills) : 0f;

			if (Kills >= Definition.targetKills)
			{
				Finish(true);
				return;
			}

			if (Definition.timeLimit > 0f && TimeLeft <= 0f) Finish(false);
		}

		void UpdateDefend()
		{
			float elapsed = Time.time - missionStart;
			Progress = Definition.defendSeconds > 0f ? Mathf.Clamp01(elapsed / Definition.defendSeconds) : 1f;

			bool inside = player != null &&
				Vector3.Distance(Flat(player.position), Flat(defendCenter)) <= Definition.defendRadius;

			if (inside) defendOutsideTimer = Mathf.Max(0f, defendOutsideTimer - Time.deltaTime * 2f);
			else defendOutsideTimer += Time.deltaTime;

			if (defendOutsideTimer >= 8f)
			{
				Finish(false);
				return;
			}

			if (Progress >= 1f) Finish(true);
		}

		void UpdateEscort()
		{
			if (escortTarget == null)
			{
				Finish(false);
				return;
			}

			float total = Mathf.Max(1f, Definition.escortDistance);
			float remaining = Vector3.Distance(Flat(escortTarget.position), Flat(escortDestination));
			Progress = Mathf.Clamp01(1f - remaining / total);

			if (player != null)
			{
				float separation = Vector3.Distance(Flat(player.position), Flat(escortTarget.position));
				if (separation > 20f) escortOutsideTimer += Time.deltaTime;
				else escortOutsideTimer = Mathf.Max(0f, escortOutsideTimer - Time.deltaTime * 1.5f);

				if (escortOutsideTimer >= 6f)
				{
					Finish(false);
					return;
				}

				var agent = escortTarget.GetComponent<NavMeshAgent>();
				if (agent != null && agent.isOnNavMesh && separation < 18f && !agent.hasPath)
					agent.SetDestination(escortDestination);
			}

			if (remaining <= 2.5f) Finish(true);
		}

		void PublishObjective(bool force)
		{
			string text;
			switch (Definition.mode)
			{
				case ObjectiveMode.ClearWaves:
					text = "CLEAR ALL WAVES   " + (Director != null ? Mathf.Max(1, Director.Wave) : 1) +
						" / " + Definition.totalWaves;
					break;
				case ObjectiveMode.Timed:
					text = "KILL " + Definition.targetKills + "   " + Kills + " / " + Definition.targetKills +
						"   " + Mathf.CeilToInt(TimeLeft) + "s";
					break;
				case ObjectiveMode.Defend:
					text = "DEFEND ZONE   " + Mathf.CeilToInt(Mathf.Max(0f, Definition.defendSeconds - (Time.time - missionStart))) + "s";
					break;
				default:
					text = "ESCORT VIP TO EXTRACTION   " + Mathf.RoundToInt(Progress * 100f) + "%";
					break;
			}

			if (!force && text == ObjectiveText) return;
			ObjectiveText = text;
			if (ObjectiveUpdated != null) ObjectiveUpdated(Progress, ObjectiveText);
		}

		void OnEnemyKilled(EnemyKind kind, Vector3 position, GameObject killer, int scoreValue)
		{
			Kills++;
		}

		void OnDirectorEnded(bool victory)
		{
			if (Definition.mode != ObjectiveMode.ClearWaves)
			{
				if (!victory) Finish(false);
				return;
			}

			Finish(victory);
		}

		/// <summary>结束关卡（幂等）。</summary>
		public void Finish(bool won)
		{
			if (!Running || Completed || Failed) return;

			Running = false;
			Completed = won;
			Failed = !won;
			Progress = won ? 1f : Progress;

			if (Director != null)
			{
				Director.OnMissionEnded -= OnDirectorEnded;
				Director.enabled = false;
			}

			if (score != null)
			{
				if (won) score.AddScore(Mathf.Max(1, Definition.rewardScore));
				score.Persist();
			}

			if (CampaignManager.Instance != null) CampaignManager.Instance.ReportResult(won);

			Debug.Log("[MissionController] 关卡结束: " + Definition.displayName + " 胜利=" + won +
				" 击杀=" + Kills + " 用时=" + (Time.time - missionStart).ToString("0.0") + "s");

			if (Finished != null) Finished(won);
			GameEvents.RaiseMissionEnded(won);
		}

		public void Restart()
		{
			if (CampaignManager.Instance != null) CampaignManager.Instance.Retry();
		}

		static Vector3 Flat(Vector3 value)
		{
			value.y = 0f;
			return value;
		}

		/// <summary>自检用状态描述。</summary>
		public string DescribeState()
		{
			return "mission=" + (Definition != null ? Definition.displayName : "<none>") +
				" mode=" + (Definition != null ? Definition.mode.ToString() : "-") +
				" running=" + Running + " completed=" + Completed + " failed=" + Failed +
				" kills=" + Kills + " progress=" + Progress.ToString("0.00") +
				" timeLeft=" + TimeLeft.ToString("0.0") +
				" objective=" + ObjectiveText;
		}
	}

	/// <summary>护送目标标记（便于自检与 UI 识别）。</summary>
	public sealed class EscortMarker : MonoBehaviour
	{
	}
}