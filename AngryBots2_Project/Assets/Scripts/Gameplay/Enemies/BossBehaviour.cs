using System.Collections.Generic;
using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// Boss 的单个阶段配置：血量阈值 + 该阶段启用的攻击模式。
	/// </summary>
	[System.Serializable]
	public sealed class BossPhase
	{
		[Tooltip("阶段名（HUD / 日志用）。")]
		public string name = "Phase 1";

		[Tooltip("进入该阶段的血量比例上限（1 = 满血）。按从高到低排列。")]
		[Range(0.01f, 1f)]
		public float healthThreshold = 1f;

		[Tooltip("该阶段是否使用弹幕。")]
		public bool useBarrage = true;

		[Tooltip("该阶段是否使用震地。")]
		public bool useSlam = true;

		[Tooltip("该阶段是否召唤小怪。")]
		public bool useSummon = false;

		[Tooltip("弹幕弹丸数。")]
		public int barrageCount = 5;

		[Tooltip("弹幕扩散角（度）。")]
		public float barrageSpread = 55f;

		[Tooltip("震地半径（米）。")]
		public float slamRadius = 7f;

		[Tooltip("震地伤害倍率（相对 AttackDamage）。")]
		public float slamDamageMultiplier = 2.2f;

		[Tooltip("召唤数量。")]
		public int summonCount = 3;

		[Tooltip("攻击轮转间隔（秒）。")]
		public float attackInterval = 2.2f;

		/// <summary>默认阶段表：三阶段，血量越低越凶。</summary>
		public static BossPhase[] CreateDefaultPhases()
		{
			return new[]
			{
				new BossPhase
				{
					name = "Phase 1 - Barrage",
					healthThreshold = 1f,
					useBarrage = true,
					useSlam = false,
					useSummon = false,
					barrageCount = 5,
					barrageSpread = 40f,
					attackInterval = 2.4f,
				},
				new BossPhase
				{
					name = "Phase 2 - Barrage + Slam",
					healthThreshold = 0.66f,
					useBarrage = true,
					useSlam = true,
					useSummon = false,
					barrageCount = 7,
					barrageSpread = 70f,
					slamRadius = 8f,
					attackInterval = 1.8f,
				},
				new BossPhase
				{
					name = "Phase 3 - Enrage",
					healthThreshold = 0.33f,
					useBarrage = true,
					useSlam = true,
					useSummon = true,
					barrageCount = 11,
					barrageSpread = 110f,
					summonCount = 3,
					slamRadius = 9f,
					attackInterval = 1.3f,
				},
			};
		}
	}

	/// <summary>
	/// Boss AI（SpiderQueen / MechBoss）：多阶段，按血量阈值切换攻击模式。
	/// <para>
	/// 阶段行为：
	/// <list type="bullet">
	/// <item>Phase 1：环形弹幕；</item>
	/// <item>Phase 2：弹幕 + 震地（范围伤害 + 击退）；</item>
	/// <item>Phase 3（狂暴）：弹幕 + 震地 + 召唤小怪。</item>
	/// </list>
	/// 每轮攻击从当前阶段启用的模式里轮转选择，攻击前有明确的蓄力前摇。
	/// </para>
	/// </summary>
	public sealed class BossBehaviour : EnemyBehaviour
	{
		[Header("Boss - Phases")]
		[Tooltip("阶段配置。留空则使用默认三阶段。")]
		public BossPhase[] phases;

		[Tooltip("阶段切换时播报的日志前缀。")]
		public string logTag = "[Boss]";

		[Header("Boss - Combat")]
		[Tooltip("进入战斗的基础距离。")]
		public float engageRange = 26f;

		[Tooltip("近身时希望保持的距离。")]
		public float preferredDistance = 12f;

		[Tooltip("攻击前摇（秒）。")]
		public float windupTime = 0.85f;

		[Tooltip("阶段切换的无敌时间（秒）。")]
		public float phaseTransitionInvulnerability = 0f;

		[Header("Boss - Summon")]
		[Tooltip("召唤用的敌人预制体（Spider 等）。为空则用定义里的 prefab。")]
		public GameObject summonPrefab;

		[Tooltip("召唤点相对 Boss 的距离。")]
		public float summonRadius = 6f;

		[Header("Boss - Presentation")]
		[Tooltip("震地特效。")]
		public GameObject slamEffect;

		[Tooltip("蓄力特效（前摇期间显示）。")]
		public GameObject windupEffect;

		[Tooltip("击杀奖励分数（覆盖定义里的 score）。")]
		public int bossScoreOverride = 500;

		// ─── 运行时 ───

		enum BossAttack { None, Barrage, Slam, Summon }

		readonly List<EnemyAgent> summoned = new List<EnemyAgent>();
		int phaseIndex;
		BossAttack pendingAttack = BossAttack.None;
		float windupElapsed;
		float nextAttackTime;
		int attackRotation;
		GameObject activeWindupEffect;

		/// <summary>当前阶段序号（从 0 开始）。</summary>
		public int PhaseIndex { get { return phaseIndex; } }

		/// <summary>当前阶段配置。</summary>
		public BossPhase CurrentPhase
		{
			get
			{
				var list = GetPhases();
				return list[Mathf.Clamp(phaseIndex, 0, list.Length - 1)];
			}
		}

		/// <summary>已召唤且仍存活的单位数量。</summary>
		public int AliveSummons
		{
			get
			{
				int count = 0;
				for (int i = 0; i < summoned.Count; i++)
					if (summoned[i] != null && !summoned[i].IsDead) count++;
				return count;
			}
		}

		/// <summary>取阶段表（必要时生成默认值）。</summary>
		public BossPhase[] GetPhases()
		{
			if (phases == null || phases.Length == 0) phases = BossPhase.CreateDefaultPhases();
			return phases;
		}

		protected override void InitializeOnce()
		{
			base.InitializeOnce();

			if (definition == null || definition.kind != EnemyKind.Boss)
			{
				Debug.LogWarning(logTag + " 未配置 Boss 的 EnemyDefinition，使用默认数值");
			}

			phaseIndex = 0;
			nextAttackTime = Time.time + 1.5f;
			summoned.Clear();
		}

		protected override void OnEnterState(EnemyState state)
		{
			switch (state)
			{
				case EnemyState.Attack:
					pendingAttack = BossAttack.None;
					windupElapsed = 0f;
					break;

				case EnemyState.Chase:
					ClearWindupEffect();
					break;

				case EnemyState.Stagger:
					ClearWindupEffect();
					pendingAttack = BossAttack.None;
					break;

				case EnemyState.Dead:
					ClearWindupEffect();
					break;
			}
		}

		protected override void TickState(EnemyState state)
		{
			UpdatePhase();

			switch (state)
			{
				case EnemyState.Idle:
				case EnemyState.Patrol:
					StopMoving();
					if (HasValidTarget()) SetState(EnemyState.Chase);
					break;

				case EnemyState.Chase:
					TickChase();
					break;

				case EnemyState.Attack:
					TickAttack();
					break;

				case EnemyState.Stagger:
					if (StaggerFinished) SetState(HasValidTarget() ? EnemyState.Chase : EnemyState.Idle);
					break;
			}
		}

		// ─── 阶段 ───

		void UpdatePhase()
		{
			var list = GetPhases();
			float ratio = HealthRatio;

			// 阶段按阈值从高到低：取「阈值 >= 当前比例」的最大序号。
			int target = 0;
			for (int i = 0; i < list.Length; i++)
				if (ratio <= list[i].healthThreshold) target = i;

			if (target > phaseIndex)
			{
				phaseIndex = target;
				OnPhaseChanged(CurrentPhase);
			}
		}

		void OnPhaseChanged(BossPhase phase)
		{
			Debug.Log(logTag + " 进入阶段 " + (phaseIndex + 1) + "：" + phase.name + "（血量 " + (HealthRatio * 100f).ToString("0") + "%）");

			// 阶段切换立刻打断当前前摇，进入一次震地作为「过场」。
			ClearWindupEffect();
			pendingAttack = BossAttack.Slam;
			nextAttackTime = Time.time + 0.6f;
		}

		// ─── 追击 ───

		void TickChase()
		{
			if (!HasValidTarget())
			{
				SetState(EnemyState.Idle);
				return;
			}

			FaceTarget();
			float distance = TargetDistance();

			if (distance > engageRange)
			{
				MoveTowards(Target.position, 1f);
				return;
			}

			if (distance > preferredDistance) MoveTowards(Target.position, 0.7f);
			else if (distance < preferredDistance * 0.6f) MoveTowards(transform.position + (transform.position - Target.position).normalized * 4f, 0.5f);
			else StopMoving();

			if (Time.time >= nextAttackTime) SetState(EnemyState.Attack);
		}

		// ─── 攻击 ───

		void TickAttack()
		{
			if (!HasValidTarget())
			{
				SetState(EnemyState.Idle);
				return;
			}

			FaceTarget(120f);
			StopMoving();

			if (pendingAttack == BossAttack.None)
			{
				pendingAttack = ChooseAttack();
				windupElapsed = 0f;
				SpawnWindupEffect();
			}

			windupElapsed += Time.deltaTime;
			if (windupElapsed < windupTime) return;

			ExecuteAttack(pendingAttack);

			ClearWindupEffect();
			pendingAttack = BossAttack.None;
			nextAttackTime = Time.time + Mathf.Max(0.3f, CurrentPhase.attackInterval);
			SetState(EnemyState.Chase);
		}

		BossAttack ChooseAttack()
		{
			var phase = CurrentPhase;

			// 按阶段启用的模式轮转，避免连续同一招。
			var options = new List<BossAttack>(3);
			if (phase.useBarrage) options.Add(BossAttack.Barrage);
			if (phase.useSlam) options.Add(BossAttack.Slam);
			if (phase.useSummon && AliveSummons < phase.summonCount * 2) options.Add(BossAttack.Summon);

			if (options.Count == 0) return BossAttack.Barrage;

			var choice = options[attackRotation % options.Count];
			attackRotation++;
			return choice;
		}

		void ExecuteAttack(BossAttack attack)
		{
			switch (attack)
			{
				case BossAttack.Barrage:
					FireBarrage();
					break;
				case BossAttack.Slam:
					DoSlam();
					break;
				case BossAttack.Summon:
					DoSummon();
					break;
			}
		}

		void FireBarrage()
		{
			var phase = CurrentPhase;
			int count = Mathf.Max(1, phase.barrageCount);
			float spread = phase.barrageSpread;

			Vector3 origin = transform.position + Vector3.up * 1.6f;
			Vector3 toTarget = ((Target != null ? Target.position : transform.position) + Vector3.up - origin);
			Vector3 forward = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
			// 压平到水平面，形成扇形弹幕。
			forward.y = 0f;
			if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
			forward.Normalize();

			for (int i = 0; i < count; i++)
			{
				float t = count == 1 ? 0f : (i / (float)(count - 1)) - 0.5f;
				Vector3 direction = Quaternion.AngleAxis(t * spread, Vector3.up) * forward;
				direction = Quaternion.Euler(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f)) * direction;
				EnemyProjectiles.Fire(definition, origin, direction, AttackDamage, gameObject, EnemyProjectileMode.Straight, 30f);
			}
		}

		void DoSlam()
		{
			var phase = CurrentPhase;
			float radius = Mathf.Max(1f, phase.slamRadius);
			Vector3 center = transform.position;

			if (slamEffect != null) Instantiate(slamEffect, center, Quaternion.identity);

			HitFeedback.EnsureInstance().TriggerShake(0.12f);

			if (Target != null)
			{
				float distance = Vector3.Distance(Target.position, center);
				if (distance <= radius)
				{
					float falloff = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(distance / radius));
					DealDamageToPlayer(AttackDamage * phase.slamDamageMultiplier * falloff, DamageType.Explosion);
				}
			}
		}

		void DoSummon()
		{
			var phase = CurrentPhase;
			int count = Mathf.Max(1, phase.summonCount);

			GameObject template = summonPrefab;
			if (template == null && definition != null) template = definition.prefab;

			if (template == null)
			{
				Debug.LogWarning(logTag + " 没有可用的召唤预制体，跳过召唤");
				return;
			}

			for (int i = 0; i < count; i++)
			{
				float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
				Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * summonRadius;
				Vector3 position = transform.position + offset;

				if (UnityEngine.AI.NavMesh.SamplePosition(position, out var hit, summonRadius, UnityEngine.AI.NavMesh.AllAreas))
					position = hit.position;

				var instance = Instantiate(template, position, Quaternion.identity);
				instance.name = template.name + "_Summoned";

				var agent = instance.GetComponent<EnemyAgent>();
				if (agent != null)
				{
					agent.SpawnWave = Agent != null ? Agent.SpawnWave : 0;
					summoned.Add(agent);
				}

				if (WaveDirectorEx.Instance != null)
					WaveDirectorEx.Instance.RegisterExternalSpawn(instance);
			}

			Debug.Log(logTag + " 召唤 " + count + " 个随从（" + template.name + "）");
		}

		void SpawnWindupEffect()
		{
			if (windupEffect == null) return;
			ClearWindupEffect();
			activeWindupEffect = Instantiate(windupEffect, transform.position + Vector3.up, Quaternion.identity, transform);
		}

		void ClearWindupEffect()
		{
			if (activeWindupEffect != null) Destroy(activeWindupEffect);
			activeWindupEffect = null;
		}

		protected override void OnDeath()
		{
			ClearWindupEffect();
			Debug.Log(logTag + " Boss 被击败，阶段 " + (phaseIndex + 1));
		}

		protected override void OnDrawGizmosSelected()
		{
			base.OnDrawGizmosSelected();
			Gizmos.color = new Color(1f, 0.15f, 0.6f, 0.5f);
			Gizmos.DrawWireSphere(transform.position, engageRange);
		}
	}
}