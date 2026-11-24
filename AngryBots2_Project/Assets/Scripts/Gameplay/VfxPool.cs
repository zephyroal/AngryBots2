using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次性特效的池化入口：从 <see cref="PoolManager"/> 取实例、播放全部粒子系统，并在估算时长后自动归还。
/// <para>
/// 相比直接 <c>Instantiate</c>：不产生每次生成的 GC，也不依赖粒子系统的 <c>StopAction = Destroy</c>
/// （池化实例若自杀会把池里的对象一起销毁，所以这里逐实例改成 <c>None</c>）。
/// </para>
/// </summary>
public static class VfxPool
{
	static readonly Dictionary<GameObject, float> lifetimes = new Dictionary<GameObject, float>();

	public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
	{
		return Spawn(prefab, position, rotation, null);
	}

	public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
	{
		return Spawn(prefab, position, rotation, parent, 1f);
	}

	/// <summary>带缩放的特效生成（狙击命中之类可以放大冲击）。</summary>
	public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, float scale)
	{
		if (prefab == null) return null;

		var instance = PoolManager.Spawn(prefab, position, rotation, parent);
		if (instance == null) return null;

		if (!Mathf.Approximately(scale, 1f))
			instance.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);

		foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
		{
			var main = ps.main;
			main.stopAction = ParticleSystemStopAction.None;
			ps.Clear(true);
			ps.Play(true);
		}

		PoolManager.DespawnAfter(instance, LifetimeOf(prefab));
		return instance;
	}

	/// <summary>估算特效实际存活时长（最长的 系统 duration + 单粒子寿命），按预制体缓存。</summary>
	public static float LifetimeOf(GameObject prefab)
	{
		float cached;
		if (prefab != null && lifetimes.TryGetValue(prefab, out cached)) return cached;

		float longest = 0f;
		if (prefab != null)
		{
			foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
			{
				var main = ps.main;
				float candidate = main.duration + main.startLifetime.constantMax;
				if (candidate > longest) longest = candidate;
			}
		}

		float life = longest > 0f ? longest + 0.1f : 1f;
		if (prefab != null) lifetimes[prefab] = life;
		return life;
	}
}
