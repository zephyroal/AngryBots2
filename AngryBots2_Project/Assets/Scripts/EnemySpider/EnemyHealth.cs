using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHealth : MonoBehaviour {

	/// <summary>敌人死亡时触发，参数为死亡位置。HUD 用来做击杀标记。</summary>
	public static event System.Action<Vector3> EnemyDied;

	public bool IsDead { get { return currentHealth <= 0; } }

	[Header("Health Settings")]
	public int totalHealth = 100;
	private int currentHealth;

	[Header("Points Settings")]
	public int pointToGive = 0;
	private ScoreManager scoreManager;

	[Header("Explosion Effect")]
	public GameObject explosionPrefab;


	void Start()
	{
		currentHealth = totalHealth;

		if(pointToGive != 0)
		{
			var scoreCanvas = GameObject.Find("ScoreCanvas");
			if(scoreCanvas != null) scoreManager = scoreCanvas.GetComponent<ScoreManager>();
		}
	}

	public void RemoveHealth(int damageTaken)
	{
		currentHealth -= damageTaken;

		if(currentHealth <= 0)
		{
			RemoveEnemy();
		}
	}

	void RemoveEnemy()
	{
		if(explosionPrefab)
		{
			Instantiate(explosionPrefab, transform.position, transform.rotation);
		}

		if(scoreManager)
		{
			scoreManager.AddToScore(pointToGive);
		}

		if (EnemyDied != null) EnemyDied(transform.position);

		Destroy(gameObject);
	}


}
