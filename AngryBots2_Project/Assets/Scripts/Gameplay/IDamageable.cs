/// <summary>
/// 统一的可受伤接口。所有会掉血的实体（玩家 / 敌人 / 可破坏物）都实现它，
/// 让武器、爆炸、陷阱只需要认这一个接口，不必认识具体组件类型。
/// <para>
/// 兼容性说明：现有的 <c>PlayerHealth</c> / <c>EnemyHealth</c> 位于其它目录且被旧场景依赖，
/// 为了不改动原文件、不影响旧序列化数据，采用「继承适配」的方式接入：
/// <c>PlayerHealthEx : PlayerHealth, IDamageable</c>、<c>EnemyHealthEx : EnemyHealth, IDamageable</c>。
/// 旧组件只要在场景里有 <c>PlayerHealthEx</c> / <c>EnemyHealthEx</c>，<c>GetComponent&lt;IDamageable&gt;()</c> 即可命中。
/// </para>
/// </summary>
public interface IDamageable
{
	/// <summary>是否已经死亡（死亡后不再接受伤害）。</summary>
	bool IsDead { get; }

	/// <summary>当前血量。</summary>
	float CurrentHealth { get; }

	/// <summary>最大血量。</summary>
	float MaxHealth { get; }

	/// <summary>该实体属于哪一类敌人；玩家 / 中性对象返回 <c>null</c>。</summary>
	EnemyKind? Kind { get; }

	/// <summary>是否由玩家阵营控制。</summary>
	bool IsPlayer { get; }

	/// <summary>承受一次伤害，返回实际扣除的血量。</summary>
	float ApplyDamage(DamageInfo info);
}

/// <summary>
/// 可被统一伤害工具（<see cref="DamageUtility"/>）使用的辅助契约：敌人死亡时把击杀者信息交回，
/// 便于计分与连击系统归属。
/// </summary>
public interface IEnemyDamageable : IDamageable
{
	/// <summary>最后一名造成伤害的玩家来源（用于击杀归属），可能为 null。</summary>
	UnityEngine.GameObject LastPlayerAttacker { get; }
}