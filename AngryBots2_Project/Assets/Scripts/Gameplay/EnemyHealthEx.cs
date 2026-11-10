using UnityEngine;

/// <summary>
/// <see cref="EnemyHealth"/> 的适配子类：在旧逻辑之上补上血量访问器与统一伤害接口。
/// 序列化字段与父类完全一致，YAML 里只需把组件类型换成它即可，旧数值全部保留。
/// </summary>
public sealed class EnemyHealthEx : EnemyHealth, IEnemyDamageable
{
	/// <summary>受击回调：(实际扣血, 是否致死)。</summary>
	public event System.Action<float, bool> Damaged;

	/// <summary>死亡回调（在旧 RemoveEnemy 销毁之前触发）。</summary>
	public event System.Action Died;

	[SerializeField, Tooltip("运行时当前血量（主要用于调试显示）。")]
	int observedHealth;

	EnemyAgent agent;
	bool initialized;

	/// <summary>当前血量。</summary>
	public float CurrentHealth { get { return initialized ? observedHealth : totalHealth; } }

	/// <summary>最大血量。</summary>
	public float MaxHealth { get { return Mathf.Max(1, totalHealth); } }

	/// <summary>血量比例 0~1，供血条 / 阶段切换使用。</summary>
	public float HealthRatio { get { return Mathf.Clamp01(CurrentHealth / MaxHealth); } }

	/// <summary>是否已死亡。</summary>
	public new bool IsDead { get { return initialized ? observedHealth <= 0 : false; } }

	/// <summary>种类（取元数据，没有则 Spider）。</summary>
	public EnemyKind? Kind { get { return agent != null ? (EnemyKind?)agent.Kind : EnemyKind.Spider; } }

	/// <summary>敌人不是玩家。</summary>
	public bool IsPlayer { get { return false; } }

	/// <summary>最后一名玩家攻击者。</summary>
	public GameObject LastPlayerAttacker { get { return agent != null ? agent.LastPlayerAttacker : null; } }

	/// <summary>关联元数据。</summary>
	public EnemyAgent Agent { get { return agent; } }

	void Awake()
	{
		agent = GetComponent<EnemyAgent>();
		initializeObserved();
	}

	void OnEnable()
	{
		// 对象池复用：重新启用时若血量已被打空，恢复满血。
		if (initialized && observedHealth <= 0) initializeObserved();
		if (agent != null) agent.ResolveHealth();
	}

	void initializeObserved()
	{
		observedHealth = Mathf.Max(1, totalHealth);
		initialized = true;
	}

	/// <summary>重置血量到指定值（同时更新 totalHealth，使 HUD 比例正确）。</summary>
	public void ResetHealth(int max)
	{
		totalHealth = Mathf.Max(1, max);
		observedHealth = totalHealth;
		initialized = true;
	}

	/// <summary>统一伤害入口（护甲 / 飘字 / 击杀归属由 EnemyAgent 处理）。</summary>
	public float ApplyDamage(DamageInfo info)
	{
		if (IsDead) return 0f;
		if (agent == null) agent = GetComponent<EnemyAgent>();
		if (agent != null)
		{
			float applied = agent.ApplyDamage(info);
			observedHealth = Mathf.Max(0, Mathf.CeilToInt(agent.CurrentHealth));
			return applied;
		}

		float before = CurrentHealth;
		ApplyRawDamage(GameplayMath.RoundDamage(info.Amount));
		float appliedRaw = Mathf.Max(0f, before - CurrentHealth);
		if (appliedRaw > 0f && Damaged != null) Damaged(appliedRaw, IsDead);
		return appliedRaw;
	}

	/// <summary>直接扣血（保持旧语义：<= 0 时销毁自身）。</summary>
	public void ApplyRawDamage(int amount)
	{
		RemoveHealth(amount);
	}

	/// <summary>覆写旧入口，保证外部直接调旧 API 时也能同步观察血量并抛事件。</summary>
	public new void RemoveHealth(int damageTaken)
	{
		if (!initialized) initializeObserved();
		if (observedHealth <= 0) return;

		int before = observedHealth;
		base.RemoveHealth(damageTaken);

		int after = Mathf.Max(0, before - damageTaken);
		bool lethal = after <= 0;
		observedHealth = after;

		if (Damaged != null) Damaged(before - after, lethal);
		if (lethal && Died != null) Died();
	}
}