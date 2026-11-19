using System;
using UnityEngine;

namespace ABGameplay.Campaign
{
	public enum UpgradeKind
	{
		WeaponDamage = 0,
		AmmoCapacity = 1,
		ArmorHealth = 2,
		PowerUpDrop = 3,
	}

	/// <summary>
	/// 商店升级：伤害 / 弹匣容量 / 最大生命。等级存进存档，进入关卡时自动套用到玩家。
	/// 数值以原工程基线为基准，避免与 WeaponSystem 的序列化值互相覆盖。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class UpgradeSystem : MonoBehaviour
	{
		public static UpgradeSystem Instance { get; private set; }

		[Header("Limits")]
		public int maxLevel = 5;

		[Header("Costs")]
		public int baseDamageCost = 120;
		public int baseAmmoCost = 100;
		public int baseHealthCost = 140;
		public int basePowerUpCost = 160;

		[Header("Per-level Gains")]
		public int rifleDamagePerLevel = 6;
		public int shotgunDamagePerLevel = 4;
		public int smgDamagePerLevel = 2;
		public int rifleMagPerLevel = 6;
		public int shotgunMagPerLevel = 2;
		public int smgMagPerLevel = 8;
		public int healthPerLevel = 25;

		[Tooltip("每级提升的增益掉落概率（加法，0.05 = +5%）。")]
		public float powerUpDropPerLevel = 0.05f;

		[Header("Baselines")]
		public int baseRifleDamage = 25;
		public int baseShotgunDamage = 12;
		public int baseSmgDamage = 9;
		public int baseRifleMag = 30;
		public int baseShotgunMag = 8;
		public int baseSmgMag = 40;
		public int baseMaxHealth = 100;

		public event Action Changed;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		void Start()
		{
			ApplyToPlayer();
		}

		public int GetLevel(UpgradeKind kind)
		{
			var save = SaveSystem.Data;
			switch (kind)
			{
				case UpgradeKind.WeaponDamage: return save.damageLevel;
				case UpgradeKind.AmmoCapacity: return save.ammoLevel;
				case UpgradeKind.PowerUpDrop: return save.powerUpLevel;
				default: return save.healthLevel;
			}
		}

		/// <summary>增益掉落概率的永久加成（0~maxLevel × powerUpDropPerLevel）。</summary>
		public float PowerUpDropBonus
		{
			get { return PowerUpDropBonusForLevel(GetLevel(UpgradeKind.PowerUpDrop)); }
		}

		/// <summary>指定等级对应的掉落加成（供自检直接调用，不依赖存档）。</summary>
		public float PowerUpDropBonusForLevel(int level)
		{
			return Mathf.Clamp(level, 0, maxLevel) * Mathf.Max(0f, powerUpDropPerLevel);
		}

		public int GetCost(UpgradeKind kind)
		{
			int level = Mathf.Clamp(GetLevel(kind), 0, maxLevel);
			int baseCost;
			switch (kind)
			{
				case UpgradeKind.WeaponDamage: baseCost = baseDamageCost; break;
				case UpgradeKind.AmmoCapacity: baseCost = baseAmmoCost; break;
				case UpgradeKind.PowerUpDrop: baseCost = basePowerUpCost; break;
				default: baseCost = baseHealthCost; break;
			}
			return baseCost * (level + 1);
		}

		public bool IsMaxed(UpgradeKind kind)
		{
			return GetLevel(kind) >= maxLevel;
		}

		public bool CanBuy(UpgradeKind kind)
		{
			if (IsMaxed(kind)) return false;
			var score = ScoreSystem.Instance != null ? ScoreSystem.Instance : FindObjectOfType<ScoreSystem>();
			return score != null && score.CanAfford(GetCost(kind));
		}

		/// <summary>购买一级。成功返回 true。</summary>
		public bool Buy(UpgradeKind kind)
		{
			if (IsMaxed(kind)) return false;

			var score = ScoreSystem.Instance != null ? ScoreSystem.Instance : FindObjectOfType<ScoreSystem>();
			if (score == null) return false;

			int cost = GetCost(kind);
			if (!score.TrySpend(cost)) return false;

			var save = SaveSystem.Data;
			switch (kind)
			{
				case UpgradeKind.WeaponDamage: save.damageLevel++; break;
				case UpgradeKind.AmmoCapacity: save.ammoLevel++; break;
				case UpgradeKind.PowerUpDrop: save.powerUpLevel++; break;
				default: save.healthLevel++; break;
			}
			SaveSystem.MarkDirty();
			SaveSystem.Save();

			ApplyToPlayer();
			if (Changed != null) Changed();
			return true;
		}

		public void ApplyToPlayer()
		{
			var player = PlayerSource.Player;
			if (player == null)
			{
				var health = FindObjectOfType<PlayerHealth>();
				if (health != null) player = health.transform;
			}
			if (player == null) return;

			int damageLevel = Mathf.Clamp(SaveSystem.Data.damageLevel, 0, maxLevel);
			int ammoLevel = Mathf.Clamp(SaveSystem.Data.ammoLevel, 0, maxLevel);
			int healthLevel = Mathf.Clamp(SaveSystem.Data.healthLevel, 0, maxLevel);

			var weapons = player.GetComponent<WeaponSystem>();
			if (weapons != null)
			{
				weapons.rifleDamage = baseRifleDamage + damageLevel * rifleDamagePerLevel;
				weapons.shotgunDamage = baseShotgunDamage + damageLevel * shotgunDamagePerLevel;
				weapons.smgDamage = baseSmgDamage + damageLevel * smgDamagePerLevel;
				weapons.rifleMagSize = baseRifleMag + ammoLevel * rifleMagPerLevel;
				weapons.shotgunMagSize = baseShotgunMag + ammoLevel * shotgunMagPerLevel;
				weapons.smgMagSize = baseSmgMag + ammoLevel * smgMagPerLevel;
			}

			var playerHealth = player.GetComponent<PlayerHealth>();
			if (playerHealth != null)
			{
				int target = baseMaxHealth + healthLevel * healthPerLevel;
				if (target > playerHealth.maxHealth)
				{
					int delta = target - playerHealth.maxHealth;
					playerHealth.maxHealth = target;
					playerHealth.Heal(delta);
				}
			}
		}

		public static UpgradeSystem EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<UpgradeSystem>();
			if (existing != null) return existing;
			var go = new GameObject("[UpgradeSystem]");
			return go.AddComponent<UpgradeSystem>();
		}
	}
}