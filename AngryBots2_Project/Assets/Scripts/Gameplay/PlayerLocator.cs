using UnityEngine;

/// <summary>
/// 玩家定位器：缓存带 <c>Player</c> tag 的对象，避免大量敌人每帧 <c>FindGameObjectWithTag</c>。
/// 存在性由 <c>GameplayBootstrap</c> 在运行时创建；敌人 AI 在拿不到单例时自行回退到 Find，
/// 保证脱离 bootstrap 的单测 / 预制体预览也能工作。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLocator : MonoBehaviour
{
	/// <summary>当前场景中的单例（可能为 null）。</summary>
	public static PlayerLocator Instance { get; private set; }

	[Tooltip("玩家根对象（一般带 PlayerHealth）。留空则运行时按 tag 查找。")]
	public Transform playerRoot;

	[Tooltip("玩家视觉中心，用于瞄准。留空则取 playerRoot 位置 + offset。")]
	public Vector3 aimOffset = new Vector3(0f, 1.2f, 0f);

	[Tooltip("每帧重新校验玩家引用（玩家可能被重生 / 替换）。")]
	public bool refreshEveryFrame;

	[Tooltip("刷新间隔（秒），refreshEveryFrame 关闭时按此周期校验。")]
	public float refreshInterval = 1f;

	/// <summary>玩家 Transform；未找到时为 null。</summary>
	public Transform Player { get { return playerRoot; } }

	/// <summary>玩家瞄准点（世界坐标）。</summary>
	public Vector3 AimPoint
	{
		get { return playerRoot != null ? playerRoot.position + aimOffset : Vector3.zero; }
	}

	/// <summary>玩家是否存活（有 PlayerHealth 时以血量为准）。</summary>
	public bool IsPlayerAlive
	{
		get
		{
			if (playerRoot == null) return false;
			var health = playerRoot.GetComponent<PlayerHealth>();
			return health == null || !health.IsDead;
		}
	}

	float nextRefresh;

	/// <summary>安全获取玩家 Transform：先走单例，找不到再回退到 tag 查询并缓存。</summary>
	public static Transform GetPlayer()
	{
		var locator = Instance;
		if (locator != null && locator.playerRoot != null) return locator.playerRoot;

		var found = GameObject.FindGameObjectWithTag("Player");
		if (found != null)
		{
			if (locator == null)
			{
				// 没有 bootstrap 时，延迟到第一次访问再建单例，避免在静态构造里碰场景对象。
				var go = new GameObject("[PlayerLocator]");
				locator = go.AddComponent<PlayerLocator>();
			}
			locator.playerRoot = found.transform;
			return found.transform;
		}
		return null;
	}

	/// <summary>安全获取玩家瞄准点。</summary>
	public static Vector3 GetPlayerAimPoint()
	{
		var locator = Instance;
		if (locator != null && locator.playerRoot != null) return locator.AimPoint;
		var player = GetPlayer();
		return player != null ? player.position + Vector3.up * 1.2f : Vector3.zero;
	}

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(this);
			return;
		}
		Instance = this;
		Resolve();
	}

	void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	void Update()
	{
		if (refreshEveryFrame)
		{
			Resolve();
			return;
		}

		if (playerRoot != null && playerRoot.gameObject.activeInHierarchy) return;
		if (Time.time < nextRefresh) return;

		nextRefresh = Time.time + Mathf.Max(0.1f, refreshInterval);
		Resolve();
	}

	void Resolve()
	{
		if (playerRoot != null && playerRoot.gameObject.activeInHierarchy) return;
		var found = GameObject.FindGameObjectWithTag("Player");
		if (found != null) playerRoot = found.transform;
	}
}