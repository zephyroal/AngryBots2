using UnityEngine;

/// <summary>
/// 伤害类型。用于后续做抗性 / 特殊反馈（例如爆炸伤害不吃护甲、等离子对 Buzzer 加伤）。
/// </summary>
public enum DamageType
{
	/// <summary>普通子弹 / 弹丸。</summary>
	Bullet = 0,

	/// <summary>近战（冲撞、利爪、震地）。</summary>
	Melee = 1,

	/// <summary>爆炸 / 范围伤害。</summary>
	Explosion = 2,

	/// <summary>能量 / 等离子。</summary>
	Energy = 3,

	/// <summary>环境伤害（跌落、陷阱、场景物体）。</summary>
	Environment = 4,

	/// <summary>调试 / 直接结算（例如脚本清场）。</summary>
	Direct = 5,
}

/// <summary>
/// 命中反馈等级，决定是否触发顿帧 / 震屏 / 飘字。
/// </summary>
public enum HitFeedbackLevel
{
	/// <summary>不产生任何反馈（背景伤害、持续伤害）。</summary>
	None = 0,

	/// <summary>普通命中：飘字 + 音效。</summary>
	Normal = 1,

	/// <summary>暴击 / 弱点：额外顿帧与震屏。</summary>
	Critical = 2,

	/// <summary>击杀：最强反馈。</summary>
	Kill = 3,
}

/// <summary>
/// 一次伤害事件的完整描述。所有新伤害入口都通过这个结构体，
/// 便于统一做来源归因（玩家 / 环境）、伤害飘字、计分与击杀统计。
/// </summary>
public struct DamageInfo
{
	/// <summary>伤害数值（正数）。</summary>
	public float Amount;

	/// <summary>伤害来源对象（可能是玩家、子弹、爆炸物，允许为 null）。</summary>
	public GameObject Source;

	/// <summary>伤害类型。</summary>
	public DamageType Type;

	/// <summary>命中点（世界坐标）。</summary>
	public Vector3 HitPoint;

	/// <summary>命中法线（世界坐标）。</summary>
	public Vector3 HitNormal;

	/// <summary>伤害来源方向（从受击者指向攻击者）。</summary>
	public Vector3 Direction;

	/// <summary>击退 / 冲量力度（0 表示不击退）。</summary>
	public float Knockback;

	/// <summary>命中反馈等级。</summary>
	public HitFeedbackLevel Feedback;

	/// <summary>是否由玩家造成（用于计分与击杀归属）。</summary>
	public bool FromPlayer;

	/// <summary>命中部位倍率后的标记（部位系统由 P3W 填充）。</summary>
	public bool IsCritical;

	/// <summary>用最少参数构造一条伤害信息（默认玩家子弹伤害）。</summary>
	public static DamageInfo Create(float amount, GameObject source, Vector3 hitPoint, Vector3 direction)
	{
		DamageInfo info;
		info.Amount = amount;
		info.Source = source;
		info.Type = DamageType.Bullet;
		info.HitPoint = hitPoint;
		info.HitNormal = -direction;
		info.Direction = direction;
		info.Knockback = 0f;
		info.Feedback = HitFeedbackLevel.Normal;
		info.FromPlayer = source != null;
		info.IsCritical = false;
		return info;
	}

	/// <summary>构造一条爆炸伤害信息。</summary>
	public static DamageInfo Explosion(float amount, GameObject source, Vector3 center, float knockback = 0f)
	{
		DamageInfo info;
		info.Amount = amount;
		info.Source = source;
		info.Type = DamageType.Explosion;
		info.HitPoint = center;
		info.HitNormal = Vector3.up;
		info.Direction = Vector3.up;
		info.Knockback = knockback;
		info.Feedback = HitFeedbackLevel.Normal;
		info.FromPlayer = source != null;
		info.IsCritical = false;
		return info;
	}

	/// <summary>是否是一条有效（> 0）的伤害。</summary>
	public bool IsValid { get { return Amount > 0f || IsCritical; } }

	public override string ToString()
	{
		return "DamageInfo(" + Amount.ToString("0.#") + ", " + Type + ", fromPlayer=" + FromPlayer + ")";
	}
}