using UnityEditor;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 临时增益系统自检（纯 Edit 模式）：倍率、计时、拾取物接线、掉落概率。
	/// 用法：-executeMethod ABGameplay.PowerUpVerification.Run
	/// </summary>
	public static class PowerUpVerification
	{
		static int fails;

		[MenuItem("ABGameplay/18. PowerUp Verification")]
		public static void Run()
		{
			fails = 0;

			var player = new GameObject("PowerUpProbePlayer");
			try
			{
				var powerUps = player.AddComponent<PowerUpSystem>();

				Ok(!powerUps.AnyActive, "初始无增益");
				Ok(Mathf.Approximately(powerUps.DamageMultiplier, 1f), "初始伤害倍率 = 1");
				Ok(Mathf.Approximately(powerUps.FireIntervalMultiplier, 1f), "初始射速间隔倍率 = 1");
				Ok(Mathf.Approximately(powerUps.IncomingDamageMultiplier, 1f), "初始受击倍率 = 1");

				powerUps.Activate(PowerUpKind.DamageBoost);
				Ok(powerUps.IsActive(PowerUpKind.DamageBoost), "伤害强化激活");
				Ok(powerUps.DamageMultiplier > 1.5f, "伤害强化倍率 = " + powerUps.DamageMultiplier.ToString("0.00"));
				Ok(powerUps.Remaining(PowerUpKind.DamageBoost) > 11f, "伤害强化计时 = " + powerUps.Remaining(PowerUpKind.DamageBoost).ToString("0.0") + "s");

				powerUps.Activate(PowerUpKind.RapidFire);
				Ok(powerUps.FireIntervalMultiplier < 0.6f, "射速间隔倍率 = " + powerUps.FireIntervalMultiplier.ToString("0.00"));

				powerUps.Activate(PowerUpKind.Shield);
				Ok(powerUps.IncomingDamageMultiplier < 0.4f, "护盾受击倍率 = " + powerUps.IncomingDamageMultiplier.ToString("0.00"));

				// 同类重复拾取不应缩短剩余时间
				float before = powerUps.Remaining(PowerUpKind.DamageBoost);
				powerUps.Activate(PowerUpKind.DamageBoost, 1f);
				Ok(powerUps.Remaining(PowerUpKind.DamageBoost) >= before, "重复拾取不缩短计时");

				powerUps.Advance(999f);
				Ok(!powerUps.AnyActive, "长时间推进后全部到期");
				Ok(Mathf.Approximately(powerUps.DamageMultiplier, 1f)
					&& Mathf.Approximately(powerUps.FireIntervalMultiplier, 1f)
					&& Mathf.Approximately(powerUps.IncomingDamageMultiplier, 1f), "到期后倍率回到 1");

				var same = PowerUpSystem.Ensure(player);
				Ok(ReferenceEquals(same, powerUps), "PowerUpSystem.Ensure 幂等");

				CheckPickupWiring();
				CheckDropperChances();
				CheckUpgradeBonus();
				CheckDropTable();
			}
			finally
			{
				Object.DestroyImmediate(player);
			}

			Debug.Log("[PowerUp] DONE fails=" + fails);
			EditorApplication.Exit(fails == 0 ? 0 : 1);
		}

		static void CheckPickupWiring()
		{
			foreach (PowerUpKind kind in new[] { PowerUpKind.DamageBoost, PowerUpKind.RapidFire, PowerUpKind.Shield })
			{
				var pickup = SimplePickup.CreatePowerUp(kind, new Vector3(0f, -500f, 0f), 0f);
				Ok(pickup != null, kind + " 拾取物生成");
				if (pickup == null) continue;

				Ok(pickup.type == SimplePickup.PickupType.PowerUp, kind + " 拾取物类型 = PowerUp");
				Ok(pickup.powerUpKind == kind, kind + " 拾取物携带正确种类");
				Ok(pickup.name.Contains(kind.ToString()), kind + " 拾取物命名包含种类");
				Object.DestroyImmediate(pickup.gameObject);
			}
		}

		static void CheckDropperChances()
		{
			var host = new GameObject("PowerUpDropperProbe");
			try
			{
				var dropper = host.AddComponent<PowerUpDropper>();
				Ok(dropper.ChanceFor(EnemyKind.Boss) >= 1f, "Boss 掉落概率 = " + dropper.ChanceFor(EnemyKind.Boss));
				Ok(dropper.ChanceFor(EnemyKind.Spider) > 0f, "蜘蛛掉落概率 = " + dropper.ChanceFor(EnemyKind.Spider));
				Ok(dropper.ChanceFor(EnemyKind.Mech) >= dropper.ChanceFor(EnemyKind.Spider), "重型单位掉落概率不低于蜘蛛");
				Ok(dropper.ChanceFor(EnemyKind.Spider, 5) > dropper.ChanceFor(EnemyKind.Spider, 1), "波次越高掉落概率越高（1→5 波）");
				Ok(dropper.ChanceFor(EnemyKind.Boss, 8) >= 1f, "Boss 掉落概率不随波次衰减");
				Ok(dropper.ChanceFor(EnemyKind.Spider, 99) <= dropper.maxChance + 0.0001f,
					"掉落概率受上限约束 = " + dropper.ChanceFor(EnemyKind.Spider, 99).ToString("0.00"));
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		static void CheckUpgradeBonus()
		{
			var host = new GameObject("UpgradeProbe");
			try
			{
				var upgrades = host.AddComponent<ABGameplay.Campaign.UpgradeSystem>();
				Ok(Mathf.Approximately(upgrades.PowerUpDropBonusForLevel(0), 0f), "0 级无掉落加成");
				Ok(upgrades.PowerUpDropBonusForLevel(4) > upgrades.PowerUpDropBonusForLevel(2), "等级越高掉落加成越高");
				Ok(Mathf.Approximately(upgrades.PowerUpDropBonusForLevel(99), upgrades.maxLevel * upgrades.powerUpDropPerLevel),
					"掉落加成受最高等级约束 = " + upgrades.PowerUpDropBonusForLevel(99).ToString("0.00"));
				Ok(upgrades.GetCost(ABGameplay.Campaign.UpgradeKind.PowerUpDrop) > 0,
					"增益掉落升级价格 = " + upgrades.GetCost(ABGameplay.Campaign.UpgradeKind.PowerUpDrop));
				Ok((int)ABGameplay.Campaign.UpgradeKind.PowerUpDrop == 3, "PowerUpDrop 枚举值 = 3（不与既有升级冲突）");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		static void CheckDropTable()
		{
			var table = ScriptableObject.CreateInstance<PowerUpDropTable>();
			try
			{
				table.rows.Add(new PowerUpDropTable.Row { match = "boss", damageWeight = 0.5f, rapidWeight = 1f, shieldWeight = 3f });
				table.rows.Add(new PowerUpDropTable.Row { match = "7", damageWeight = 3f, rapidWeight = 0.5f, shieldWeight = 0.5f });

				var bossWeights = table.Resolve("Boss Rush", "Scenes/Boss", 5);
				Ok(bossWeights[2] > bossWeights[0], "Boss 关权重偏向护盾 = " + bossWeights[2].ToString("0.0") + " vs " + bossWeights[0].ToString("0.0"));

				var indexWeights = table.Resolve("Normal", "Scenes/AngryBots", 7);
				Ok(indexWeights[0] > indexWeights[1], "纯数字 match 按关卡序号命中（7）");

				var fallback = table.Resolve("Unknown", "Scenes/Default", 1);
				Ok(Mathf.Approximately(fallback[0], 1f) && Mathf.Approximately(fallback[1], 1f) && Mathf.Approximately(fallback[2], 1f),
					"未命中任何行时使用默认等概率权重");

				Ok(PowerUpDropTable.PickKind(new[] { 0f, 0f, 5f }, 0.99f) == PowerUpKind.Shield, "权重只有护盾时必出护盾");
				Ok(PowerUpDropTable.PickKind(new[] { 1f, 1f, 1f }, 0f) == PowerUpKind.DamageBoost, "等概率权重 roll=0 出伤害强化");
				Ok(PowerUpDropTable.PickKind(new[] { 1f, 1f, 1f }, 0.99f) == PowerUpKind.Shield, "等概率权重 roll=0.99 出护盾");

				var host = new GameObject("DropTableProbe");
				try
				{
					var dropper = host.AddComponent<PowerUpDropper>();
					dropper.dropTable = table;
					Ok(dropper.PickKind(0.99f) == PowerUpKind.Shield, "掉落器按表格权重取增益（无战役上下文时用默认权重）");
				}
				finally
				{
					Object.DestroyImmediate(host);
				}
			}
			finally
			{
				Object.DestroyImmediate(table);
			}
		}

		static void Ok(bool condition, string message)
		{
			if (condition) Debug.Log("[PowerUp] PASS " + message);
			else { fails++; Debug.LogError("[PowerUp] FAIL " + message); }
		}
	}
}
