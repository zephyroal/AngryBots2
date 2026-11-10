using UnityEngine;
using UnityEngine.AI;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// Buzzer（飞行骚扰单位）AI：无重力悬浮 → 绕玩家盘旋 → 间歇发射慢速追踪弹。
	/// <para>
	/// 特点：血量低、移速快、不走 NavMesh（直接 Rigidbody 悬浮）。
	/// 盘旋使用「切向 + 径向」合成：距离太远时向内收，太近时向外推，同时沿切线绕圈，
	/// 形成有节奏的骚扰感。
	/// </para>
	/// </summary>
	public sealed class BuzzerBehaviour : EnemyBehaviour
	{
		[Header("Hover")]
		[Tooltip("悬停高度（相对玩家脚底）。")]
		public float hoverHeight = 2.4f;

		[Tooltip("高度修正强度。")]
		public float heightCorrection = 5f;

		[Tooltip("悬浮浮动幅度（米）。")]
		public float bobAmplitude = 0.35f;

		[Tooltip("悬浮浮动频率。")]
		public float bobFrequency = 1.6f;

		[Header("Orbit")]
		[Tooltip("理想盘旋半径（米）。")]
		public float orbitRadius = 8f;

		[Tooltip("盘旋角速度（度/秒）。")]
		public float orbitSpeed = 70f;

		[Tooltip("径向纠偏强度。")]
		public float radialCorrection = 2.4f;

		[Tooltip("每隔多久反向绕一次（秒）。0 表示不反向。")]
		public float orbitFlipInterval = 6f;

		[Header("Shooting")]
		[Tooltip("发射慢速弹的间隔（秒）。")]
		public float shootInterval = 2.1f;

		[Tooltip("发射前需要对准的夹角（度）。")]
		public float aimTolerance = 18f;

		[Tooltip("发射点相对自身的高度偏移。")]
		public float muzzleHeight = -0.15f;

		float orbitPhase;
		float orbitDirectionSign = 1f;
		float lastFlipTime;
		float nextShootTime;
		bool hoverReady;
		float bobPhase;

		/// <summary>Buzzer 是飞行单位。</summary>
		public override bool IsFlying { get { return true; } }

		protected override void InitializeOnce()
		{
			base.InitializeOnce();

			// 飞行单位：关掉 NavMeshAgent，使用无重力刚体。
			if (NavAgent != null)
			{
				NavAgent.enabled = false;
				HasNavAgent = false;
			}

			if (Body != null)
			{
				Body.useGravity = false;
				Body.isKinematic = false;
				Body.drag = 1.5f;
				Body.angularDrag = 4f;
			}

			orbitPhase = Random.Range(0f, Mathf.PI * 2f);
			bobPhase = Random.Range(0f, Mathf.PI * 2f);
			nextShootTime = Time.time + Random.Range(0.5f, 1.5f);
			lastFlipTime = Time.time;
			hoverReady = true;
		}

		protected override void OnEnterState(EnemyState state)
		{
			switch (state)
			{
				case EnemyState.Attack:
					BeginAttackCooldown();
					break;
				case EnemyState.Dead:
					if (Body != null)
					{
						Body.isKinematic = false;
						Body.useGravity = true;
						Body.mass = 0.4f;
					}
					break;
			}
		}

		protected override void TickState(EnemyState state)
		{
			switch (state)
			{
				case EnemyState.Idle:
				case EnemyState.Patrol:
					TickHover(Vector3.zero, false);
					if (HasValidTarget()) SetState(EnemyState.Chase);
					break;

				case EnemyState.Chase:
					TickOrbit();
					break;

				case EnemyState.Attack:
					TickShoot();
					break;

				case EnemyState.Stagger:
					TickHover(Vector3.zero, false);
					if (StaggerFinished) SetState(HasValidTarget() ? EnemyState.Chase : EnemyState.Idle);
					break;
			}
		}

		// ─── 悬停 ───

		void TickHover(Vector3 targetPosition, bool chase)
		{
			if (!hoverReady || Body == null) return;

			Vector3 desired = transform.position;
			if (chase) desired = targetPosition;
			desired.y = targetPosition.y + hoverHeight + Mathf.Sin(Time.time * bobFrequency + bobPhase) * bobAmplitude;

			Vector3 toDesired = desired - transform.position;
			Vector3 desiredVelocity = Vector3.ClampMagnitude(toDesired * heightCorrection, MoveSpeed * 0.5f);

			// 保持一点向上的反重力补偿，防止缓慢下沉。
			desiredVelocity.y += 1.5f;

			Body.velocity = Vector3.MoveTowards(Body.velocity, desiredVelocity, 20f * Time.deltaTime);
			FaceMovementOrTarget();
		}

		void FaceMovementOrTarget()
		{
			Vector3 direction;
			if (HasValidTarget())
				direction = Target.position - transform.position;
			else if (Body != null)
				direction = Body.velocity;
			else
				return;

			direction.y = 0f;
			if (direction.sqrMagnitude < 0.02f) return;

			Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * Time.deltaTime);
		}

		// ─── 盘旋 ───

		void TickOrbit()
		{
			if (!HasValidTarget() || Body == null)
			{
				SetState(EnemyState.Idle);
				return;
			}

			if (orbitFlipInterval > 0f && Time.time - lastFlipTime > orbitFlipInterval)
			{
				orbitDirectionSign *= -1f;
				lastFlipTime = Time.time;
			}

			orbitPhase += orbitSpeed * orbitDirectionSign * Mathf.Deg2Rad * Time.deltaTime;

			Vector3 playerPosition = Target.position;
			Vector3 center = playerPosition + Vector3.up * hoverHeight;

			// 以玩家为圆心的目标环点。
			Vector3 radial = new Vector3(Mathf.Cos(orbitPhase), 0f, Mathf.Sin(orbitPhase));
			Vector3 ringPoint = center + radial * orbitRadius;

			// 径向纠偏：距离过远/过近时加大修正。
			float actualRadius = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z),
				new Vector3(playerPosition.x, 0f, playerPosition.z));

			Vector3 tangent = Vector3.Cross(Vector3.up, radial) * orbitDirectionSign;
			float radialError = (actualRadius - orbitRadius) / Mathf.Max(1f, orbitRadius);

			Vector3 desiredVelocity = (tangent * MoveSpeed) + ((-radial) * radialError * radialCorrection * MoveSpeed);
			desiredVelocity.y = (ringPoint.y - transform.position.y) * heightCorrection;
			desiredVelocity.y += Mathf.Sin(Time.time * bobFrequency + bobPhase) * bobAmplitude * 2f;

			Body.velocity = Vector3.MoveTowards(Body.velocity, desiredVelocity, 24f * Time.deltaTime);
			FaceMovementOrTarget();

			// 发射窗口
			if (Time.time >= nextShootTime && RangedRange > 0f && TargetDistance() <= RangedRange)
			{
				Vector3 aimDirection = (Target.position + Vector3.up - transform.position).normalized;
				if (Vector3.Angle(transform.forward, aimDirection) <= aimTolerance)
					SetState(EnemyState.Attack);
			}
		}

		// ─── 射击 ───

		void TickShoot()
		{
			if (!HasValidTarget())
			{
				SetState(EnemyState.Idle);
				return;
			}

			// 射击期间保持悬停，不再绕大圈。
			Vector3 playerPosition = Target.position;
			Vector3 holdPoint = transform.position;
			holdPoint.y = playerPosition.y + hoverHeight;
			Vector3 toHold = holdPoint - transform.position;
			if (Body != null)
				Body.velocity = Vector3.MoveTowards(Body.velocity, toHold * 2f + Vector3.up * 1.5f, 20f * Time.deltaTime);

			FaceMovementOrTarget();

			if (StateElapsed < Mathf.Max(0.05f, AttackWindup)) return;

			Vector3 origin = transform.position + Vector3.up * muzzleHeight;
			Vector3 direction = (Target.position + Vector3.up * 1.1f - origin).normalized;

			int count = definition != null ? Mathf.Max(1, definition.projectileCount) : 1;
			for (int i = 0; i < count; i++)
			{
				Vector3 spreadDirection = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-3f, 3f), 0f) * direction;
				EnemyProjectiles.Fire(definition, origin, spreadDirection, AttackDamage, gameObject,
					EnemyProjectileMode.Homing, TargetDistance());
			}

			nextShootTime = Time.time + Mathf.Max(0.2f, shootInterval);
			BeginAttackCooldown(0.4f);
			SetState(EnemyState.Chase);
		}

		protected override void OnDeath()
		{
			// 死亡后让刚体自然坠落（已在 OnEnterState(Dead) 里恢复重力）。
			if (Body != null)
			{
				Body.velocity = Vector3.down * 2f + Random.insideUnitSphere;
			}
		}

		protected override void OnDrawGizmosSelected()
		{
			base.OnDrawGizmosSelected();
			Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.4f);
			var player = PlayerSource.Player;
			if (player != null)
				Gizmos.DrawWireSphere(player.position + Vector3.up * hoverHeight, orbitRadius);
		}
	}
}