using UnityEngine;

/// <summary>
/// 敌人血量适配器基类：把旧的 <c>EnemyHealth</c> 接入统一伤害体系，同时**不改动 EnemyHealth.cs**。
/// <para>
/// AB2 的 <c>EnemyHealth</c> 只有 <c>RemoveHealth(int)</c>、没有最大 / 当前血量访问器，
/// 且被大量旧预制体与场景引用。具体子类 <c>EnemyHealthEx</c> 记录当前血量与最大值
/// （旧字段 <c>totalHealth</c> 为 public，可直接读写），并提供 <see cref="IEnemyDamageable"/>。
/// </para>
/// </summary>
public abstract class EnemyHealthAdapter : MonoBehaviour, IEnemyDamageable
{
	/// <summary>受到伤害时触发：(实际扣血量, 是否致死)。</summary>
	public event System.Action<float, bool> Damaged;

	/// <summary>死亡时触发（在 GameObject 被销毁之前）。</summary>
	public event System.Action Died;

	/// <summary>关联的敌人元数据（可能为 null，例如纯旧敌人还未补挂）。</summary>
	public EnemyAgent Agent { get { return agent; } }

	/// <summary>关键字种类，没有元数据时按 Spider 处理。</summary>
	public EnemyKind? Kind { get { return agent != null ? (EnemyKind?)agent.Kind : null; } }

	/// <summary>敌人不是玩家。</summary>
	public bool IsPlayer { get { return false; } }

	/// <summary>最后一名玩家攻击者。</summary>
	public GameObject LastPlayerAttacker { get { return agent != null ? agent.LastPlayerAttacker : null; } }

	/// <summary>是否已死亡。</summary>
	public abstract bool IsDead { get; }

	/// <summary>当前血量。</summary>
	public abstract float CurrentHealth { get; }

	/// <summary>最大血量。</summary>
	public abstract float MaxHealth { get; }

	EnemyAgent agent;

	/// <summary>旧 EnemyHealth 组件的引用（纯旧敌人通过它读写血量；缺省时为组件自身）。</summary>
	protected EnemyHealth LegacyHealth { get; private set; }

	protected virtual void Awake()
	{
		agent = GetComponent<EnemyAgent>();
		var ex = GetComponent<EnemyHealthEx>();
		LegacyHealth = ex != null ? (EnemyHealth)ex : GetComponent<EnemyHealth>();
	}

	/// <summary>把血量同步回旧 EnemyHealth（纯旧敌人改造后使用）。</summary>
	protected void PushHealth(int current, int max)
	{
		if (LegacyHealth == null || ReferenceEquals(LegacyHealth, this)) return;
		LegacyHealth.totalHealth = Mathf.Max(1, max);

		if (current <= 0) LegacyHealth.RemoveHealth(LegacyHealth.totalHealth + 1);
		else LegacyHealth.RemoveHealth(current - Mathf.Max(1, max));
	}

	/// <summary>让宿主挂上 EnemyAgent（旧敌人在运行时升级用）。</summary>
	protected EnemyAgent EnsureAgent()
	{
		if (agent == null) agent = GetComponent<EnemyAgent>();
		if (agent == null) agent = gameObject.AddComponent<EnemyAgent>();
		return agent;
	}

	protected virtual void OnEnable()
	{
		if (agent != null) agent.ResolveHealth();
	}

	/// <summary>统一伤害入口。</summary>
	public virtual float ApplyDamage(DamageInfo info)
	{
		if (IsDead) return 0f;
		if (agent != null || EnsureAgent() != null) return EnsureAgent().ApplyDamage(info);

		float before = CurrentHealth;
		float armorFree = Mathf.Max(0f, info.Amount);
		ApplyRawDamage(GameplayMath.RoundDamage(armorFree));
		float applied = Mathf.Max(0f, before - CurrentHealth);

		if (applied > 0f) RaiseDamaged(applied, IsDead);
		return applied;
	}

	/// <summary>直接扣血（整数）。子类实现。</summary>
	public abstract void ApplyRawDamage(int amount);

	protected void RaiseDamaged(float amount, bool lethal)
	{
		if (Damaged != null) Damaged(amount, lethal);
	}

	protected void RaiseDied()
	{
		if (Died != null) Died();
	}
}