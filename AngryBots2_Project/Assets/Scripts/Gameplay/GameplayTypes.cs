using UnityEngine;

/// <summary>
/// 敌人切换的 AI 状态。由 <c>EnemyBehaviour</c> 基类使用，四类新敌人共享同一套状态机。
/// </summary>
public enum EnemyState
{
	/// <summary>原地待机（无目标、等待波次开场）。</summary>
	Idle = 0,

	/// <summary>沿路线 / 刷怪点附近巡逻。</summary>
	Patrol = 1,

	/// <summary>追击玩家。</summary>
	Chase = 2,

	/// <summary>执行攻击（有前摇 / 冷却）。</summary>
	Attack = 3,

	/// <summary>受击硬直。</summary>
	Stagger = 4,

	/// <summary>已死亡。</summary>
	Dead = 5,
}

/// <summary>
/// 敌人的结束方式，用于计分归因（击杀 / 自毁 / 环境）。
/// </summary>
public enum EnemyDeathCause
{
	/// <summary>被伤害打死。</summary>
	Killed = 0,

	/// <summary>自爆 / 自毁。</summary>
	SelfDestruct = 1,

	/// <summary>环境（掉落、陷阱）。</summary>
	Environment = 2,

	/// <summary>关卡清场 / 调试移除。</summary>
	Removed = 3,
}

[System.Flags]
public enum EnemyArchetype
{
	None = 0,

	/// <summary>地面单位，优先走 NavMeshAgent。</summary>
	Ground = 1,

	/// <summary>飞行单位，优先走 Rigidbody 悬浮。</summary>
	Flying = 2,

	/// <summary>远程攻击。</summary>
	Ranged = 4,

	/// <summary>近战 / 冲撞。</summary>
	Melee = 8,

	/// <summary>自爆。</summary>
	SelfDestruct = 16,

	/// <summary>Boss。</summary>
	Boss = 32,
}

/// <summary>
/// 把「伤害来源对象」解析成玩家 GameObject 的小工具。
/// 子弹 / 特效往往不是玩家本体，需要沿父级回溯一次。
/// </summary>
public static class PlayerSource
{
	/// <summary>玩家 tag 对应的对象缓存（避免热点路径 FindGameObjectWithTag）。</summary>
	static Transform cachedPlayer;

	/// <summary>当前场景的玩家 Transform。</summary>
	public static Transform Player
	{
		get
		{
			if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy) return cachedPlayer;
			cachedPlayer = PlayerLocator.GetPlayer();

			// 兜底：U6 战役场景 / Edit 模式自检里没有 Player tag，按组件查一次。
			if (cachedPlayer == null)
			{
				var health = UnityEngine.Object.FindObjectOfType<PlayerHealth>();
				if (health != null && health.gameObject.activeInHierarchy) cachedPlayer = health.transform;
			}
			return cachedPlayer;
		}
	}

	/// <summary>判断某个对象是否属于玩家阵营（玩家本体或有 <c>PlayerHealth</c> 的父级）。</summary>
	public static bool IsPlayer(GameObject source)
	{
		if (source == null) return false;
		if (source.CompareTag("Player")) return true;
		if (source.GetComponentInParent<PlayerHealth>() != null) return true;

		var player = Player;
		if (player == null) return false;
		return source.transform.IsChildOf(player);
	}

	/// <summary>把任意伤害来源归一化成玩家 GameObject（不是玩家则返回 null）。</summary>
	public static GameObject Resolve(GameObject source)
	{
		if (source == null) return null;
		if (source.CompareTag("Player")) return source;

		var health = source.GetComponentInParent<PlayerHealth>();
		if (health != null) return health.gameObject;

		var player = Player;
		if (player != null && source.transform.IsChildOf(player)) return player.gameObject;

		return null;
	}
}

/// <summary>
/// 数值缩放工具：难度曲线 / 升级系统共用。
/// </summary>
public static class GameplayMath
{
	/// <summary>线性曲线：value = base + perStep * (step - 1)。</summary>
	public static float Linear(float baseValue, float perStep, int step)
	{
		return baseValue + perStep * Mathf.Max(0, step - 1);
	}

	/// <summary>指数曲线：value = base * pow(growth, step - 1)。</summary>
	public static float Exponential(float baseValue, float growth, int step)
	{
		return baseValue * Mathf.Pow(growth, Mathf.Max(0, step - 1));
	}

	/// <summary>把伤害草稿取整并保证至少 1（避免 0 伤害飘字）。</summary>
	public static int RoundDamage(float amount)
	{
		if (amount <= 0f) return 0;
		return Mathf.Max(1, Mathf.RoundToInt(amount));
	}
}