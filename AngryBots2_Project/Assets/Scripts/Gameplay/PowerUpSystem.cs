using System;
using UnityEngine;

/// <summary>临时增益种类。</summary>
public enum PowerUpKind
{
	/// <summary>伤害提升（默认 ×1.6 / 12 秒）。</summary>
	DamageBoost = 0,

	/// <summary>射速提升（射击间隔 ×0.55 / 10 秒）。</summary>
	RapidFire = 1,

	/// <summary>护盾：受到的伤害按比例降低（默认 ×0.35 / 8 秒）。</summary>
	Shield = 2,
}

/// <summary>
/// 玩家临时增益：伤害强化 / 快速射击 / 护盾，各自独立计时、可叠加不同种类。
/// <para>
/// 由 <see cref="SimplePickup"/> 拾取触发；<see cref="WeaponSystem"/> 读取伤害与射速倍率，
/// <see cref="PlayerHealth"/> 读取受击倍率。所有倍率在没有增益时恒为 1，未挂该组件时行为与原来一致。
/// </para>
/// </summary>
public sealed class PowerUpSystem : MonoBehaviour
{
	public const int KindCount = 3;

	[Header("Durations (seconds)")]
	public float damageBoostDuration = 12f;
	public float rapidFireDuration = 10f;
	public float shieldDuration = 8f;

	[Header("Effects")]
	[Tooltip("伤害强化倍率。")]
	public float damageBoostMultiplier = 1.6f;

	[Tooltip("快速射击的时间间隔缩放（<1 更快）。")]
	public float rapidFireIntervalScale = 0.55f;

	[Tooltip("护盾生效时受到的伤害缩放（<1 更硬）。")]
	public float shieldIncomingScale = 0.35f;

	/// <summary>某个增益被激活：(种类, 持续秒数)。</summary>
	public event Action<PowerUpKind, float> Activated;

	/// <summary>某个增益到期。</summary>
	public event Action<PowerUpKind> Expired;

	readonly float[] remaining = new float[KindCount];

	// ── 查询 ──────────────────────────────────────────────
	public bool IsActive(PowerUpKind kind)
	{
		int index = (int)kind;
		return index >= 0 && index < KindCount && remaining[index] > 0f;
	}

	public float Remaining(PowerUpKind kind)
	{
		int index = (int)kind;
		return index >= 0 && index < KindCount ? remaining[index] : 0f;
	}

	/// <summary>当前是否有任意增益在生效。</summary>
	public bool AnyActive
	{
		get
		{
			for (int i = 0; i < KindCount; i++) if (remaining[i] > 0f) return true;
			return false;
		}
	}

	/// <summary>剩余时间最长的增益（供 HUD 显示）。</summary>
	public PowerUpKind Dominant
	{
		get
		{
			int best = -1;
			float bestTime = 0f;
			for (int i = 0; i < KindCount; i++)
			{
				if (remaining[i] > bestTime) { bestTime = remaining[i]; best = i; }
			}
			return best < 0 ? PowerUpKind.DamageBoost : (PowerUpKind)best;
		}
	}

	public float DamageMultiplier { get { return IsActive(PowerUpKind.DamageBoost) ? damageBoostMultiplier : 1f; } }
	public float FireIntervalMultiplier { get { return IsActive(PowerUpKind.RapidFire) ? rapidFireIntervalScale : 1f; } }
	public float IncomingDamageMultiplier { get { return IsActive(PowerUpKind.Shield) ? shieldIncomingScale : 1f; } }

	// ── 驱动 ──────────────────────────────────────────────
	public void Activate(PowerUpKind kind, float duration = 0f)
	{
		int index = (int)kind;
		if (index < 0 || index >= KindCount) return;

		float length = duration > 0f ? duration : DefaultDuration(kind);
		if (length <= 0f) return;

		// 同类增益取较长的剩余时间，避免连续拾取反而缩短
		remaining[index] = Mathf.Max(remaining[index], length);
		if (Activated != null) Activated(kind, remaining[index]);
	}

	/// <summary>清空全部增益（重开一局 / 死亡时调用）。</summary>
	public void ClearAll()
	{
		for (int i = 0; i < KindCount; i++)
		{
			if (remaining[i] <= 0f) continue;
			remaining[i] = 0f;
			if (Expired != null) Expired((PowerUpKind)i);
		}
	}

	void Update()
	{
		Advance(Time.deltaTime);
	}

	/// <summary>推进计时（拆出来供自检直接调用）。</summary>
	public void Advance(float deltaSeconds)
	{
		if (deltaSeconds <= 0f) return;

		for (int i = 0; i < KindCount; i++)
		{
			if (remaining[i] <= 0f) continue;
			remaining[i] -= deltaSeconds;
			if (remaining[i] > 0f) continue;

			remaining[i] = 0f;
			if (Expired != null) Expired((PowerUpKind)i);
		}
	}

	float DefaultDuration(PowerUpKind kind)
	{
		switch (kind)
		{
			case PowerUpKind.RapidFire: return rapidFireDuration;
			case PowerUpKind.Shield: return shieldDuration;
			default: return damageBoostDuration;
		}
	}

	/// <summary>确保玩家身上有增益组件（幂等）。</summary>
	public static PowerUpSystem Ensure(GameObject player)
	{
		if (player == null) return null;
		var system = player.GetComponent<PowerUpSystem>();
		return system != null ? system : player.AddComponent<PowerUpSystem>();
	}
}
