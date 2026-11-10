using UnityEngine;

/// <summary>
/// 全局事件总线：用 C# event 解耦各玩法子系统（HUD / 计分 / 音效 / 连击…），
/// 避免它们互相持有强引用。
/// <para>
/// 与旧系统的兼容桥接：AB2 原工程有两条静态事件被 <c>GameHud</c> 订阅 ——
/// <c>ProjectileBehaviour.EnemyHit</c> 与 <c>EnemyHealth.EnemyDied</c>。本总线不删除、不替换它们，
/// 而是在运行时额外订阅并转发成 <see cref="EnemyDamagedByPlayer"/> / <see cref="LegacyEnemyDestroyed"/>，
/// 新系统只订阅总线即可。桥接由 <c>GameplayBootstrap</c> 负责安装与卸载。
/// </para>
/// </summary>
public static class GameEvents
{
	// ───────────────────────────── 敌人 ─────────────────────────────

	/// <summary>敌人被击杀：(种类, 死亡位置, 击杀者, 计分值)。</summary>
	public static event System.Action<EnemyKind, Vector3, GameObject, int> EnemyKilled;

	/// <summary>敌人受到伤害：(受击者, 实际伤害, 伤害信息)。由 DamageReceiver / 适配器抛出。</summary>
	public static event System.Action<GameObject, float, DamageInfo> EnemyDamaged;

	/// <summary>旧的 <c>ProjectileBehaviour.EnemyHit</c> 转发（只有位置，没有伤害数值）。</summary>
	public static event System.Action<Vector3> EnemyDamagedByPlayer;

	/// <summary>旧的 <c>EnemyHealth.EnemyDied</c> 转发（只有位置，常用于命中标记）。</summary>
	public static event System.Action<Vector3> LegacyEnemyDestroyed;

	/// <summary>敌人注册 / 注销（转发 <see cref="EnemyRegistry"/>）。</summary>
	public static event System.Action<EnemyAgent> EnemyRegistered;
	public static event System.Action<EnemyAgent> EnemyUnregistered;

	// ───────────────────────────── 玩家 ─────────────────────────────

	/// <summary>玩家受伤：(当前血量, 最大血量, 伤害信息)。</summary>
	public static event System.Action<int, int, DamageInfo> PlayerDamaged;

	/// <summary>玩家血量变化：(当前, 最大)。</summary>
	public static event System.Action<int, int> PlayerHealthChanged;

	/// <summary>玩家死亡。</summary>
	public static event System.Action PlayerDied;

	// ───────────────────────────── 波次 ─────────────────────────────

	/// <summary>波次开始：(波次号, 本波敌人总数, 是否 Boss 波)。</summary>
	public static event System.Action<int, int, bool> WaveStarted;

	/// <summary>波次清空：(波次号, 是否 Boss 波)。</summary>
	public static event System.Action<int, bool> WaveCleared;

	/// <summary>任务结束：(是否胜利)。</summary>
	public static event System.Action<bool> MissionEnded;

	// ───────────────────────────── 计分 / 拾取 ─────────────────────────────

	/// <summary>分数变化：(当前总分, 增量)。</summary>
	public static event System.Action<int, int> ScoreChanged;

	/// <summary>玩家拾取物品：(拾取描述, 数值)。</summary>
	public static event System.Action<string, float> PickupCollected;

	// ───────────────────────────── 内部桥接 ─────────────────────────────

	/// <summary>内部：由 EnemyRegistry 调用的击杀通道（C# 事件只能在声明类内触发）。</summary>
	internal static System.Action<EnemyAgent, GameObject, Vector3, int> EnemyKilledInternal;

	static bool hooksInstalled;

	/// <summary>安装/卸载旧静态事件的转发桥（幂等）。由 <c>GameplayBootstrap</c> 调用。</summary>
	public static void InstallLegacyHooks()
	{
		if (hooksInstalled) return;
		hooksInstalled = true;

		ProjectileBehaviour.EnemyHit += OnLegacyEnemyHit;
		EnemyHealth.EnemyDied += OnLegacyEnemyDied;
		EnemyRegistry.EnemyRegistered += OnRegistryRegistered;
		EnemyRegistry.EnemyUnregistered += OnRegistryUnregistered;
		EnemyKilledInternal = OnEnemyKilledInternal;

		if (EnemyRegistry.AliveCount > 0)
		{
			var alive = EnemyRegistry.GetAlive();
			for (int i = 0; i < alive.Count; i++) RaiseEnemyRegistered(alive[i]);
		}
	}

