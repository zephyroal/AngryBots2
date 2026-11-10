using UnityEngine;

/// <summary>
/// 手雷：抛掷后 1.6s 或撞到环境时爆炸，对半径内敌人造成伤害并播放爆炸特效。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GrenadeProjectile : MonoBehaviour
{
	[Header("Damage")]
	public int damage = 150;
	public float radius = 4f;

	[Header("Timing")]
	public float fuseTime = 1.6f;

	[Header("VFX")]
	public GameObject explosionPrefab;
	public AudioClip explosionClip;

	bool exploded;

	void Start()
	{
		Invoke("Explode", fuseTime);
	}

	void OnCollisionEnter(Collision collision)
	{
		// 直接命中敌人或环境立刻引爆，避免弹跳后延时过久。
		if (collision.collider.CompareTag("Enemy") || collision.collider.CompareTag("Environment"))
			Explode();
	}

	public void Explode()
	{
		if (exploded) return;
		exploded = true;

		if (explosionPrefab != null)
			Instantiate(explosionPrefab, transform.position, Quaternion.identity);
		if (explosionClip != null)
			AudioSource.PlayClipAtPoint(explosionClip, transform.position);

		foreach (var health in FindObjectsOfType<EnemyHealth>())
		{
			if (health == null || health.IsDead) continue;
			if (Vector3.Distance(health.transform.position, transform.position) > radius) continue;
			health.RemoveHealth(damage);
		}

		Destroy(gameObject);
	}
}