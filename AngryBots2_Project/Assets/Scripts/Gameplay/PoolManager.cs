using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用对象池：子弹 / 特效 / 伤害飘字等高频对象复用，避免 Instantiate 抖动与 GC。
/// <para>
/// 用法：
/// <code>
/// var bullet = PoolManager.Spawn(bulletPrefab, position, rotation);
/// ...
/// PoolManager.Despawn(bullet);            // 手动归还
/// PoolManager.DespawnAfter(bullet, 2f);   // 定时归还
/// </code>
/// 池化对象被激活时会调用其上的 <c>IPoolable.OnSpawned</c>，停用时调用 <c>IPoolable.OnDespawned</c>。
/// </para>
/// </summary>
public sealed class PoolManager : MonoBehaviour
{
	/// <summary>池化对象可实现的生命周期回调。</summary>
	public interface IPoolable
	{
		/// <summary>从池中取出并激活之后调用。</summary>
		void OnSpawned();

		/// <summary>归还到池中（尚未 SetActive(false)）时调用。</summary>
		void OnDespawned();
	}

	/// <summary>场景单例；不存在时按需创建。</summary>
	public static PoolManager Instance
	{
		get
		{
			if (instance == null)
			{
				var go = new GameObject("[PoolManager]");
				instance = go.AddComponent<PoolManager>();
			}
			return instance;
		}
	}

	static PoolManager instance;

	[Header("Settings")]
	[Tooltip("每个池在重建时预热的实例数上限（0 表示不预热）。")]
	public int prewarmCount = 0;

	[Tooltip("单个池最多缓存的空闲对象数，超出后销毁而不是继续堆积。")]
	public int maxCachedPerPool = 128;

	[Tooltip("池根节点是否在场景切换时保留。")]
	public bool dontDestroyOnLoad = true;

	sealed class Pool
	{
		public GameObject Prefab;
		public Transform Root;
		public readonly Stack<GameObject> Idle = new Stack<GameObject>();
	}

	readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>();
	readonly Dictionary<GameObject, GameObject> prefabOf = new Dictionary<GameObject, GameObject>();
	readonly List<KeyValuePair<GameObject, float>> scheduled = new List<KeyValuePair<GameObject, float>>();

	/// <summary>当前缓存的池数量。</summary>
	public int PoolCount { get { return pools.Count; } }

	/// <summary>当前等待自动归还的对象数量。</summary>
	public int PendingDespawns { get { return scheduled.Count; } }

	void Awake()
	{
		if (instance != null && instance != this)
		{
			Destroy(this);
			return;
		}
		instance = this;
		if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
	}

	void OnDestroy()
	{
		if (instance == this) instance = null;
	}

	void Update()
	{
		if (scheduled.Count == 0) return;

		float now = Time.time;
		for (int i = scheduled.Count - 1; i >= 0; i--)
		{
			var pair = scheduled[i];
			if (pair.Key == null)
			{
				scheduled.RemoveAt(i);
				continue;
			}
			if (now < pair.Value) continue;

			scheduled.RemoveAt(i);
			Despawn(pair.Key);
		}
	}

	/// <summary>从池中取出对象（没有则 Instantiate 并登记）。</summary>
	public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
	{
		return Instance.SpawnInternal(prefab, position, rotation, null);
	}

	/// <summary>从池中取出对象并指定父节点。</summary>
	public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
	{
		return Instance.SpawnInternal(prefab, position, rotation, parent);
	}

	/// <summary>泛型版本，省去 GetComponent。</summary>
	public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
	{
		if (prefab == null) return null;
		var go = Spawn(prefab.gameObject, position, rotation);
		return go != null ? go.GetComponent<T>() : null;
	}

	/// <summary>归还对象到池。</summary>
	public static void Despawn(GameObject go)
	{
		if (go == null) return;
		if (instance == null)
		{
			Object.Destroy(go);
			return;
		}
		instance.DespawnInternal(go);
	}

	/// <summary>delay 秒后自动归还。</summary>
	public static void DespawnAfter(GameObject go, float delay)
	{
		if (go == null) return;
		Instance.scheduled.Add(new KeyValuePair<GameObject, float>(go, Time.time + Mathf.Max(0f, delay)));
	}

	/// <summary>预热某个预制体（进入战斗前调用可消除首帧卡顿）。</summary>
	public void Prewarm(GameObject prefab, int count)
	{
		if (prefab == null || count <= 0) return;
		var pool = GetOrCreatePool(prefab);
		for (int i = 0; i < count; i++)
		{
			var go = Instantiate(prefab, pool.Root);
			go.SetActive(false);
			prefabOf[go] = prefab;
			pool.Idle.Push(go);
		}
	}

	/// <summary>清空所有池（切场景时调用）。</summary>
	public void ClearAll()
	{
		foreach (var pool in pools.Values)
		{
			while (pool.Idle.Count > 0)
			{
				var go = pool.Idle.Pop();
				if (go != null) Destroy(go);
			}
		}
		pools.Clear();
		prefabOf.Clear();
		scheduled.Clear();
	}

	GameObject SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
	{
		if (prefab == null) return null;

		var pool = GetOrCreatePool(prefab);
		GameObject go;

		while (pool.Idle.Count > 0)
		{
			go = pool.Idle.Pop();
			if (go != null)
			{
				var transform = go.transform;
				if (parent != null) transform.SetParent(parent, false);
				transform.position = position;
				transform.rotation = rotation;
				go.SetActive(true);
				NotifySpawned(go);
				return go;
			}
		}

		go = Instantiate(prefab, position, rotation, parent != null ? parent : pool.Root);
		go.name = prefab.name + "(Pooled)";
		prefabOf[go] = prefab;
		NotifySpawned(go);
		return go;
	}

	void DespawnInternal(GameObject go)
	{
		if (go == null) return;

		NotifyDespawned(go);

		GameObject prefab;
		if (!prefabOf.TryGetValue(go, out prefab) || prefab == null)
		{
			// 不是池化对象（外部直接 Instantiate 的），退化为普通销毁。
			Destroy(go);
			return;
		}

		var pool = GetOrCreatePool(prefab);
		if (pool.Idle.Count >= Mathf.Max(1, maxCachedPerPool))
		{
			prefabOf.Remove(go);
			Destroy(go);
			return;
		}

		go.SetActive(false);
		go.transform.SetParent(pool.Root, false);
		pool.Idle.Push(go);
	}

	Pool GetOrCreatePool(GameObject prefab)
	{
		Pool pool;
		if (pools.TryGetValue(prefab, out pool)) return pool;

		var rootObject = new GameObject("Pool:" + prefab.name);
		rootObject.transform.SetParent(transform, false);

		pool = new Pool { Prefab = prefab, Root = rootObject.transform };
		pools[prefab] = pool;

		if (prewarmCount > 0) Prewarm(prefab, prewarmCount);
		return pool;
	}

	static void NotifySpawned(GameObject go)
	{
		var behaviours = go.GetComponents<MonoBehaviour>();
		for (int i = 0; i < behaviours.Length; i++)
		{
			var poolable = behaviours[i] as IPoolable;
			if (poolable != null) poolable.OnSpawned();
		}
	}

	static void NotifyDespawned(GameObject go)
	{
		var behaviours = go.GetComponents<MonoBehaviour>();
		for (int i = 0; i < behaviours.Length; i++)
		{
			var poolable = behaviours[i] as IPoolable;
			if (poolable != null) poolable.OnDespawned();
		}
	}

	/// <summary>确保运行时存在池管理器（幂等）。</summary>
	public static PoolManager EnsureInstance()
	{
		return Instance;
	}
}