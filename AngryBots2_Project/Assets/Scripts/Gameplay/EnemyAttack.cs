using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// 蜘蛛近身攻击。挂在敌人预制体上：玩家进入攻击距离后按间隔造成伤害。
/// 有出招前摇，命中时才结算，避免贴身瞬间连扣。
/// </summary>
public class EnemyAttack : MonoBehaviour
{
	[Header("Attack Settings")]
	public int damage = 10;
	public float attackRange = 2.2f;
	public float attackInterval = 1.3f;
	public float attackWindup = 0.25f;

	[Header("References")]
	public Transform player;

	PlayerHealth playerHealth;
	Animator animator;
	EnemyHealth enemyHealth;
	bool hasAttackTrigger;
	float nextAttackTime;

	void Awake()
	{
		animator = GetComponentInChildren<Animator>();
		enemyHealth = GetComponent<EnemyHealth>();
		hasAttackTrigger = animator != null && animator.parameters.Any(p => p.nameHash == Animator.StringToHash("Attack"));
	}

	void Start()
	{
		if (player == null)
		{
			var playerObject = GameObject.FindGameObjectWithTag("Player");
			if (playerObject != null) player = playerObject.transform;
		}
		if (player != null && playerHealth == null) playerHealth = player.GetComponent<PlayerHealth>();
	}

	void Update()
	{
		if (player == null || playerHealth == null) return;
		if (enemyHealth != null && enemyHealth.IsDead) return;
		if (Time.time < nextAttackTime) return;

		Vector3 offset = player.position - transform.position;
		offset.y = 0f;
		if (offset.magnitude > attackRange) return;

		nextAttackTime = Time.time + attackInterval;
		if (hasAttackTrigger) animator.SetTrigger("Attack");
		StartCoroutine(StrikeAfterWindup());
	}

	IEnumerator StrikeAfterWindup()
	{
		yield return new WaitForSeconds(attackWindup);
		if (player == null || playerHealth == null) yield break;
		if (enemyHealth != null && enemyHealth.IsDead) yield break;

		// 前摇期间玩家可能已经拉开距离，超出攻击距离 1.5 倍则打空。
		Vector3 offset = player.position - transform.position;
		offset.y = 0f;
		if (offset.magnitude > attackRange * 1.5f) yield break;

		playerHealth.TakeDamage(damage);
	}
}