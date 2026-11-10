using UnityEngine;

/// <summary>
/// 远程弹丸模式：决定敌人子弹如何飞行与结算。
/// </summary>
public enum EnemyProjectileMode
{
	/// <summary>直线飞行，撞到玩家或环境消失。</summary>
	Straight = 0,

	/// <summary>缓慢追踪玩家。</summary>
	Homing = 1,

	/// <summary>抛物线弹道（榴弹）。</summary>
	Lobbed = 2,
}

/// <summary>
/// 敌人定义数据（ScriptableObject）：一种敌人的全部数值与资源引用，
/// 供 <c>EnemyBehaviour</c> / <c>WaveDirectorEx</c> / <c>EnemySpawnTable</c> 读取。
/// <para>
/// 同时提供内联默认值工具 <see cref="CreateRuntimeDefault"/>，
/// 让 batchmode 自检在没有资产的情况下也能构造一个合法的定义。
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "Enemy_", menuName = "ABGameplay/Enemy Definition", order = 11)]
public sealed class EnemyDefinition : ScriptableObject
{
	[Header("Identity")]
	[Tooltip("敌人种类。")]
	public EnemyKind kind = EnemyKind.Spider;

	[Tooltip("显示名（HUD / 击杀提示用）。")]
	public string displayName = "Spider";

	[Tooltip("敌人预制体（刷怪系统用）。")]
	public GameObject prefab;

	[Header("Vitals")]
	[Tooltip("基础血量。")]
	public float maxHealth = 100f;

	[Tooltip("护甲减伤比例 0~1。")]
	[Range(0f, 0.95f)]
	public float armor = 0f;

	[Header("Movement")]
	[Tooltip("移动速度（米/秒）。")]
	public float moveSpeed = 4f;

	[Tooltip("转向速度（度/秒）。")]
	public float turnSpeed = 540f;

	[Tooltip("索敌半径（米）。")]
	public float detectionRadius = 22f;

	[Tooltip("失去目标半径（米）。")]
	public float loseTargetRadius = 32f;

	[Header("Attack")]
	[Tooltip("攻击伤害。")]
	public float attackDamage = 10f;

	[Tooltip("攻击间隔（秒）。")]
	public float attackInterval = 1.3f;

	[Tooltip("攻击前摇（秒），前摇内被打断会空掉。")]
	public float attackWindup = 0.25f;

	[Tooltip("近战攻击距离（米）。")]
	public float meleeRange = 2.2f;

	[Tooltip("远程攻击距离（米）。0 表示没有远程攻击。")]
	public float rangedRange = 0f;

	[Tooltip("弹丸数量（远程 / 弹幕）。")]
	public int projectileCount = 1;

	[Tooltip("弹丸速度（米/秒）。")]
	public float projectileSpeed = 18f;

	[Tooltip("弹丸模式。")]
	public EnemyProjectileMode projectileMode = EnemyProjectileMode.Straight;

	[Tooltip("弹丸预制体（可选）。为空时用程序化子弹。")]
	public GameObject projectilePrefab;

	[Header("Behaviour")]
	[Tooltip("行为标签。")]
	public EnemyArchetype archetype = EnemyArchetype.Ground;

	[Tooltip("受击硬直时长（秒）。")]
	public float staggerDuration = 0.18f;

	[Tooltip("硬直触发间隔（秒），避免被连射锁死。")]
	public float staggerCooldown = 0.6f;

	[Header("Scoring & Drops")]
	[Tooltip("击杀得分。")]
	public int score = 10;

	[Tooltip("掉落的血包概率 0~1。")]
	[Range(0f, 1f)]
	public float healthDropChance = 0f;

	[Tooltip("掉落的弹药包概率 0~1。")]
	[Range(0f, 1f)]
	public float ammoDropChance = 0f;

	[Header("Presentation")]
	[Tooltip("死亡爆炸特效（可选）。")]
	public GameObject deathEffectPrefab;

	[Tooltip("受击 / 攻击音效（可选）。")]
	public AudioClip[] vocalClips;

