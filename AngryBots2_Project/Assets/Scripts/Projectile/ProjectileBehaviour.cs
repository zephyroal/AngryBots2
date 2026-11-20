using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ProjectileBehaviour : MonoBehaviour {

	/// <summary>命中敌人时触发，参数为命中点。HUD 用来做命中标记。</summary>
	public static event System.Action<Vector3> EnemyHit;

	[Header("Movement")]
	public float speed;
	private Rigidbody projectileRigidbody;

	[Header("Life Settings")]
	public float lifeTime;

	[Header("Damage")]
	public int damageToEnemy;

	[Header("Hit Object AVFX")]
	public GameObject hitEnemyParticles;
	public GameObject hitWallParticles;

	[Header("Bounce Settings")]
	public bool enableBouncing = false;
	public int maxBounces = 3;
	public float bounceForceMultiplier = 1.0f;
	private int bounceCount = 0;

	[Header("Pierce / Critical")]
	[Tooltip("命中敌人后还能继续穿透的次数（0 = 命中即消失）。")]
	public int pierceRemaining;

	[Tooltip("爆头倍率（1 = 关闭）。")]
	public float criticalMultiplier = 1f;

	[Tooltip("爆头判定：命中点高出敌人包围盒中心多少米算爆头。")]
	public float criticalHeight = 0.35f;

	readonly System.Collections.Generic.HashSet<int> hitEnemies = new System.Collections.Generic.HashSet<int>();

	/// <summary>爆头伤害计算（纯函数，便于自检）。</summary>
	public static int ApplyCritical(int damage, float hitY, float centerY, float heightThreshold, float multiplier)
	{
		if (damage <= 0) return 0;
		if (multiplier <= 1f) return damage;
		if (hitY - centerY < heightThreshold) return damage;
		return Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));
	}


	void Awake()
	{
		projectileRigidbody = GetComponent<Rigidbody>();
	}

	// 池化：每次从池里取出（SetActive(true)）都会走 OnEnable，这里重置状态并重新计时。
	void OnEnable()
	{
		bounceCount = 0;
		hitEnemies.Clear();
		if (projectileRigidbody != null)
		{
			projectileRigidbody.velocity = Vector3.zero;
			projectileRigidbody.angularVelocity = Vector3.zero;
		}
		if (lifeTime > 0f) Invoke("RemoveProjectile", lifeTime);
	}

	void OnDisable()
	{
		CancelInvoke();
	}

	void OnTriggerEnter(Collider theCollider)
	{	
		// 如果碰到敌人
		if(theCollider.CompareTag("Enemy"))
		{
			// 同一个敌人只结算一次（穿透时不会反复扣血）
			int enemyId = theCollider.gameObject.GetInstanceID();
			if (!hitEnemies.Add(enemyId)) return;

			int damage = ApplyCritical(damageToEnemy, transform.position.y, theCollider.bounds.center.y,
				criticalHeight, criticalMultiplier);

			if (damage > 0)
			{
				var enemyHealth = theCollider.GetComponent<EnemyHealth>();
				if (enemyHealth != null) enemyHealth.RemoveHealth(damage);
			}

			if (EnemyHit != null) EnemyHit(transform.position);

			VfxPool.Spawn(hitEnemyParticles, transform.position, transform.rotation);

			// 还有穿透次数就继续飞
			if (pierceRemaining > 0)
			{
				pierceRemaining--;
				return;
			}

			RemoveProjectile();
		}
		// 如果碰到环境物体并且启用了反弹
		else if (theCollider.CompareTag("Environment"))
		{
			if (enableBouncing && bounceCount < maxBounces)
			{
				BounceProjectile(theCollider);
			}
			else
			{
				VfxPool.Spawn(hitWallParticles, transform.position, transform.rotation);
				RemoveProjectile();
			}
		}

	}

	void BounceProjectile(Collider hitCollider)
	{
		// 增加反弹计数
		bounceCount++;

		// 计算反射方向
		Vector3 incomingDirection = transform.forward;
		Vector3 normal = Vector3.zero;

		// 尝试获取碰撞表面的法线
		ContactPoint[] contacts = new ContactPoint[1];
		if (hitCollider.Raycast(new Ray(transform.position, -incomingDirection), out RaycastHit hit, 1.0f))
		{
			normal = hit.normal;
		}
		else
		{
			// 如果无法获取精确法线，使用简单的反射计算
			normal = hitCollider.bounds.center - transform.position;
			normal = -Vector3.ProjectOnPlane(normal, incomingDirection).normalized;
		}

		// 计算反射方向
		Vector3 reflectDirection = Vector3.Reflect(incomingDirection, normal);

		// 应用新的方向和力
		transform.forward = reflectDirection;
		projectileRigidbody.velocity = reflectDirection * speed * bounceForceMultiplier;

		// 播放反弹粒子效果
		if (hitWallParticles != null)
		{
			VfxPool.Spawn(hitWallParticles, transform.position, Quaternion.LookRotation(reflectDirection));
		}
	}
	
	void Update()
	{
			Vector3 movement = transform.forward * speed * Time.deltaTime;
			projectileRigidbody.MovePosition(transform.position + movement);
	}

	void RemoveProjectile()
	{
		// 池化对象归还复用；非池化对象 PoolManager 会退化为 Destroy。
		PoolManager.Despawn(gameObject);
	}

}