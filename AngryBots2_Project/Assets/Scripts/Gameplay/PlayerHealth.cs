using System;
using UnityEngine;

/// <summary>
/// 玩家生命值。原工程只有敌人血量，玩家是无敌的。
/// 补充受击、无敌帧、死亡事件，供 HUD 与任务系统订阅。
/// </summary>
public class PlayerHealth : MonoBehaviour
{
	[Header("Health Settings")]
	public int maxHealth = 100;
	public float invulnerabilityTime = 0.7f;

	int currentHealth;
	bool initialized;

	/// <summary>当前血量。首次访问时惰性初始化（Edit 模式 / 运行时动态挂载都不会是 0）。</summary>
	public int CurrentHealth
	{
		get
		{
			EnsureInitialized();
			return currentHealth;
		}
		private set { currentHealth = value; initialized = true; }
	}

	void EnsureInitialized()
	{
		if (initialized) return;
		currentHealth = Mathf.Max(1, maxHealth);
		initialized = true;
	}
	public int MaxHealth { get { return maxHealth; } }
	public bool IsDead { get { return CurrentHealth <= 0; } }

	/// <summary>(current, max)</summary>
	public event Action<int, int> HealthChanged;
	public event Action Damaged;
	public event Action Died;

	PlayerMovementAndLook movement;
	WeaponSystem weapons;
	float invulnerableUntil;

	void Awake()
	{
		CurrentHealth = maxHealth;
		movement = GetComponent<PlayerMovementAndLook>();
		weapons = GetComponent<WeaponSystem>();
	}

	void Start()
	{
		RaiseHealthChanged();
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || IsDead) return;
		if (Time.time < invulnerableUntil) return;

		invulnerableUntil = Time.time + invulnerabilityTime;
		CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
		RaiseHealthChanged();
		if (Damaged != null) Damaged();

		if (CurrentHealth <= 0) Die();
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || IsDead) return;
		CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
		RaiseHealthChanged();
	}

	public void ResetHealth()
	{
		CurrentHealth = maxHealth;
		RaiseHealthChanged();
	}

	/// <summary>给一段限时的无敌（波次开场 / 重生保护）。</summary>
	public void GrantInvulnerability(float seconds)
	{
		invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);
	}

	void Die()
	{
		if (movement != null) movement.enabled = false;
		if (weapons != null) weapons.enabled = false;

		if (Died != null) Died();
	}

	void RaiseHealthChanged()
	{
		if (HealthChanged != null) HealthChanged(CurrentHealth, maxHealth);
	}
}