	/// <summary>卸载旧静态事件的转发桥。</summary>
	public static void UninstallLegacyHooks()
	{
		if (!hooksInstalled) return;
		hooksInstalled = false;

		ProjectileBehaviour.EnemyHit -= OnLegacyEnemyHit;
		EnemyHealth.EnemyDied -= OnLegacyEnemyDied;
		EnemyRegistry.EnemyRegistered -= OnRegistryRegistered;
		EnemyRegistry.EnemyUnregistered -= OnRegistryUnregistered;
		EnemyKilledInternal = null;
	}

	/// <summary>桥接是否已安装（自检用）。</summary>
	public static bool LegacyHooksInstalled { get { return hooksInstalled; } }

	/// <summary>
	/// 只补齐击杀事件的内部通道（不订阅旧静态事件），供 Edit 模式 / 早期初始化使用。
	/// 幂等。
	/// </summary>
	public static void EnsureInternalChannel()
	{
		if (EnemyKilledInternal == null) EnemyKilledInternal = OnEnemyKilledInternal;
	}

	// ───────────────────────────── 抛发接口 ─────────────────────────────

	public static void RaisePlayerDamaged(int current, int max, DamageInfo info)
	{
		if (PlayerDamaged != null) PlayerDamaged(current, max, info);
		if (PlayerHealthChanged != null) PlayerHealthChanged(current, max);
	}

	public static void RaisePlayerHealthChanged(int current, int max)
	{
		if (PlayerHealthChanged != null) PlayerHealthChanged(current, max);
	}

	public static void RaisePlayerDied()
	{
		if (PlayerDied != null) PlayerDied();
	}

	public static void RaiseEnemyDamaged(GameObject target, float amount, DamageInfo info)
	{
		if (EnemyDamaged != null) EnemyDamaged(target, amount, info);
	}

	public static void RaiseEnemyKilled(EnemyKind kind, Vector3 position, GameObject killer, int score)
	{
		if (EnemyKilled != null) EnemyKilled(kind, position, killer, score);
	}

	public static void RaiseWaveStarted(int wave, int totalEnemies, bool isBoss)
	{
		if (WaveStarted != null) WaveStarted(wave, totalEnemies, isBoss);
	}

	public static void RaiseWaveCleared(int wave, bool isBoss)
	{
		if (WaveCleared != null) WaveCleared(wave, isBoss);
	}

	public static void RaiseMissionEnded(bool victory)
	{
		if (MissionEnded != null) MissionEnded(victory);
	}

	public static void RaiseScoreChanged(int total, int delta)
	{
		if (ScoreChanged != null) ScoreChanged(total, delta);
	}

	public static void RaisePickupCollected(string what, float amount)
	{
		if (PickupCollected != null) PickupCollected(what, amount);
	}

	// ───────────────────────────── 内部转发 ─────────────────────────────

	static void OnEnemyKilledInternal(EnemyAgent agent, GameObject killer, Vector3 position, int score)
	{
		RaiseEnemyKilled(agent != null ? agent.KindValue : EnemyKind.Spider, position, killer, score);
	}

	static void OnRegistryRegistered(EnemyAgent agent)
	{
		RaiseEnemyRegistered(agent);
	}

	static void OnRegistryUnregistered(EnemyAgent agent)
	{
		if (EnemyUnregistered != null) EnemyUnregistered(agent);
	}

	static void RaiseEnemyRegistered(EnemyAgent agent)
	{
		if (EnemyRegistered != null) EnemyRegistered(agent);
	}

	static void OnLegacyEnemyHit(Vector3 position)
	{
		if (EnemyDamagedByPlayer != null) EnemyDamagedByPlayer(position);
	}

	static void OnLegacyEnemyDied(Vector3 position)
	{
		if (LegacyEnemyDestroyed != null) LegacyEnemyDestroyed(position);
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	static void ResetOnLoad()
	{
		// 关闭 Domain Reload 时必须手动清空静态事件，否则残留的是上一个 Play 会话的委托。
		hooksInstalled = false;
		EnemyKilled = null;
		EnemyDamaged = null;
		EnemyDamagedByPlayer = null;
		LegacyEnemyDestroyed = null;
		EnemyRegistered = null;
		EnemyUnregistered = null;
		PlayerDamaged = null;
		PlayerHealthChanged = null;
		PlayerDied = null;
		WaveStarted = null;
		WaveCleared = null;
		MissionEnded = null;
		ScoreChanged = null;
		PickupCollected = null;
		EnemyKilledInternal = null;
	}
}