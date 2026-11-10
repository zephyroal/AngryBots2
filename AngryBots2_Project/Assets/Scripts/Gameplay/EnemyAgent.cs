using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人的全局元数据与运行时注册组件（P3B 起由怪物预制体携带，旧敌人会被 <c>GameplayBootstrap</c> 自动补挂）。
/// 职责：
/// 1) 反向绑定 <see cref="EnemyHealth"/> / <c>EnemyHealthAdapter</c>，把旧血量组件纳入统一伤害体系；
/// 2) 向 <see cref="EnemyRegistry"/> 注册 / 注销；
/// 3) 提供种类、计分值、护甲减伤、伤害飘字配置等公共数据；
/// 4) 汇总「最后一名玩家攻击者」，供计分 / 连击归属。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAgent : MonoBehaviour, IEnemyDamageable
{
	[Header("Identity")]
	[Tooltip("敌人种类。旧蜘蛛模型默认 Spider。")]
	public EnemyKind kind = EnemyKind.Spider;

	[Tooltip("敌人定义资产（可选）。为空时使用下面的内联数值。")]
	public EnemyDefinition definition;

	[Header("Combat")]
	[Tooltip("击杀该敌人给予的分数。会被定义资产覆盖（若已配置）。")]
	public int scoreValue = 10;

	[Tooltip("护甲减伤比例 0~1。0 表示不减免。")]
	[Range(0f, 0.95f)]
	public float armor = 0f;

	[Tooltip("受击飘字配置（可选）。")]
	public DamageNumberSettings damageNumberSettings;

	[Header("Runtime")]
	[Tooltip("刷出该敌人的波次号（由 WaveDirectorEx 写入，供波次清空判定）。")]
	public int waveNumber;

	[Tooltip("随波次缩放的数值倍率（血量 / 伤害），由刷怪系统写入。")]
	public float statScale = 1f;

	/// <summary>刷出该敌人的波次号（<see cref="waveNumber"/> 的别名，供注册表与波次系统读取）。</summary>
	public int SpawnWave
	{
		get { return waveNumber; }
		set { waveNumber = value; }
	}

	/// <summary>来源血量为空的敌人（例如没有 EnemyHealth 的纯 P3B 敌人）用本地血量。</summary>
	float standaloneHealth = -1f;
	float standaloneMaxHealth = -1f;

	EnemyHealth health;
	EnemyHealthEx healthEx;
	GameObject lastPlayerAttacker;

	/// <summary>判定敌人存活的代理组件（旧敌人为 EnemyHealthAdapter，新敌人为自己或 EnemyBehaviour）。</summary>
	public EnemyHealthAdapter Adapter { get; private set; }

	/// <summary>种类（实现字段，供注册表等内部读取）。</summary>
	public EnemyKind KindValue { get { return kind; } }

	/// <summary>种类（IDamageable 契约）。</summary>
	public EnemyKind? Kind { get { return kind; } }

	/// <summary>敌人不是玩家。</summary>
	public bool IsPlayer { get { return false; } }

	/// <summary>最后一名造成伤害的玩家。</summary>
	public GameObject LastPlayerAttacker { get { return lastPlayerAttacker; } }

	/// <summary>是否已死亡。</summary>
	public bool IsDead
	{
		get
		{
			if (health != null) return health.IsDead;
			if (Adapter != null) return Adapter.IsDead;
			return standaloneHealth >= 0f && standaloneHealth <= 0f;
		}
	}

	/// <summary>当前血量。</summary>
	public float CurrentHealth
	{
		get
		{
			ResolveHealth();
			if (healthEx != null) return healthEx.CurrentHealth;
			if (Adapter != null) return Adapter.CurrentHealth;
			return Mathf.Max(0f, standaloneHealth);
		}
	}

	/// <summary>最大血量。</summary>
	public float MaxHealth
	{
		get
		{
			ResolveHealth();
			if (healthEx != null) return healthEx.MaxHealth;
			if (Adapter != null) return Adapter.MaxHealth;
			return Mathf.Max(1f, standaloneMaxHealth);
		}
	}

	/// <summary>最终计分值：优先定义资产，其次内联字段。</summary>
	public int ScoreValue
	{
		get { return definition != null ? definition.score : scoreValue; }
	}

	/// <summary>护甲减伤后实际伤害（至少 1 点穿透）。</summary>
	public float ApplyArmor(float raw)
	{
		float effectiveArmor = definition != null ? definition.armor : armor;
		if (effectiveArmor <= 0f) return raw;
		return Mathf.Max(1f, raw * (1f - Mathf.Clamp01(effectiveArmor)));
	}

	void Awake()
	{
		ResolveHealth();
	}

	void OnEnable()
	{
		EnemyRegistry.Register(this);
	}

	void OnDisable()
	{
		EnemyRegistry.Unregister(this);
	}

	void OnDestroy()
	{
		EnemyRegistry.Unregister(this);
	}

	/// <summary>绑定血量来源（EnemyHealth 适配器 / 楼面独立血量）。</summary>
	public void ResolveHealth()
	{
		if (health == null)
		{
			health = GetComponent<EnemyHealth>();
			healthEx = health as EnemyHealthEx;

			if (healthEx != null && definition != null && definition.maxHealth > 0)
			{
				int scaled = Mathf.RoundToInt(definition.maxHealth * Mathf.Max(0.01f, statScale));
				healthEx.ResetHealth(scaled);
			}
		}

		if (Adapter == null) Adapter = GetComponent<EnemyHealthAdapter>();
	}

	/// <summary>直接消耗血量（由伤害链路调用，不经过浮点接口）。</summary>
	internal void ConsumeHealth(int amount)
	{
		ResolveHealth();

		if (health != null)
		{
			health.RemoveHealth(amount);
			return;
		}
		if (Adapter != null)
		{
			Adapter.ApplyRawDamage(amount);
			return;
		}

		EnsureStandaloneHealth();
		standaloneHealth -= amount;
	}

	/// <summary>初始化独立血量（没有 EnemyHealth 的新敌人）。</summary>
	public void EnsureStandaloneHealth()
	{
		if (standaloneMaxHealth > 0f) return;
		float max = definition != null ? definition.maxHealth : 100f;
		standaloneMaxHealth = Mathf.Max(1f, max * Mathf.Max(0.01f, statScale));
		standaloneHealth = standaloneMaxHealth;
	}

	internal float StandaloneHealth
	{
		get { EnsureStandaloneHealth(); return standaloneHealth; }
		set { EnsureStandaloneHealth(); standaloneHealth = Mathf.Clamp(value, 0f, standaloneMaxHealth); }
	}

	internal float StandaloneMaxHealth
	{
		get { EnsureStandaloneHealth(); return standaloneMaxHealth; }
	}

	/// <summary>记录玩家攻击来源（供死亡计分归属）。</summary>
	public void NotePlayerAttacker(GameObject source)
	{
		var player = PlayerSource.Resolve(source);
		if (player != null) lastPlayerAttacker = player;
	}

	/// <summary>由伤害链路调用：扣血 → 飘字 → 组织击杀。</summary>
	public float ApplyDamage(DamageInfo info)
	{
		if (IsDead) return 0f;
		if (info.Amount <= 0f && !info.IsCritical) return 0f;

		NotePlayerAttacker(info.Source);

		float armorAdjusted = ApplyArmor(info.Amount);
		int rounded = GameplayMath.RoundDamage(armorAdjusted);
		if (rounded <= 0) return 0f;

		float before = CurrentHealth;
		ConsumeHealth(rounded);
		float after = CurrentHealth;
		float applied = Mathf.Max(0f, before - after);

		if (applied > 0f) GameEvents.RaiseEnemyDamaged(gameObject, applied, info);

		DamageNumberSpawner.SpawnEnemyDamage(
			info.HitPoint == Vector3.zero ? transform.position + Vector3.up : info.HitPoint,
			applied,
			info.IsCritical,
			damageNumberSettings);

		if (after <= 0f) Die(info);
		return applied;
	}

	/// <summary>结算死亡：上报给注册表 / 事件总线，然后按旧逻辑由血量组件销毁自身。</summary>
	public void Die(DamageInfo info)
	{
		if (IsDead && !gameObject.activeInHierarchy) return;

		var killer = info.FromPlayer ? PlayerSource.Resolve(info.Source) : null;
		if (killer == null) killer = lastPlayerAttacker;

		EnemyRegistry.NotifyKilled(this, killer, transform.position, ScoreValue);

		// 旧 EnemyHealth 在 RemoveHealth 里自己销毁；独立血量的敌人在此销毁避免残留。
		if (health == null && Adapter == null && gameObject.activeInHierarchy)
			Destroy(gameObject, 0.01f);
	}

	/// <summary>外部强制击杀（自爆 / 清场 / 调试）。</summary>
	public void Kill(DamageInfo info)
	{
		if (IsDead) return;

		if (health != null) health.RemoveHealth(Mathf.CeilToInt(CurrentHealth + 1));
		else if (Adapter != null) Adapter.ApplyRawDamage(Mathf.CeilToInt(CurrentHealth + 1));
		else
		{
			StandaloneHealth = 0f;
			Die(info);
		}
	}

	/// <summary>所有存活敌人快照（转发注册表）。</summary>
	public static List<EnemyAgent> All()
	{
		return EnemyRegistry.GetAlive();
	}
}