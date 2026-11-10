using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// Mech（重型机甲）AI：巡逻 → 索敌 → 减速追击 → 机枪扫射（射弹）→ 近距离冲撞。
	/// <para>
	/// 数值来自 <see cref="EnemyDefinition"/>（kind = Mech），默认血量高、移速中等、护甲 25%。
	/// 追击时先拉近到「扫射距离」，到达后边走边打；进入近身距离立刻进入 Attack 状态做冲撞。
	/// </para>
	/// </summary>
	public sealed class MechBehaviour : EnemyBehaviour
	{
		[Header("Mech - Ranged")]
		[Tooltip("开始扫射的距离（米）。进入后转为「绕圈站桩射击」。")]
		public float fireRange = 14f;

		[Tooltip("每次扫射连发的弹丸数。")]
		public int burstCount = 3;

		[Tooltip("连发之间的间隔（秒）。")]
		public float burstInterval = 0.16f;

		[Tooltip("弹丸散布（度）。")]
		public float bulletSpread = 2.5f;

		[Tooltip("枪口位置（可选）。为空时从 1.4m 高度发射。")]
		public Transform muzzle;

		[Header("Mech - Charge")]
		[Tooltip("冲撞准备时间（秒）。")]
		public float chargeWindup = 0.5f;

		[Tooltip("冲撞速度倍率。")]
		public float chargeSpeedMultiplier = 2.6f;

		[Tooltip("冲撞持续时间（秒）。")]
		public float chargeDuration = 0.7f;

		[Tooltip("冲撞伤害倍率（相对 AttackDamage）。")]
		public float chargeDamageMultiplier = 2.2f;

		[Header("Mech - Patrol")]
		[Tooltip("巡逻半径（米）。")]
		public float patrolRadius = 8f;

		[Tooltip("巡逻点停留时间（秒）。")]
		public float patrolPause = 2f;

		float phaseTimer;
		int shotsFired;
		Vector3 patrolHome;
		Vector3 patrolPoint;
		bool chargeHitDone;

		void Start()
		{
			patrolHome = transform.position;
			PickPatrolPoint();
		}

		protected override void OnEnterState(EnemyState state)
		{
			phaseTimer = 0f;

			switch (state)
			{
				case EnemyState.Idle:
				case EnemyState.Patrol:
					StopMoving();
					PickPatrolPoint();
					break;

				case EnemyState.Chase:
					break;

				case EnemyState.Attack:
					BeginAttackCooldown();
					shotsFired = 0;
					chargeHitDone = false;
					break;

				case EnemyState.Stagger:
					StopMoving();
					break;

				case EnemyState.Dead:
					StopMoving();
					break;
			}
		}

		protected override void TickState(EnemyState state)
		{
			switch (state)
			{
				case EnemyState.Idle:
				case EnemyState.Patrol:
					TickPatrol();
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

		void TickPatrol()
		{
			if (!HasValidTarget())
			{
				if (State == EnemyState.Idle && StateElapsed > 1f) SetState(EnemyState.Patrol);
				return;
			}

			Vector3 flat = patrolPoint - transform.position;
			flat.y = 0f;

			if (flat.sqrMagnitude < 0.6f)
			{
				phaseTimer += Time.deltaTime;
				StopMoving();
				if (phaseTimer >= patrolPause)
				{
					phaseTimer = 0f;
					PickPatrolPoint();
				}
				return;
			}

			MoveTowards(patrolPoint, 0.45f);
			FaceDirection(flat);
		}

		void PickPatrolPoint()
		{
			// 在 NavMesh 上取一个随机可达点；没有 NavMesh 时退化为圆形随机点。
			Vector2 random = Random.insideUnitCircle * patrolRadius;
			Vector3 candidate = patrolHome + new Vector3(random.x, 0f, random.y);

			if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, patrolRadius, UnityEngine.AI.NavMesh.AllAreas))
				candidate = hit.position;

			patrolPoint = candidate;
		}

		void TickChase()
		{
			if (!HasValidTarget())
			{
				SetState(EnemyState.Idle);
				return;
			}

			float distance = TargetDistance();

			// 近身：直接进入攻击（冲撞）。
			if (distance <= MeleeRange)
			{
				SetState(EnemyState.Attack);
				return;
			}

			// 中距离：进入攻击（扫射），但保持移动。
			if (RangedRange > 0f && distance <= fireRange && CanAttack())
			{
				SetState(EnemyState.Attack);
				return;
			}

			// 距离太近但还不能攻击时，保持站位而不是一直贴脸。
			if (distance < fireRange * 0.55f && RangedRange > 0f)
			{
				Vector3 away = transform.position + (transform.position - Target.position).normalized * 4f;
				MoveTowards(away, 0.5f);
			}
			else
			{
				MoveTowards(Target.position, 1f);
			}

			FaceTarget();
		}

		void TickAttack()
		{
			if (!HasValidTarget())
			{
				SetState(EnemyState.Idle);
				return;
			}

			float distance = TargetDistance();
			FaceTarget();

			// 并入近身：冲撞
			if (distance <= MeleeRange)
			{
				TickCharge(distance);
				return;
			}

			// 超出射程太远：回到追击
			if (distance > fireRange * 1.4f)
			{
				SetState(EnemyState.Chase);
				return;
			}

			TickBurst(distance);
		}

		void TickBurst(float distance)
		{
			phaseTimer += Time.deltaTime;

			// 边走边扫射：保持与玩家的水平距离。
			if (distance > fireRange * 0.75f) MoveTowards(Target.position, 0.55f);
			else if (distance < fireRange * 0.4f) MoveTowards(transform.position + (transform.position - Target.position).normalized * 3f, 0.4f);
			else StopMoving();

			float nextShot = shotsFired * burstInterval;
			if (shotsFired < Mathf.Max(1, burstCount) && phaseTimer >= nextShot)
			{
				FireBullet(distance);
				shotsFired++;
			}

			if (shotsFired >= Mathf.Max(1, burstCount) && phaseTimer >= shotsFired * burstInterval + 0.15f)
				SetState(EnemyState.Chase);
		}

		void FireBullet(float distance)
		{
			Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.4f;
			Vector3 aim = Target.position + Vector3.up * 1.1f;
			Vector3 direction = (aim - origin).normalized;
			direction = Quaternion.Euler(
				Random.Range(-bulletSpread, bulletSpread),
				Random.Range(-bulletSpread, bulletSpread),
				0f) * direction;

			EnemyProjectiles.Fire(
				definition,
				origin,
				direction,
				AttackDamage,
				gameObject,
				EnemyProjectileMode.Straight,
				distance);
		}

		void TickCharge(float distance)
		{
			phaseTimer += Time.deltaTime;

			if (phaseTimer < chargeWindup)
			{
				StopMoving();
				return;
			}

			if (phaseTimer < chargeWindup + chargeDuration)
			{
				// 冲撞阶段：高速贴近，命中一次伤害。
				Vector3 destination = Target.position;
				MoveTowards(destination, chargeSpeedMultiplier);

				if (!chargeHitDone && distance <= MeleeRange * 1.3f)
				{
					chargeHitDone = true;
					DealDamageToPlayer(AttackDamage * chargeDamageMultiplier, DamageType.Melee);
				}
				return;
			}

			StopMoving();
			BeginAttackCooldown();
			SetState(EnemyState.Chase);
		}

		void FaceDirection(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return;
			Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * Time.deltaTime);
		}

		protected override void OnDamaged(float amount, bool lethal)
		{
			// 重装单位受击硬直短，且被击中会立刻警觉（跳过巡逻）。
			if (!lethal && State == EnemyState.Idle && HasValidTarget() && StateElapsed > 0.5f)
				SetState(EnemyState.Chase);
		}
	}
}