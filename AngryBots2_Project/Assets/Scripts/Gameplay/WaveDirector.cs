using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 关卡任务：波次生存，每波限时清场。清光当前波后刷新更强的一波；
/// 超时或玩家阵亡则任务失败，打完最后一波则任务完成。
/// </summary>
public class WaveDirector : MonoBehaviour
{
	[Header("Spawning")]
	public GameObject enemyPrefab;
	public Transform[] spawnPoints;
	public int firstWaveSize = 3;
	public int extraEnemiesPerWave = 1;

	[Header("Mission")]
	public int wavesToWin = 5;
	public float waveTimeLimit = 90f;
	public float waveDelay = 3f;
	public int grenadeRefillPerWave = 1;
	public int healPerWave = 20;
	public float spawnProtectionTime = 1.5f;

	public int Wave { get; private set; }
	public int AliveEnemies { get; private set; }
	public float TimeLeft { get; private set; }
	public bool MissionOver { get; private set; }
	public bool Victory { get; private set; }
	public float NextWaveCountdown { get; private set; }

	public event Action WaveStarted;
	public event Action<bool> MissionEnded;

	PlayerHealth playerHealth;
	WeaponSystem weapons;

	void Start()
	{
		var player = GameObject.FindGameObjectWithTag("Player");
		if (player != null)
		{
			playerHealth = player.GetComponent<PlayerHealth>();
			weapons = player.GetComponent<WeaponSystem>();
			if (playerHealth != null) playerHealth.Died += OnPlayerDied;
		}

		Wave = 1;
		TimeLeft = waveTimeLimit;
		RefreshAliveCount();
		if (WaveStarted != null) WaveStarted();
	}

	void OnDestroy()
	{
		if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
	}

	void Update()
	{
		if (MissionOver) return;

		RefreshAliveCount();
		TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);

		if (TimeLeft <= 0f)
		{
			EndMission(false);
			return;
		}

		if (AliveEnemies > 0)
		{
			NextWaveCountdown = 0f;
			return;
		}

		if (Wave >= wavesToWin)
		{
			EndMission(true);
			return;
		}

		NextWaveCountdown += Time.deltaTime;
		if (NextWaveCountdown >= waveDelay) StartNextWave();
	}

	void RefreshAliveCount()
	{
		int alive = 0;
		foreach (var enemy in FindObjectsOfType<EnemyHealth>())
		{
			if (enemy != null && !enemy.IsDead) alive++;
		}
		AliveEnemies = alive;
	}

	void StartNextWave()
	{
		Wave++;
		TimeLeft = waveTimeLimit;
		NextWaveCountdown = 0f;

		if (weapons != null) weapons.AddGrenades(grenadeRefillPerWave);
		if (playerHealth != null)
		{
			playerHealth.Heal(healPerWave);
			playerHealth.GrantInvulnerability(spawnProtectionTime);
		}

		int count = Mathf.Max(1, firstWaveSize + (Wave - 1) * extraEnemiesPerWave);
		for (int i = 0; i < count; i++)
			SpawnEnemy(i);

		RefreshAliveCount();
		if (WaveStarted != null) WaveStarted();
		Debug.Log("[WaveDirector] wave " + Wave + " spawned, enemies=" + count);
	}

	void SpawnEnemy(int index)
	{
		if (enemyPrefab == null) return;

		Vector3 position = transform.position;
		if (spawnPoints != null && spawnPoints.Length > 0)
		{
			var point = spawnPoints[index % spawnPoints.Length];
			if (point != null) position = point.position;
		}

		if (NavMesh.SamplePosition(position, out var hit, 6f, NavMesh.AllAreas))
			position = hit.position;

		Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
		var enemy = Instantiate(enemyPrefab, position, rotation);
		enemy.name = enemyPrefab.name;

		var chase = enemy.GetComponent<EnemyChaseMovement>();
		var player = GameObject.FindGameObjectWithTag("Player");
		if (chase != null && player != null)
		{
			chase.player = player.transform;
			if (chase.lineOfSightOrigin == null) chase.lineOfSightOrigin = enemy.transform;
			if (chase.agent == null) chase.agent = enemy.GetComponent<NavMeshAgent>();
			// 新一波敌人生成后直接进入警戒状态：可见玩家就追击，而不是原地巡逻。
			chase.PlayerIsNowInRange();
		}

		if (enemy.GetComponent<EnemyPatrol>() == null)
		{
			var patrol = enemy.AddComponent<EnemyPatrol>();
			patrol.agent = enemy.GetComponent<NavMeshAgent>();
			patrol.chaseMovement = chase;
			if (chase != null) patrol.animator = chase.animator;
		}

		var attack = enemy.GetComponent<EnemyAttack>();
		if (attack != null && player != null) attack.player = player.transform;
	}

	void OnPlayerDied()
	{
		EndMission(false);
	}

	public void EndMission(bool victory)
	{
		if (MissionOver) return;

		MissionOver = true;
		Victory = victory;
		Debug.Log("[WaveDirector] mission over, victory=" + victory + ", wave=" + Wave);
		if (MissionEnded != null) MissionEnded(victory);
	}

	public void Restart()
	{
		var scene = SceneManager.GetActiveScene();
		SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
	}
}