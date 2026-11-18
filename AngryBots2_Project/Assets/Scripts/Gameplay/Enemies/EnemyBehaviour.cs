using UnityEngine;
using UnityEngine.AI;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// 敌人 AI 基类：统一状态机（Idle / Patrol / Chase / Attack / Stagger / Dead）+
	/// 目标感知 + 硬直 + 血量接入 + 死亡处理，供 Mech / Buzzer / Kamikaze / Boss 继承。
	/// <para>
	/// 设计要点：
	/// <list type="bullet">
	/// <item>血量统一走 <see cref="EnemyAgent"/>（护甲 / 飘字 / 击杀归属 / 注册表）；</item>
	/// <item>地面单位优先用 <see cref="NavMeshAgent"/>，没有可用 NavMesh 时自动回退到 Rigidbody 移动，
	///       保证在没有烘焙 NavMesh 的场景（如 batchmode 自检）里也能跑；</item>
	/// <item>子类只需覆写 <see cref="OnEnterState"/> / <see cref="TickState"/> / <see cref="OnDeath"/>。</item>
	/// </list>
	/// </para>
	/// </summary>
	[RequireComponent(typeof(EnemyAgent))]
	public abstract class EnemyBehaviour : EnemyHealthAdapter
	{
		[Header("Definition")]
		[Tooltip("敌人定义（可选）。为空时使用下面的内联数值。")]
		public EnemyDefinition definition;

		[Header("Movement Fallback")]
		[Tooltip("没有可用的 NavMeshAgent 时，用 Rigidbody 自己走。")]
		public bool allowRigidbodyFallback = true;

		[Tooltip("Rigidbody 回退模式下的加速度。")]
		public float fallbackAcceleration = 40f;

		[Header("Perception")]
		[Tooltip("索敌半径（米）。0 表示用定义里的值。")]
		public float detectionRadiusOverride;

		[Tooltip("失去目标半径（米）。0 表示用定义里的值。")]
		public float loseTargetRadiusOverride;

		[Header("Debug")]
		[Tooltip("在 Scene 视图里画出状态与感知范围。")]
		public bool drawGizmos = true;

		// ───── 状态 ─────

		/// <summary>当前状态。</summary>
		public EnemyState State { get; private set; } = EnemyState.Idle;

		/// <summary>上一次进入的状态。</summary>
		public EnemyState PreviousState { get; private set; } = EnemyState.Idle;

		/// <summary>当前状态已经持续的时长（秒）。</summary>
		public float StateElapsed { get; private set; }

		/// <summary>当前目标（玩家）。</summary>
		public Transform Target { get; protected set; }

		/// <summary>已注册的敌人元数据。</summary>
		public EnemyAgent Agent { get; private set; }

		/// <summary>NavMeshAgent（可能为 null）。</summary>
		public NavMeshAgent NavAgent { get; protected set; }

		/// <summary>Rigidbody（可能为 null）。</summary>
		public Rigidbody Body { get; protected set; }

		/// <summary>NavMeshAgent 是否可用（有代理且站在 NavMesh 上）。</summary>
		public bool HasNavAgent { get; protected set; }

		/// <summary>难度缩放倍率（血量 / 伤害），由刷怪系统写入。</summary>
		public float DifficultyScale { get; protected set; } = 1f;

		/// <summary>本敌人所属波次。</summary>
		public int WaveNumber { get; protected set; }

		// ───── 定义后门取值 ─────

		/// <summary>移动速度。</summary>
		public float MoveSpeed { get { return definition != null ? definition.moveSpeed : 4f; } }

		/// <summary>转向速度（度/秒）。</summary>
		public float TurnSpeed { get { return definition != null ? definition.turnSpeed : 480f; } }

		/// <summary>索敌半径。</summary>
		public float DetectionRadius
		{
			get { return detectionRadiusOverride > 0f ? detectionRadiusOverride : (definition != null ? definition.detectionRadius : 22f); }
		}

		/// <summary>脱离半径。</summary>
		public float LoseTargetRadius
		{
			get { return loseTargetRadiusOverride > 0f ? loseTargetRadiusOverride : (definition != null ? definition.loseTargetRadius : 32f); }
		}

		/// <summary>攻击伤害（已乘难度缩放）。</summary>
		public float AttackDamage
		{
			get { return (definition != null ? definition.attackDamage : 10f) * DifficultyScale; }
		}

		/// <summary>攻击间隔（秒）。</summary>
		public float AttackInterval { get { return definition != null ? definition.attackInterval : 1.3f; } }

		/// <summary>攻击前摇（秒）。</summary>
		public float AttackWindup { get { return definition != null ? definition.attackWindup : 0.25f; } }

		/// <summary>近战距离。</summary>
		public float MeleeRange { get { return definition != null ? definition.meleeRange : 2.2f; } }

		/// <summary>远程距离。</summary>
		public float RangedRange { get { return definition != null ? definition.rangedRange : 0f; } }

		// ───── 内部 ─────

		float nextAttackTime;
		float nextStaggerTime;
		bool ready;

		// EnemyHealthAdapter 契约 ───

		/// <summary>当前血量（走 EnemyAgent）。</summary>
		public override float CurrentHealth { get { return Agent != null ? Agent.CurrentHealth : 0f; } }

		/// <summary>最大血量（走 EnemyAgent）。</summary>
		public override float MaxHealth { get { return Agent != null ? Mathf.Max(1f, Agent.MaxHealth) : 1f; } }

		/// <summary>是否死亡。</summary>
		public override bool IsDead { get { return State == EnemyState.Dead; } }

		/// <summary>血量比例 0~1（Boss 阶段切换用）。</summary>
		public float HealthRatio { get { return Mathf.Clamp01(CurrentHealth / Mathf.Max(1f, MaxHealth)); } }

		// ───── 生命周期 ─────

		protected virtual void Awake()
		{
			Agent = GetComponent<EnemyAgent>();
			if (Agent == null) Agent = gameObject.AddComponent<EnemyAgent>();

			NavAgent = GetComponent<NavMeshAgent>();
			Body = GetComponent<Rigidbody>();
		}

		protected virtual void OnEnable()
		{
			// 对象池复用：恢复血量与状态。
			if (State == EnemyState.Dead || !ready) InitializeOnce();
			SetState(EnemyState.Idle);
		}

		/// <summary>初始化一次（血量、速度、代理参数）。</summary>
		protected virtual void InitializeOnce()
		{
			ready = true;

			// Edit 模式自检 / 编辑器内预览不会触发 Awake，这里补齐引用并登记注册表。
			if (Agent == null) Agent = GetComponent<EnemyAgent>();
			if (Agent == null) Agent = gameObject.AddComponent<EnemyAgent>();
			if (NavAgent == null) NavAgent = GetComponent<NavMeshAgent>();
			if (Body == null) Body = GetComponent<Rigidbody>();
			if (!EnemyRegistry.Contains(Agent)) EnemyRegistry.Register(Agent);

			Agent.ResolveHealth();
			Agent.EnsureStandaloneHealth();

			if (definition != null)
			{
				Agent.kind = definition.kind;
			}

			if (NavAgent != null)
			{
				NavAgent.speed = MoveSpeed;
				NavAgent.angularSpeed = TurnSpeed;
				NavAgent.stoppingDistance = 0.2f;
				NavAgent.updateRotation = true;
				HasNavAgent = NavAgent.enabled && NavAgent.isOnNavMesh;
			}

			if (Body != null && !HasNavAgent)
			{
				Body.isKinematic = false;
				Body.useGravity = !IsFlying;
			}
		}

		/// <summary>供刷怪系统写入的难度缩放与波次号。</summary>
		public void ApplySpawnParameters(int wave, float difficultyScale)
		{
			WaveNumber = wave;
			DifficultyScale = Mathf.Max(0.01f, difficultyScale);

			if (Agent != null)
			{
				Agent.SpawnWave = wave;
				Agent.statScale = DifficultyScale;
			}
		}

		/// <summary>是否飞行单位（由定义 / 子类决定）。</summary>
		public virtual bool IsFlying
		{
			get { return definition != null && definition.IsFlying; }
		}

		// ───── 主循环 ─────

		protected virtual void Update()
		{
			if (!ready) InitializeOnce();
			if (State == EnemyState.Dead) return;

			TickBehaviour();
		}

		/// <summary>
		/// 手动驱动一帧（Editor 自检 / 暂停时补帧用）。
		/// Edit 模式下 MonoBehaviour.Update 不会被调用，自检通过它逐帧推进同一套逻辑，
		/// 保证「自检跑的行为」与「运行时行为」完全一致。
		/// </summary>
		public void SimulateTick()
		{
			if (!ready) InitializeOnce();
			if (State == EnemyState.Dead) return;

			TickBehaviour();
		}

		/// <summary>状态机主推进（Update 与 SimulateTick 共用）。</summary>
		protected void TickBehaviour()
		{
			StateElapsed += Time.deltaTime;
			UpdateTarget();

			switch (State)
			{
				case EnemyState.Idle:
				case EnemyState.Patrol:
					if (HasValidTarget()) SetState(EnemyState.Chase);
					else TickState(State);
					break;

				case EnemyState.Chase:
					if (!HasValidTarget()) SetState(EnemyState.Idle);
					else TickState(State);
					break;

				case EnemyState.Attack:
				case EnemyState.Stagger:
					TickState(State);
					break;
			}
		}

		protected virtual void UpdateTarget()
		{
			var player = PlayerSource.Player;
			if (player == null)
			{
				Target = null;
				return;
			}

			float distance = Vector3.Distance(transform.position, player.position);

			if (Target == null)
			{
				if (distance <= DetectionRadius) Target = player;
				return;
			}

			if (distance > LoseTargetRadius) Target = null;
		}

		/// <summary>是否有有效目标。</summary>
		protected bool HasValidTarget()
		{
			if (Target == null) return false;

			var health = Target.GetComponent<PlayerHealth>();
			if (health != null && health.IsDead) return false;
			return true;
		}

		/// <summary>目标水平距离。</summary>
		protected float TargetDistance()
		{
			if (Target == null) return float.PositiveInfinity;
			return Vector3.Distance(transform.position, Target.position);
		}

		/// <summary>切换状态（会触发 OnExitState / OnEnterState）。</summary>
		public void SetState(EnemyState next)
		{
			if (State == next) return;

			OnExitState(State);
			PreviousState = State;
			State = next;
			StateElapsed = 0f;
			OnEnterState(next);
		}

		// ───── 子类钩子 ─────

		/// <summary>进入状态时调用。</summary>
		protected abstract void OnEnterState(EnemyState state);

		/// <summary>离开状态时调用（可选覆写）。</summary>
		protected virtual void OnExitState(EnemyState state) { }

		/// <summary>状态每帧调用。</summary>
		protected abstract void TickState(EnemyState state);

		/// <summary>死亡时调用（在 EnemyAgent.Die 之后）。</summary>
		protected virtual void OnDeath() { }

		/// <summary>受击时调用（可在子类里做受击反馈 / 硬直）。</summary>
		protected virtual void OnDamaged(float amount, bool lethal) { }

		// ───── 工具方法 ─────

		/// <summary>是否可以攻击（冷却完毕）。</summary>
		protected bool CanAttack()
		{
			return Time.time >= nextAttackTime;
		}

		/// <summary>标记攻击冷却（用定义里的间隔）。</summary>
		protected void BeginAttackCooldown(float intervalOverride = -1f)
		{
			float interval = intervalOverride > 0f ? intervalOverride : AttackInterval;
			nextAttackTime = Time.time + Mathf.Max(0.05f, interval);
		}

		/// <summary>进入硬直（有冷却，避免被连射锁死）。</summary>
		public void TryStagger(float duration)
		{
			if (definition != null && definition.staggerDuration <= 0f) return;
			if (Time.time < nextStaggerTime) return;

			float stagger = duration > 0f ? duration : (definition != null ? definition.staggerDuration : 0.18f);
			if (stagger <= 0f) return;

			nextStaggerTime = Time.time + (definition != null ? definition.staggerCooldown : 0.6f);
			SetState(EnemyState.Stagger);
			StaggerUntil = Time.time + stagger;

			if (NavAgent != null && HasNavAgent) NavAgent.isStopped = true;
		}

		/// <summary>硬直结束时间。</summary>
		protected float StaggerUntil { get; private set; }

		/// <summary>硬直是否结束。</summary>
		protected bool StaggerFinished { get { return Time.time >= StaggerUntil; } }

		/// <summary>面向目标（水平）。</summary>
		protected void FaceTarget(float degreesPerSecond = -1f)
		{
			if (Target == null) return;

			Vector3 direction = Target.position - transform.position;
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return;

			float speed = degreesPerSecond > 0f ? degreesPerSecond : TurnSpeed;
			Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, look, speed * Time.deltaTime);
		}

		/// <summary>朝目标移动（NavMesh 优先，Rigidbody 回退）。</summary>
		protected void MoveTowards(Vector3 destination, float speedScale = 1f)
		{
			float speed = MoveSpeed * Mathf.Max(0.01f, speedScale);

			if (NavAgent != null && NavAgent.enabled)
			{
				if (!NavAgent.isOnNavMesh)
				{
					HasNavAgent = false;
					return;
				}

				HasNavAgent = true;
				NavAgent.isStopped = false;
				NavAgent.speed = speed;
				NavAgent.SetDestination(destination);
				return;
			}

			if (!allowRigidbodyFallback) return;

			Vector3 flat = destination - transform.position;
			flat.y = 0f;
			if (flat.sqrMagnitude < 0.01f) return;

			Vector3 desired = flat.normalized * speed;
			if (Body != null)
			{
				Vector3 velocity = Body.velocity;
				velocity.x = Mathf.MoveTowards(velocity.x, desired.x, fallbackAcceleration * Time.deltaTime);
				velocity.z = Mathf.MoveTowards(velocity.z, desired.z, fallbackAcceleration * Time.deltaTime);
				Body.velocity = velocity;
			}
			else
			{
				transform.position += desired * Time.deltaTime;
			}
		}

		/// <summary>停止移动。</summary>
		protected void StopMoving()
		{
			if (NavAgent != null && NavAgent.enabled && NavAgent.isOnNavMesh)
			{
				NavAgent.isStopped = true;
				NavAgent.ResetPath();
			}

			if (Body != null && !IsFlying)
			{
				Vector3 velocity = Body.velocity;
				velocity.x = 0f;
				velocity.z = 0f;
				Body.velocity = velocity;
			}
		}

		/// <summary>对玩家造成伤害（统一走 PlayerHealthAdapter 的伤害链路）。</summary>
		protected void DealDamageToPlayer(float amount, DamageType type = DamageType.Melee)
		{
			if (Target == null) return;

			var health = Target.GetComponent<PlayerHealth>();
			if (health == null || health.IsDead) return;

			var info = DamageInfo.Create(amount, gameObject, Target.position, (transform.position - Target.position).normalized);
			info.Type = type;
			info.Feedback = HitFeedbackLevel.Normal;

			var adapter = health as PlayerHealthAdapter;
			if (adapter != null) adapter.ApplyDamage(info);
			else health.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(amount)));
		}

		/// <summary>死亡处理：停移动、禁碰撞、触发子类回调、延迟销毁。</summary>
		protected void DieFromDamage()
		{
			if (State == EnemyState.Dead) return;

			SetState(EnemyState.Dead);
			StopMoving();

			if (NavAgent != null) NavAgent.enabled = false;
			if (Body != null)
			{
				Body.velocity = Vector3.zero;
				Body.isKinematic = true;
			}

			foreach (var collider in GetComponentsInChildren<Collider>(true))
				collider.enabled = false;

			OnDeath();

			if (definition != null && definition.deathEffectPrefab != null)
				VfxPool.Spawn(definition.deathEffectPrefab, transform.position, Quaternion.identity);

			Destroy(gameObject, destroyDelay);
		}

		[Tooltip("死亡后多久销毁（秒），留时间播死亡特效。")]
		public float destroyDelay = 0.3f;

		// ───── EnemyHealthAdapter 覆写 ─────

		/// <summary>统一伤害入口：扣血 → 受击回调 → 死亡。</summary>
		public override float ApplyDamage(DamageInfo info)
		{
			if (State == EnemyState.Dead || info.Amount <= 0f) return 0f;

			float before = CurrentHealth;
			float applied = Agent != null ? Agent.ApplyDamage(info) : 0f;
			bool lethal = CurrentHealth <= 0f;

			if (applied > 0f)
			{
				OnDamaged(applied, lethal);
				if (!lethal) TryStagger(0f);
			}

			if (lethal) DieFromDamage();
			return Mathf.Max(0f, before - CurrentHealth);
		}

		/// <summary>直接扣血（旧接口兼容）。</summary>
		public override void ApplyRawDamage(int amount)
		{
			if (State == EnemyState.Dead || amount <= 0) return;

			Agent.ConsumeHealth(amount);
			bool lethal = CurrentHealth <= 0f;
			OnDamaged(amount, lethal);
			if (lethal) DieFromDamage();
		}

		// ───── 调试 ─────

		protected virtual void OnDrawGizmosSelected()
		{
			if (!drawGizmos) return;

			Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
			Gizmos.DrawWireSphere(transform.position, DetectionRadius);

			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, MeleeRange);

			if (RangedRange > 0f)
			{
				Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
				Gizmos.DrawWireSphere(transform.position, RangedRange);
			}
		}
	}
}