using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 存活敌人注册表：替代 <c>FindObjectsOfType&lt;EnemyHealth&gt;()</c> 的每帧查询。
/// 敌人自己在 <c>OnEnable</c> / <c>OnDisable</c> 调 <see cref="Register"/> / <see cref="Unregister"/>。
/// <para>
/// 不依赖任何具体敌人组件：只要敌人挂 <c>EnemyAgent</c>（由 <c>GameplayBootstrap</c> 自动补挂），
/// 就会出现在 <see cref="AliveCount"/> 与各查询接口里。旧 <c>EnemyHealth</c> 敌人由适配器补挂。
/// </para>
/// </summary>
public static class EnemyRegistry
{
	sealed class Entry
	{
		public EnemyAgent Agent;
		public EnemyKind Kind;
		public Transform Transform;
		public int SpawnWave;
	}

	static readonly Dictionary<EnemyAgent, Entry> Entries = new Dictionary<EnemyAgent, Entry>(64);
	static readonly List<EnemyAgent> Scratch = new List<EnemyAgent>(64);

	/// <summary>新敌人注册时触发。</summary>
	public static event System.Action<EnemyAgent> EnemyRegistered;

	/// <summary>敌人注销（死亡或销毁）时触发。</summary>
	public static event System.Action<EnemyAgent> EnemyUnregistered;

	/// <summary>当前存活敌人数量（O(1)）。</summary>
	public static int AliveCount { get { return Entries.Count; } }

	/// <summary>注册一个敌人。重复注册会被忽略。</summary>
	public static void Register(EnemyAgent agent)
	{
		if (agent == null || Entries.ContainsKey(agent)) return;

		Entries[agent] = new Entry
		{
			Agent = agent,
			Kind = agent.KindValue,
			Transform = agent.transform,
			SpawnWave = agent.SpawnWave,
		};

		if (EnemyRegistered != null) EnemyRegistered(agent);
	}

	/// <summary>注销一个敌人（死亡或销毁时调用）。</summary>
	public static void Unregister(EnemyAgent agent)
	{
		if (agent == null) return;
		if (!Entries.Remove(agent)) return;

		if (EnemyUnregistered != null) EnemyUnregistered(agent);
	}

	/// <summary>
	/// 敌人被击杀：先从存活表移除，再抛出击杀事件。
	/// 击杀事件走 <see cref="GameEvents.EnemyKilled"/>；若 <see cref="GameEvents.InstallLegacyHooks"/>
	/// 尚未被调用（例如 Edit 模式 / 早期初始化），这里会先自动安装一次，保证击杀事件不丢。
	/// </summary>
	public static void NotifyKilled(EnemyAgent agent, GameObject killer, Vector3 position, int scoreValue)
	{
		if (agent != null) Unregister(agent);

		GameEvents.EnsureInternalChannel();
		if (GameEvents.EnemyKilledInternal != null)
			GameEvents.EnemyKilledInternal(agent, killer, position, scoreValue);
	}

	/// <summary>清空注册表（切场景 / 单元测试用）。</summary>
	public static void Clear()
	{
		if (Entries.Count > 0)
		{
			Scratch.Clear();
			Scratch.AddRange(Entries.Keys);
			for (int i = 0; i < Scratch.Count; i++) Unregister(Scratch[i]);
		}
		Entries.Clear();
	}

	/// <summary>某个敌人是否已注册。</summary>
	public static bool Contains(EnemyAgent agent)
	{
		return agent != null && Entries.ContainsKey(agent);
	}

	/// <summary>某个种类当前有多少存活。</summary>
	public static int CountOf(EnemyKind kind)
	{
		int count = 0;
		foreach (var entry in Entries.Values)
			if (entry.Kind == kind) count++;
		return count;
	}

	/// <summary>某一波刷出的敌人还剩多少存活。</summary>
	public static int CountOfWave(int wave)
	{
		int count = 0;
		foreach (var entry in Entries.Values)
			if (entry.SpawnWave == wave) count++;
		return count;
	}

	/// <summary>取当前存活敌人快照（调用方负责释放列表内容；内部复用临时缓冲，返回的列表不要跨帧持有）。</summary>
	public static List<EnemyAgent> GetAlive()
	{
		Scratch.Clear();
		Scratch.AddRange(Entries.Keys);
		return Scratch;
	}

	/// <summary>取距离某点最近的存活敌人。</summary>
	public static EnemyAgent FindNearest(Vector3 position, float maxDistance = float.PositiveInfinity)
	{
		EnemyAgent best = null;
		float bestSqr = maxDistance * maxDistance;

		foreach (var entry in Entries.Values)
		{
			if (entry.Transform == null) continue;
			float sqr = (entry.Transform.position - position).sqrMagnitude;
			if (sqr < bestSqr)
			{
				bestSqr = sqr;
				best = entry.Agent;
			}
		}
		return best;
	}

	/// <summary>
	/// 找出距离某点最近的玩家来源敌人（用于爆炸伤害判定、锁定与 HUD 指示）。
	/// </summary>
	public static EnemyAgent FindNearestPlayerFacing(Vector3 position, Vector3 forward, float maxAngle)
	{
		EnemyAgent best = null;
		float bestDot = Mathf.Cos(maxAngle * Mathf.Deg2Rad);

		foreach (var entry in Entries.Values)
		{
			if (entry.Transform == null) continue;
			Vector3 dir = entry.Transform.position - position;
			if (dir.sqrMagnitude < 0.0001f) { best = entry.Agent; break; }
			float dot = Vector3.Dot(forward, dir.normalized);
			if (dot > bestDot)
			{
				bestDot = dot;
				best = entry.Agent;
			}
		}
		return best;
	}

	/// <summary>把注册表同步到场景（编辑器自检 / 运行时热加载用）：补齐缺失的 <c>EnemyAgent</c>。</summary>
	public static int SynchronizeScene()
	{
		var adapters = Object.FindObjectsOfType<EnemyHealthAdapter>();
		int added = 0;
		for (int i = 0; i < adapters.Length; i++)
		{
			if (adapters[i] == null) continue;
			if (adapters[i].GetComponent<EnemyAgent>() == null)
			{
				adapters[i].gameObject.AddComponent<EnemyAgent>();
				added++;
			}
		}
		return added;
	}

	/// <summary>调试信息：按种类统计存活数量。</summary>
	public static string Describe()
	{
		var builder = new System.Text.StringBuilder();
		builder.Append("EnemyRegistry alive=").Append(Entries.Count);
		builder.Append(" [");
		foreach (EnemyKind kind in System.Enum.GetValues(typeof(EnemyKind)))
		{
			int count = CountOf(kind);
			if (count > 0) builder.Append(kind).Append('=').Append(count).Append(' ');
		}
		builder.Append(']');
		return builder.ToString();
	}

	// 场景切换时清空，避免残留失效引用。
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	static void ResetOnLoad()
	{
		Entries.Clear();
		Scratch.Clear();
	}
}