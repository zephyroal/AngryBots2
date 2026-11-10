using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// 敌人弹丸发射器（静态工具）：把 <see cref="EnemyDefinition"/> 的弹丸参数翻译成实际飞行物。
	/// <para>
	/// 走 <see cref="PoolManager"/> 复用；弹丸对象挂 <see cref="EnemyProjectile"/>，
	/// 命中玩家 / 环境后自毁。若定义里配置了 <c>projectilePrefab</c>，会用它作为外观模板并
	/// 额外补挂 <see cref="EnemyProjectile"/>（模板本身不需要预挂脚本）。
	/// </para>
	/// </summary>
	public static class EnemyProjectiles
	{
		/// <summary>发射一发弹丸。</summary>
		/// <param name="definition">敌人定义（可为 null，取默认值）。</param>
		/// <param name="origin">发射点。</param>
		/// <param name="direction">飞行方向（会自动归一化）。</param>
		/// <param name="damage">伤害。</param>
		/// <param name="source">发射者（用于击杀归属与体积忽略）。</param>
		/// <param name="mode">模式（直线 / 追踪 / 抛射）。</param>
		/// <param name="straightDistanceHint">抛射模式用的距离提示。</param>
		public static GameObject Fire(
			EnemyDefinition definition,
			Vector3 origin,
			Vector3 direction,
			float damage,
			GameObject source,
			EnemyProjectileMode mode = EnemyProjectileMode.Straight,
			float straightDistanceHint = 12f)
		{
			if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
			direction.Normalize();

			float speed = definition != null ? Mathf.Max(1f, definition.projectileSpeed) : 18f;
			GameObject template = definition != null ? definition.projectilePrefab : null;

			GameObject projectile;
			if (template != null)
			{
				projectile = PoolManager.Spawn(template, origin, Quaternion.LookRotation(direction, Vector3.up));
			}
			else
			{
				projectile = PoolManager.Spawn(GetProceduralTemplate(), origin, Quaternion.LookRotation(direction, Vector3.up));
			}

			if (projectile == null) return null;

			var behaviour = projectile.GetComponent<EnemyProjectile>();
			if (behaviour == null) behaviour = projectile.AddComponent<EnemyProjectile>();

			behaviour.Launch(origin, direction, speed, damage, source, mode, straightDistanceHint);
			return projectile;
		}

		// ─── 程序化模板 ───

		static GameObject proceduralTemplate;

		/// <summary>取（必要时创建）程序化弹丸模板：一个无渲染依赖的发光小球 + 触发器。</summary>
		public static GameObject GetProceduralTemplate()
		{
			if (proceduralTemplate != null) return proceduralTemplate;

			proceduralTemplate = new GameObject("[EnemyProjectile]");
			proceduralTemplate.SetActive(false);

			var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			sphere.name = "Body";
			sphere.transform.SetParent(proceduralTemplate.transform, false);
			sphere.transform.localScale = Vector3.one * 0.28f;

			var collider = sphere.GetComponent<SphereCollider>();
			if (collider != null) collider.radius = 1.1f;

			var renderer = sphere.GetComponent<Renderer>();
			if (renderer != null)
			{
				var shader = Shader.Find("Universal Render Pipeline/Unlit");
				if (shader == null) shader = Shader.Find("Unlit/Color");
				if (shader != null)
				{
					var material = new Material(shader);
					material.name = "EnemyProjectile_Material";
					var color = new Color(1f, 0.35f, 0.15f);
					if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
					material.color = color;
					renderer.sharedMaterial = material;
				}
			}

			// 模板本身不能带 Rigidbody：弹丸用 transform 移动，不用物理。
			proceduralTemplate.AddComponent<EnemyProjectile>();
			return proceduralTemplate;
		}
	}

	/// <summary>
	/// 敌方弹丸：由 <see cref="EnemyProjectiles"/> 发射，命中玩家造成伤害。
	/// 用 transform 位移 + OverlapSphere 手动检测，避免物理层配置依赖。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class EnemyProjectile : MonoBehaviour, PoolManager.IPoolable
	{
		[Header("Tuning")]
		[Tooltip("瞄准追踪的转向速度（度/秒），仅 Homing 模式生效。")]
		public float homingTurnSpeed = 90f;

		[Tooltip("生命上限（秒）。")]
		public float maxLife = 6f;

		[Tooltip("命中半径（米）。")]
		public float hitRadius = 0.35f;

		Vector3 velocity;
		bool active;
		float life;
		float damage;
		GameObject source;
		EnemyProjectileMode mode;
		Transform target;

		void Awake()
		{
			// 自带一个触发器，避免模板忘了配碰撞体。
			if (GetComponent<Collider>() == null)
			{
				var sphere = gameObject.AddComponent<SphereCollider>();
				sphere.isTrigger = true;
				sphere.radius = hitRadius;
			}
		}

		/// <summary>发射。</summary>
		public void Launch(Vector3 origin, Vector3 direction, float speed, float damageAmount, GameObject sourceObject, EnemyProjectileMode projectileMode, float distanceHint)
		{
			transform.position = origin;
			transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

			velocity = direction.normalized * Mathf.Max(0.1f, speed);
			damage = Mathf.Max(0.1f, damageAmount);
			source = sourceObject;
			mode = projectileMode;
			life = 0f;
			active = true;
			target = PlayerSource.Player;

			if (mode == EnemyProjectileMode.Homing) maxLife = Mathf.Min(6f, Mathf.Max(1.5f, distanceHint / Mathf.Max(0.5f, speed) + 1f));
		}

		void Update()
		{
			if (!active) return;

			life += Time.deltaTime;
			if (life > maxLife)
			{
				Despawn();
				return;
			}

			if (mode == EnemyProjectileMode.Homing && target != null)
			{
				Vector3 desired = (target.position + Vector3.up - transform.position).normalized;
				velocity = Vector3.RotateTowards(velocity.normalized, desired, homingTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f)
					* velocity.magnitude;
				transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
			}

			Vector3 step = velocity * Time.deltaTime;
			transform.position += step;

			if (CheckHit(step.magnitude)) return;
		}

		bool CheckHit(float distance)
		{
			// 检测玩家：用玩家 Transform 的距离判定（不依赖 tag / 层）。
			var player = PlayerSource.Player;
			if (player != null)
			{
				Vector3 center = player.position + Vector3.up * 1.0f;
				if (Vector3.Distance(transform.position, center) <= hitRadius + 0.55f)
				{
					var health = player.GetComponent<PlayerHealth>();
					if (health != null && !health.IsDead)
					{
						var info = DamageInfo.Create(damage, source, transform.position, velocity.normalized);
						info.Type = DamageType.Energy;
						var adapter = health as PlayerHealthAdapter;
						if (adapter != null) adapter.ApplyDamage(info);
						else health.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(damage)));
					}
					Despawn();
					return true;
				}
			}

			// 环境：向前探一步，撞到非玩家碰撞体就算命中墙体。
			if (Physics.Raycast(transform.position, velocity.normalized, out var hit, distance + 0.1f, ~0, QueryTriggerInteraction.Ignore))
			{
				if (hit.collider.GetComponentInParent<PlayerHealth>() == null &&
					hit.collider.GetComponentInParent<EnemyAgent>() == null)
				{
					transform.position = hit.point;
					Despawn();
					return true;
				}
			}

			if (mode == EnemyProjectileMode.Lobbed && life > 0.05f && Physics.Raycast(transform.position, Vector3.down, 0.2f))
			{
				Despawn();
				return true;
			}

			return false;
		}

		/// <summary>归还池。</summary>
		public void Despawn()
		{
			active = false;
			PoolManager.Despawn(gameObject);
		}

		public void OnSpawned()
		{
			active = false;
			life = 0f;
		}

		public void OnDespawned()
		{
			active = false;
		}
	}
}