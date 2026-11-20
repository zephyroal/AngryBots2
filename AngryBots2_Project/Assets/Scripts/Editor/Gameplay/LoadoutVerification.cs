using UnityEditor;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 武器/掉落自检（纯 Edit 模式）：第四把武器（狙击）的枚举、数值与备弹；精英词缀掉落加成。
	/// 用法：-executeMethod ABGameplay.LoadoutVerification.Run
	/// </summary>
	public static class LoadoutVerification
	{
		static int fails;

		[MenuItem("ABGameplay/22. Loadout Verification")]
		public static void Run()
		{
			fails = 0;

			Ok((int)WeaponSystem.WeaponMode.Sniper == 3, "Sniper 枚举值 = 3（不与既有武器冲突）");

			var host = new GameObject("LoadoutProbePlayer");
			try
			{
				var weapons = host.AddComponent<WeaponSystem>();

				Ok(weapons.sniperDamage > weapons.rifleDamage * 2, "狙击单发伤害 = " + weapons.sniperDamage);
				Ok(weapons.sniperInterval > weapons.rifleInterval * 3, "狙击射击间隔 = " + weapons.sniperInterval + "s（更慢）");
				Ok(weapons.sniperMagSize <= 10, "狙击弹匣 = " + weapons.sniperMagSize);

				weapons.SwitchTo(WeaponSystem.WeaponMode.Sniper);
				Ok(weapons.Mode == WeaponSystem.WeaponMode.Sniper, "切换到狙击成功");
				Ok(weapons.ModeName == "SNIPER", "武器名为 SNIPER");
				Ok(weapons.CurrentMagSize == weapons.sniperMagSize, "当前弹匣上限 = " + weapons.CurrentMagSize);
				Ok(weapons.CurrentMag == weapons.sniperMagSize, "初始弹匣 = " + weapons.CurrentMag);
				Ok(weapons.CurrentReserve == weapons.sniperMagSize * 4, "初始备弹 = " + weapons.CurrentReserve);

				int before = weapons.CurrentReserve;
				int added = weapons.AddReserve(3);
				Ok(added == 3 && weapons.CurrentReserve == before + 3, "狙击备弹可补给 = " + weapons.CurrentReserve);

				weapons.SwitchTo(WeaponSystem.WeaponMode.Rifle);
				Ok(weapons.ModeName == "RIFLE", "切回步枪");

				Ok((int)WeaponSystem.WeaponMode.Launcher == 4, "Launcher 枚举值 = 4");
				weapons.SwitchTo(WeaponSystem.WeaponMode.Launcher);
				Ok(weapons.ModeName == "LAUNCHER", "武器名为 LAUNCHER");
				Ok(weapons.CurrentMagSize == weapons.launcherMagSize, "榴弹弹匣上限 = " + weapons.CurrentMagSize);
				Ok(weapons.CurrentMag == weapons.launcherMagSize, "榴弹初始弹匣 = " + weapons.CurrentMag);
				Ok(weapons.launcherSpeed > 10f, "榴弹初速 = " + weapons.launcherSpeed);

				Ok(weapons.sniperPierce >= 1, "狙击穿透数 = " + weapons.sniperPierce);
				Ok(weapons.sniperCriticalMultiplier > 1f, "狙击爆头倍率 = " + weapons.sniperCriticalMultiplier);

				// 爆头纯函数
				Ok(ProjectileBehaviour.ApplyCritical(90, 1.5f, 1.0f, 0.35f, 2f) == 180, "爆头伤害翻倍 = 180");
				Ok(ProjectileBehaviour.ApplyCritical(90, 1.2f, 1.0f, 0.35f, 2f) == 90, "未达高度阈值不翻倍");
				Ok(ProjectileBehaviour.ApplyCritical(90, 1.5f, 1.0f, 0.35f, 1f) == 90, "倍率 1 时视为关闭爆头");
				weapons.SwitchTo(WeaponSystem.WeaponMode.Rifle);
			}
			finally
			{
				Object.DestroyImmediate(host);
			}

			var affixHost = new GameObject("LoadoutProbeElite");
			try
			{
				var affix = affixHost.AddComponent<EliteAffixHost>();
				Ok(affix.bonusDropChance > 0f, "精英词缀额外掉落概率 = " + affix.bonusDropChance.ToString("0.00"));
			}
			finally
			{
				Object.DestroyImmediate(affixHost);
			}

			Debug.Log("[Loadout] DONE fails=" + fails);
			EditorApplication.Exit(fails == 0 ? 0 : 1);
		}

		static void Ok(bool condition, string message)
		{
			if (condition) Debug.Log("[Loadout] PASS " + message);
			else { fails++; Debug.LogError("[Loadout] FAIL " + message); }
		}
	}
}
