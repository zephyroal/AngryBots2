using UnityEngine;

/// <summary>
/// 增益掉落器：监听 <see cref="GameEvents.EnemyKilled"/>，按敌人种类概率在死亡点掉落随机增益。
/// <para>
/// 不改动 <c>EnemyDefinition</c>（它是 GBK 编码的旧文件），掉落概率集中在这里调；
/// 与血包/弹药包的掉落相互独立，所以一次击杀可能同时掉补给与增益。
/// </para>
/// </summary>
public sealed class PowerUpDropper : MonoBehaviour
{
	[Header("Chances (0~1)")]
	[Tooltip("普通蜘蛛的掉落概率。")]
	[Range(0f, 1f)] public float spiderChance = 0.06f;

	[Tooltip("重型/飞行/自爆单位的掉落概率。")]
	[Range(0f, 1f)] public float heavyChance = 0.12f;

	[Tooltip("Boss 的掉落概率。")]
	[Range(0f, 1f)] public float bossChance = 1f;

	[Tooltip("关闭后不再掉落（调试用）。")]
	public bool dropEnabled = true;

	[Header("Wave Scaling")]
	[Tooltip("每多一波，掉落概率增加多少（加法）。")]
	public float chancePerWave = 0.02f;

	[Tooltip("波次加成后的概率上限（Boss 仍保持 100%）。")]
	[Range(0f, 1f)] public float maxChance = 0.35f;

	[Header("Per-Mission Weights")]
	[Tooltip("按关卡配置三种增益的出现权重；留空时从 Resources/PowerUpDropTable 读取，仍为空则等概率。")]
	public PowerUpDropTable dropTable;

	static PowerUpDropper instance;

	/// <summary>场景单例；不存在时按需创建。</summary>
	public static PowerUpDropper Instance
	{
		get
		{
			if (instance == null) instance = EnsureInstance();
			return instance;
		}
	}

	public static PowerUpDropper EnsureInstance()
	{
		if (instance != null) return instance;

		var existing = Object.FindObjectOfType<PowerUpDropper>();
		if (existing != null)
		{
			instance = existing;
			return instance;
		}

		var go = new GameObject("[PowerUpDropper]");
		instance = go.AddComponent<PowerUpDropper>();
		return instance;
	}

	void Awake()
	{
		if (instance != null && instance != this)
		{
			Destroy(this);
			return;
		}
		instance = this;
	}

	void OnEnable()
	{
		GameEvents.EnemyKilled += OnEnemyKilled;
	}

	void OnDisable()
	{
		GameEvents.EnemyKilled -= OnEnemyKilled;
	}

	/// <summary>在指定位置按当前关卡权重掉一个增益（chance 为额外保证概率，1 = 必掉）。</summary>
	public static void DropAt(Vector3 position, float chance)
	{
		if (chance <= 0f) return;
		if (chance < 1f && Random.value > chance) return;

		var dropper = Instance;
		var kind = dropper != null
			? dropper.PickKind(Random.value)
			: (PowerUpKind)Random.Range(0, PowerUpSystem.KindCount);

		SimplePickup.CreatePowerUp(kind, position + Vector3.up * 0.2f, 0f);
	}

	void OnEnemyKilled(EnemyKind kind, Vector3 position, GameObject killer, int score)
	{
		if (!dropEnabled) return;

		float chance = ChanceFor(kind);
		if (chance <= 0f || Random.value > chance) return;

		var pick = PickKind(Random.value);
		SimplePickup.CreatePowerUp(pick, position + Vector3.up * 0.2f, 0f);
	}

	/// <summary>按关卡权重挑一种增益（roll∈[0,1)，自检可直接调用）。</summary>
	public PowerUpKind PickKind(float roll01)
	{
		var table = ResolveTable();
		float[] weights = table != null
			? table.Resolve(CurrentMissionName(), CurrentSceneName(), CurrentMissionIndex())
			: new[] { 1f, 1f, 1f };

		return PowerUpDropTable.PickKind(weights, roll01);
	}

	PowerUpDropTable ResolveTable()
	{
		if (dropTable != null) return dropTable;
		dropTable = Resources.Load<PowerUpDropTable>("PowerUpDropTable");
		return dropTable;
	}

	static string CurrentMissionName()
	{
		var manager = ABGameplay.Campaign.CampaignManager.Instance;
		return manager != null && manager.Current != null ? manager.Current.displayName : null;
	}

	static string CurrentSceneName()
	{
		var manager = ABGameplay.Campaign.CampaignManager.Instance;
		return manager != null && manager.Current != null ? manager.Current.sceneName : null;
	}

	static int CurrentMissionIndex()
	{
		var manager = ABGameplay.Campaign.CampaignManager.Instance;
		return manager != null ? manager.CurrentIndex : -1;
	}

	/// <summary>按敌人种类返回掉落概率（自检可直接调用）。</summary>
	public float ChanceFor(EnemyKind kind)
	{
		// WaveDirectorEx 在 ABGameplay.Enemies 命名空间里
		var director = ABGameplay.Enemies.WaveDirectorEx.Instance;
		int wave = director != null ? Mathf.Max(1, director.Wave) : 1;
		return ChanceFor(kind, wave);
	}

	/// <summary>指定波次下的掉落概率：基础 + 每波加成 + 商城永久加成，上限 maxChance；Boss 恒为 100%。</summary>
	public float ChanceFor(EnemyKind kind, int wave)
	{
		float baseChance = BaseChanceFor(kind);
		if (baseChance <= 0f) return 0f;
		if (baseChance >= 1f) return 1f;

		float bonus = 0f;
		var upgrades = ABGameplay.Campaign.UpgradeSystem.Instance;
		if (upgrades != null) bonus = Mathf.Max(0f, upgrades.PowerUpDropBonus);

		float scaled = baseChance + chancePerWave * (Mathf.Max(1, wave) - 1) + bonus;
		return Mathf.Clamp(scaled, 0f, Mathf.Max(baseChance, maxChance + bonus));
	}

	float BaseChanceFor(EnemyKind kind)
	{
		switch (kind)
		{
			case EnemyKind.Boss: return bossChance;
			case EnemyKind.Spider: return spiderChance;
			default: return heavyChance;
		}
	}
}
