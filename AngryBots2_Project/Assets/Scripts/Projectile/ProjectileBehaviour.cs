using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ProjectileBehaviour : MonoBehaviour {

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


	void Start()
	{
		projectileRigidbody = GetComponent<Rigidbody>();
		Invoke("RemoveProjectile", lifeTime);
	}
	
	void OnTriggerEnter(Collider theCollider)
	{	
		// 如果碰到敌人
		if(theCollider.CompareTag("Enemy"))
		{
			if(damageToEnemy > 0)
			{
				theCollider.GetComponent<EnemyHealth>().RemoveHealth(damageToEnemy);	
			}

			Instantiate(hitEnemyParticles, transform.position, transform.rotation);
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
				Instantiate(hitWallParticles, transform.position, transform.rotation);
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
			Instantiate(hitWallParticles, transform.position, Quaternion.LookRotation(reflectDirection));
		}
	}
	
	void Update()
	{
			Vector3 movement = transform.forward * speed * Time.deltaTime;
			projectileRigidbody.MovePosition(transform.position + movement);
	}

	void RemoveProjectile()
	{
		Destroy(gameObject);
	}

}