	/// <summary>是否为飞行单位。</summary>
	public bool IsFlying { get { return (archetype & EnemyArchetype.Flying) != 0; } }

	/// <summary>是否为 Boss。</summary>
	public bool IsBoss { get { return (archetype & EnemyArchetype.Boss) != 0 || kind == EnemyKind.Boss; } }

	/// <summary>是否有远程攻击。</summary>
	public bool IsRanged { get { return rangedRange > 0f && (archetype & EnemyArchetype.Ranged) != 0; } }

	/// <summary>数值合法性校验。</summary>
	public bool Validate(out string error)
	{
		error = string.Empty;
		if (maxHealth <= 0f) { error = "maxHealth <= 0"; return false; }
		if (moveSpeed <= 0f) { error = "moveSpeed <= 0"; return false; }
		if (attackDamage <= 0f && kind != EnemyKind.Buzzer) { error = "attackDamage <= 0"; return false; }
		if (attackInterval <= 0f) { error = "attackInterval <= 0"; return false; }
		if (detectionRadius <= 0f) { error = "detectionRadius <= 0"; return false; }
		if (score < 0) { error = "score < 0"; return false; }
		return true;
	}

	/// <summary>按种类生成一份内存中的默认定义（自检 / 兜底用，不落盘）。</summary>
	public static EnemyDefinition CreateRuntimeDefault(EnemyKind kind)
	{
		var definition = CreateInstance<EnemyDefinition>();
		definition.name = "Enemy_" + kind + "(Runtime)";
		definition.kind = kind;
		definition.displayName = kind.ToString();

		switch (kind)
		{
			case EnemyKind.Spider:
				definition.archetype = EnemyArchetype.Ground | EnemyArchetype.Melee;
				definition.maxHealth = 60f;
				definition.moveSpeed = 4.2f;
				definition.attackDamage = 10f;
				definition.attackInterval = 1.3f;
				definition.meleeRange = 2.2f;
				definition.score = 10;
				break;

			case EnemyKind.Mech:
				definition.archetype = EnemyArchetype.Ground | EnemyArchetype.Ranged | EnemyArchetype.Melee;
				definition.maxHealth = 320f;
				definition.armor = 0.25f;
				definition.moveSpeed = 2.6f;
				definition.turnSpeed = 260f;
				definition.attackDamage = 8f;
				definition.attackInterval = 0.18f;
				definition.meleeRange = 3.2f;
				definition.rangedRange = 26f;
				definition.projectileCount = 1;
				definition.projectileSpeed = 34f;
				definition.score = 60;
				break;

			case EnemyKind.Buzzer:
				definition.archetype = EnemyArchetype.Flying | EnemyArchetype.Ranged;
				definition.maxHealth = 45f;
				definition.moveSpeed = 6.5f;
				definition.attackDamage = 9f;
				definition.attackInterval = 2.1f;
				definition.rangedRange = 20f;
				definition.projectileCount = 1;
				definition.projectileSpeed = 12f;
				definition.projectileMode = EnemyProjectileMode.Homing;
				definition.score = 20;
				break;

			case EnemyKind.Kamikaze:
				definition.archetype = EnemyArchetype.Flying | EnemyArchetype.SelfDestruct;
				definition.maxHealth = 30f;
				definition.moveSpeed = 8.5f;
				definition.attackDamage = 32f;
				definition.attackInterval = 1.5f;
				definition.meleeRange = 2.4f;
				definition.score = 15;
				break;

			case EnemyKind.Boss:
				definition.archetype = EnemyArchetype.Ground | EnemyArchetype.Ranged | EnemyArchetype.Boss;
				definition.maxHealth = 2400f;
				definition.armor = 0.15f;
				definition.moveSpeed = 3f;
				definition.attackDamage = 18f;
				definition.attackInterval = 1f;
				definition.meleeRange = 4f;
				definition.rangedRange = 30f;
				definition.projectileCount = 5;
				definition.projectileSpeed = 16f;
				definition.score = 500;
				definition.staggerDuration = 0f;
				break;
		}

		return definition;
	}
}