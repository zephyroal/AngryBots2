using UnityEngine;

/// <summary>
/// 玩家血量适配器：不改动 <c>PlayerHealth.cs</c>，用子类补出 <see cref="IDamageable"/> 与事件总线转发。
/// <para>
/// <c>PlayerHealth</c> 的 <c>TakeDamage(int)</c> / <c>Heal(int)</c> 未标记 virtual，因此这里用
/// <c>new</c> 隐藏 + 显式转发的方式包装：旧代码照常调用父类实现，新代码统一走 <see cref="ApplyDamage"/>。
/// </para>
/// </summary>
public sealed class PlayerHealthAdapter : PlayerHealth, IDamageable
{
	/// <summary>玩家受到伤害：(当前, 最大, 伤害信息)。</summary>
	public event System.Action<int, int, DamageInfo> PlayerDamaged;

	/// <summary>玩家血量变化：(当前, 最大)。</summary>
	public event System.Action<int, int> PlayerHealthChangedInternal;

	/// <summary>是否已死亡（父类语义）。</summary>
	public bool IsDeadSafe { get { return IsDead; } }

	/// <summary>IDamageable：当前血量。</summary>
	float IDamageable.CurrentHealth { get { return CurrentHealth; } }

	/// <summary>IDamageable：最大血量。</summary>
	float IDamageable.MaxHealth { get { return MaxHealth; } }

	/// <summary>IDamageable：玩家没有敌人种类。</summary>
	EnemyKind? IDamageable.Kind { get { return null; } }

	/// <summary>IDamageable：这是玩家。</summary>
	bool IDamageable.IsPlayer { get { return true; } }

	/// <summary>统一伤害入口：走父类逻辑，并把结果广播到事件总线。</summary>
	public float ApplyDamage(DamageInfo info)
	{
		if (info.Amount <= 0f || IsDead) return 0f;

		int before = CurrentHealth;
		TakeDamageEx(Mathf.Max(1, Mathf.RoundToInt(info.Amount)));
		int applied = Mathf.Max(0, before - CurrentHealth);

		if (applied > 0)
		{
			if (PlayerDamaged != null) PlayerDamaged(CurrentHealth, MaxHealth, info);
			GameEvents.RaisePlayerDamaged(CurrentHealth, MaxHealth, info);
		}
		return applied;
	}

	/// <summary>对应父类 <c>TakeDamage(int)</c>，额外广播事件总线。</summary>
	public void TakeDamageEx(int amount)
	{
		int before = CurrentHealth;
		base.TakeDamage(amount);
		int applied = Mathf.Max(0, before - CurrentHealth);

		if (applied > 0)
		{
			var info = DamageInfo.Create(applied, null, transform.position, transform.forward);
			info.Feedback = HitFeedbackLevel.Critical;
			if (PlayerDamaged != null) PlayerDamaged(CurrentHealth, MaxHealth, info);
			GameEvents.RaisePlayerDamaged(CurrentHealth, MaxHealth, info);
		}
	}

	/// <summary>对应父类 <c>Heal(int)</c>，额外广播回血。</summary>
	public void HealEx(int amount)
	{
		int before = CurrentHealth;
		base.Heal(amount);
		if (CurrentHealth != before && PlayerHealthChangedInternal != null)
			PlayerHealthChangedInternal(CurrentHealth, MaxHealth);
	}

	/// <summary>对应父类的 <c>ResetHealth()</c>，额外广播。</summary>
	public void ResetHealthEx()
	{
		base.ResetHealth();
		if (PlayerHealthChangedInternal != null)
			PlayerHealthChangedInternal(CurrentHealth, MaxHealth);
	}
}