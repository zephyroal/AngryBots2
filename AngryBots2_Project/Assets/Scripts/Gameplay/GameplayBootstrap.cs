using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩法基础层的运行时接线（场景单例，可重复调用 <see cref="Apply"/> 做到幂等）。
/// <para>
/// 负责：
/// <list type="number">
/// <item>创建 <see cref="PlayerLocator"/> / <see cref="PoolManager"/> / <see cref="DamageNumberSpawner"/> / <see cref="HitFeedback"/> 单例；</item>
/// <item>安装旧静态事件（<c>ProjectileBehaviour.EnemyHit</c> / <c>EnemyHealth.EnemyDied</c>）到 <see cref="GameEvents"/> 的桥；</item>
/// <item>把场景里所有旧 <c>EnemyHealth</c> 升级为 <c>EnemyHealthEx</c> 并补挂 <see cref="EnemyAgent"/>；</item>
/// <item>把玩家身上的 <c>PlayerHealth</c> / <c>WeaponSystem</c> 升级为适配子类；</item>
/// <item>把玩家身上的新系统（<see cref="HitFeedback"/> 等）补齐。</item>
/// </list>
/// 任何时候都不删除 / 不重写旧组件类型以外的数据；只在必要时替换组件类型，数值通过复制保留。
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class GameplayBootstrap : MonoBehaviour
{
	/// <summary>场景单例。</summary>
	public static GameplayBootstrap Instance { get; private set; }

	[Header("Upgrade Legacy Components")]
	[Tooltip("把场景内的旧 EnemyHealth 升级为 EnemyHealthEx（保留血量数值）并补挂 EnemyAgent。")]
	public bool upgradeLegacyEnemies = true;

	[Tooltip("把玩家身上的 PlayerHealth / WeaponSystem 升级为适配子类。")]
	public bool upgradePlayer = true;

	[Tooltip("补齐玩家身上的 HitFeedback（命中顿帧 / 震屏）。")]
	public bool addPlayerHitFeedback = true;

	[Tooltip("每个场景启动时自动同步一次。")]
	public bool autoApplyOnStart = true;

	[Tooltip("切换场景后自动重建（推荐开启，新关卡也能拿到基础设施）。")]
	public bool persistAcrossScenes = true;

	[Header("Report")]
	[Tooltip("上次 Apply 升级的旧敌人数量。")]
	public int upgradedEnemies;

	[Tooltip("上次 Apply 补挂的 EnemyAgent 数量。")]
	public int patchedAgents;

	/// <summary>是否已经完成一次接线。</summary>
	public bool Applied { get; private set; }

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(this);
			return;
		}

		Instance = this;
		if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
	}

	void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
			GameEvents.UninstallLegacyHooks();
		}
	}

	void Start()
	{
		if (autoApplyOnStart) Apply();
	}

	void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (!persistAcrossScenes || !autoApplyOnStart) return;
		Apply();
	}

	/// <summary>执行一次（幂等的）接线。可在编辑器里手动调用。</summary>
	public void Apply()
	{
		EnsureCoreSystems();
		GameEvents.InstallLegacyHooks();

		if (upgradeLegacyEnemies)
		{
			upgradedEnemies = UpgradeSceneEnemies();
			patchedAgents = EnemyRegistry.SynchronizeScene();
		}

		if (upgradePlayer) UpgradePlayer();

		Applied = true;
	}

	/// <summary>确保基础单例存在。</summary>
	public static void EnsureCoreSystems()
	{
		if (PlayerLocator.Instance == null)
		{
			var player = GameObject.FindGameObjectWithTag("Player");
			if (player == null) player = FindAnyPlayerFallback();

			var locatorObject = new GameObject("[PlayerLocator]");
			var locator = locatorObject.AddComponent<PlayerLocator>();
			if (player != null) locator.playerRoot = player.transform;
		}

		PoolManager.EnsureInstance();

		if (!Application.isPlaying) return;

		if (DamageNumberSpawner.Instance == null) DamageNumberSpawner.EnsureInstance();
		if (Object.FindObjectOfType<HitFeedback>() == null) HitFeedback.EnsureInstance();
		PowerUpDropper.EnsureInstance();
	}

	/// <summary>把场景里所有旧 <c>EnemyHealth</c> 组件升级为 <c>EnemyHealthEx</c>。</summary>
	public static int UpgradeSceneEnemies()
	{
		int upgraded = 0;
		var holders = Object.FindObjectsOfType<EnemyHealth>(true);

		foreach (var legacy in holders)
		{
			if (legacy == null) continue;
			if (legacy is EnemyHealthEx) continue;

			var host = legacy.gameObject;
			legacy.enabled = true;

			// 子类与父类序列化字段一致：运行时直接换成子类组件并复制旧数值。
			legacy.enabled = false;
			var upgradedComponent = host.AddComponent<EnemyHealthEx>();
			upgradedComponent.totalHealth = Mathf.Max(1, legacy.totalHealth);
			upgradedComponent.pointToGive = legacy.pointToGive;
			upgradedComponent.explosionPrefab = legacy.explosionPrefab;
			Object.Destroy(legacy);
			upgraded++;

			if (host.GetComponent<EnemyAgent>() == null) host.AddComponent<EnemyAgent>();
		}

		return upgraded;
	}

	/// <summary>把玩家身上的旧组件升级成适配子类。</summary>
	public static bool UpgradePlayer()
	{
		var player = GameObject.FindGameObjectWithTag("Player");
		if (player == null) player = FindAnyPlayerFallback();
		if (player == null) return false;

		bool changed = false;

		// 临时增益组件（伤害强化 / 快速射击 / 护盾）随玩家一起就位，幂等。
		PowerUpSystem.Ensure(player);

		var health = player.GetComponent<PlayerHealth>();
		if (health != null)
		{
			if (!(health is PlayerHealthAdapter))
			{
				int max = health.maxHealth;
				float invulnerability = health.invulnerabilityTime;
				health.enabled = false;
				var adapted = player.AddComponent<PlayerHealthAdapter>();
				adapted.maxHealth = max;
				adapted.invulnerabilityTime = invulnerability;
				Object.Destroy(health);
				changed = true;
			}
		}

		var weapons = player.GetComponent<WeaponSystem>();
		if (weapons != null && !(weapons is WeaponSystemAdapter))
		{
			CopyWeaponSettings(weapons, player.AddComponent<WeaponSystemAdapter>());
			weapons.enabled = false;
			Object.Destroy(weapons);
			changed = true;
		}

		if (player.GetComponent<HitFeedback>() == null) player.AddComponent<HitFeedback>();

		return changed;
	}

	static void CopyWeaponSettings(WeaponSystem source, WeaponSystemAdapter destination)
	{
		destination.projectilePrefab = source.projectilePrefab;
		destination.spawnPoint = source.spawnPoint;
		destination.muzzleParticles = source.muzzleParticles;
		destination.muzzleFlashRoot = source.muzzleFlashRoot;
		destination.fireAudio = source.fireAudio;
		destination.grenadePrefab = source.grenadePrefab;

		destination.rifleInterval = source.rifleInterval;
		destination.rifleMagSize = source.rifleMagSize;
		destination.rifleReloadTime = source.rifleReloadTime;
		destination.rifleDamage = source.rifleDamage;
		destination.rifleSpread = source.rifleSpread;

		destination.shotgunInterval = source.shotgunInterval;
		destination.shotgunMagSize = source.shotgunMagSize;
		destination.shotgunReloadTime = source.shotgunReloadTime;
		destination.shotgunDamage = source.shotgunDamage;
		destination.shotgunPellets = source.shotgunPellets;
		destination.shotgunSpread = source.shotgunSpread;

		destination.smgInterval = source.smgInterval;
		destination.smgMagSize = source.smgMagSize;
		destination.smgReloadTime = source.smgReloadTime;
		destination.smgDamage = source.smgDamage;
		destination.smgSpread = source.smgSpread;

		destination.grenadeCharges = source.grenadeCharges;
		destination.maxGrenadeCharges = source.maxGrenadeCharges;
		destination.grenadeCooldown = source.grenadeCooldown;
		destination.grenadeThrowForce = source.grenadeThrowForce;
	}

	/// <summary>兜底：没有 Player tag 时找一个带 PlayerHealth 的对象（U6 场景没有 Player tag）。</summary>
	public static GameObject FindAnyPlayerFallback()
	{
		var health = Object.FindObjectOfType<PlayerHealth>();
		return health != null ? health.gameObject : null;
	}

	/// <summary>运行时自动接线（幂等）。</summary>
	public static GameplayBootstrap EnsureInstance()
	{
		if (Instance != null) return Instance;

		var existing = Object.FindObjectOfType<GameplayBootstrap>();
		if (existing != null) return existing;

		var go = new GameObject("[GameplayBootstrap]");
		return go.AddComponent<GameplayBootstrap>();
	}
}