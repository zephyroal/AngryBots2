using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// Kamikaze（自爆蜂）AI：无重力悬浮 → 冲刺接近玩家 → 进入引爆半径后自爆（范围伤害）；
	/// 也可在飞行途中被击杀，此时提前引爆并造成减半的范围伤害。
	/// <para>
	/// 设计：血量很低、移速很快，靠数量压场；玩家必须在中距离击杀它们，
	/// 否则它们会贴脸自爆（有 0.35s 的引爆前摇，给了最后反应窗口）。
	/// </para>
	/// </summary>
	public sealed class KamikazeBehaviour : EnemyBehaviour
	{
		[Header("Kamikaze - Movement")]
		[Tooltip("悬停高度（相对玩家脚底），冲刺时会逐步降低到爆点高度。")]
		public float hoverHeight = 1.6f;

		[Tooltip("发现玩家前的游荡半径。")]
		public float unarmedWanderRadius = 5f;

		[Tooltip("冲刺加速度（越大越难躲）。")]
		public float dashAcceleration = 55f;

		[Tooltip("悬浮时的垂直纠偏强度。")]
		public float heightCorrection = 6f;

		[Header("Kamikaze - Detonation")]
		[Tooltip("进入该距离后开始引爆倒计时。")]
		public float detonationRange = 2.6f;

		[Tooltip("引爆前摇（秒），给玩家最后的反应窗口。")]
		public float detonationFuse = 0.35f;

		[Tooltip("爆炸半径（米）。")]
		public float explosionRadius = 4f;

		[Tooltip("爆炸伤害（对玩家）。")]
		public float explosionDamage = 32f;

		[Tooltip("被击杀时提前引爆的伤害倍率。")]
		[Range(0f, 1f)]
		public float killedDetonationMultiplier = 0.5f;

		[Tooltip("爆炸特效（可选）。")]
		public GameObject explosionEffect;

		[Tooltip("爆炸音效（可选）。")]
		public AudioClip explosionClip;

		[Header("Kamikaze - Visual")]
		[Tooltip("引爆前摇期间的发光颜色（对带 Renderer 的对象做快速闪烁）。")]
		public Color flashColor = new Color(1f, 0.3f, 0.1f);

		bool detonating;
		bool detonated;
		float fuseTimer;
		Vector3 wanderCenter;
		float wanderPhase;
		Renderer[] cachedRenderers;
		Color[] originalColors;

		/// <summary>Kamikaze 是飞行单位。</summary>
		public override bool IsFlying { get { return true; } }

		/// <summary>是否正在引爆倒计时。</summary>
		public bool Detonating { get { return detonating; } }

		/// <summary>是否已经爆炸。</summary>
		public bool Detonated { get { return detonated; } }

		protected override void InitializeOnce()
		{
			base.InitializeOnce();

			if (NavAgent != null)
			{
				NavAgent.enabled = false;
				HasNavAgent = false;
			}

			if (Body != null)
			{
				Body.useGravity = false;
				Body.isKinematic = false;
				Body.drag = 0.5f;
				Body.angularDrag = 6f;
			}

			wanderCenter = transform.position;
			wanderPhase = Random.Range(0f, Mathf.PI * 2f);

			CacheRenderers();
		}

		void CacheRenderers()
		{
			cachedRenderers = GetComponentsInChildren<Renderer>(true);
			originalColors = new Color[cachedRenderers.Length];
			for (int i = 0; i < cachedRenderers.Length; i++)
			{
				var material = cachedRenderers[i] != null ? cachedRenderers[i].sharedMaterial : null;
				if (material != null)
				{
					if (material.HasProperty("_BaseColor")) originalColors[i] = material.GetColor("_BaseColor");
					else if (material.HasProperty("_Color")) originalColors[i] = material.color;
				}
			}
		}

		protected override void OnEnterState(EnemyState state)
		{
			switch (state)
			{
				case EnemyState.Dead:
					if (Body != null)
					{
						Body.isKinematic = false;
						Body.useGravity = true;
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
					TickWander();
					if (HasValidTarget()) SetState(EnemyState.Chase);
					break;

				case EnemyState.Chase:
					TickDash();
					break;

				case EnemyState.Attack:
					TickFuse();
					break;

				case EnemyState.Stagger:
					// 自爆蜂不做硬直：受击时仍然保持冲刺势头。
					SetState(HasValidTarget() ? EnemyState.Chase : EnemyState.Idle);
					break;
			}
		}

		void TickWander()
		{
			if (Body == null) return;

			wanderPhase += Time.deltaTime * 0.9f;
			Vector3 ring = wanderCenter + new Vector3(Mathf.Cos(wanderPhase), 0f, Mathf.Sin(wanderPhase)) * unarmedWanderRadius;
			ring.y = wanderCenter.y + hoverHeight + Mathf.Sin(wanderPhase * 2f) * 0.4f;

			Vector3 desired = Vector3.ClampMagnitude((ring - transform.position) * 2f, MoveSpeed * 0.4f);
			desired.y += 1.5f;
			Body.velocity = Vector3.MoveTowards(Body.velocity, desired, 15f * Time.deltaTime);
			FaceTowards(ring - transform.position);
		}

		void TickDash()
		{
			if (!HasValidTarget() || Body == null)
			{
				SetState(EnemyState.Idle);
				return;
			}

			float distance = TargetDistance();
			if (distance <= detonationRange)
			{
				StartDetonation();
				return;
			}

			Vector3 aimPoint = Target.position + Vector3.up * hoverHeight;
			Vector3 direction = (aimPoint - transform.position).normalized;

			Vector3 desiredVelocity = direction * MoveSpeed;
			Body.velocity = Vector3.MoveTowards(Body.velocity, desiredVelocity, dashAcceleration * Time.deltaTime);
			FaceTowards(direction);

			// 起爆倒计时期间被推远（玩家冲刺跑开）则回到冲刺。
			if (detonating && distance > detonationRange * 1.8f) detonating = false;
		}

		/// <summary>开始引爆倒计时。</summary>
		public void StartDetonation()
		{
			if (detonating || detonated) return;
			detonating = true;
			fuseTimer = 0f;
			SetState(EnemyState.Attack);
		}

		void TickFuse()
		{
			fuseTimer += Time.deltaTime;

			if (Body != null) Body.velocity = Vector3.MoveTowards(Body.velocity, Vector3.zero, 28f * Time.deltaTime);
			if (HasValidTarget()) FaceTowards(Target.position - transform.position);

			FlashFuse(fuseTimer / Mathf.Max(0.01f, detonationFuse));

			if (fuseTimer >= detonationFuse)
				Explode(1f);
		}

		void FlashFuse(float t)
		{
			if (cachedRenderers == null || cachedRenderers.Length == 0) return;

			bool on = Mathf.FloorToInt(t * 10f) % 2 == 0;
			for (int i = 0; i < cachedRenderers.Length; i++)
			{
				var renderer = cachedRenderers[i];
				if (renderer == null) continue;
				var material = renderer.sharedMaterial;
				if (material == null) continue;

				if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", on ? flashColor : originalColors[i]);
				else if (material.HasProperty("_Color")) material.color = on ? flashColor : originalColors[i];
			}
		}

		/// <summary>爆炸：范围伤害 + 特效，并以「自毁」方式结束（不重复计击杀分）。</summary>
		public void Explode(float damageMultiplier)
		{
			if (detonated) return;
			detonated = true;
			detonating = false;

			Vector3 center = transform.position;

			var player = PlayerSource.Player;
			if (player != null)
			{
				float distance = Vector3.Distance(player.position + Vector3.up, center);
				if (distance <= explosionRadius)
				{
					float falloff = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(distance / Mathf.Max(0.01f, explosionRadius)));
					DealDamageToPlayer(explosionDamage * falloff * damageMultiplier, DamageType.Explosion);
				}
			}

			if (explosionEffect != null)
				VfxPool.Spawn(explosionEffect, center, Quaternion.identity);
			if (explosionClip != null)
				AudioOneShot.Play(explosionClip, center);
			CameraShake.Shake(0.5f, 0.28f);

			// 自爆算自毁：不给击杀分（击杀分只在对玩家击杀时给）。
			SetState(EnemyState.Dead);
			StopMoving();
			foreach (var collider in GetComponentsInChildren<Collider>(true))
				collider.enabled = false;
			Destroy(gameObject, 0.05f);
		}

		void FaceTowards(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return;
			Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
			transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * 1.5f * Time.deltaTime);
		}

		protected override void OnDamaged(float amount, bool lethal)
		{
			// 被击杀 → 提前引爆（减半伤害）。
			if (lethal && !detonated)
				Explode(killedDetonationMultiplier);
		}

		protected override void OnDrawGizmosSelected()
		{
			base.OnDrawGizmosSelected();
			Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.35f);
			Gizmos.DrawWireSphere(transform.position, explosionRadius);
			Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
			Gizmos.DrawWireSphere(transform.position, detonationRange);
		}
	}
}