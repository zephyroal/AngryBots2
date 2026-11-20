using UnityEditor;
using UnityEngine;

namespace ABGameplay
{
	/// <summary>
	/// 精英词缀自检（纯 Edit 模式）：权重分布、爆炸伤害衰减、词缀挂载幂等。
	/// 用法：-executeMethod ABGameplay.EliteAffixVerification.Run
	/// </summary>
	public static class EliteAffixVerification
	{
		static int fails;

		[MenuItem("ABGameplay/21. Elite Affix Verification")]
		public static void Run()
		{
			fails = 0;

			// 权重边界：40% 自爆 / 30% 迅捷 / 30% 铁壁
			Ok(EliteAffixHost.PickAffix(0f) == EliteAffix.Explosive, "roll=0 → 自爆");
			Ok(EliteAffixHost.PickAffix(0.39f) == EliteAffix.Explosive, "roll=0.39 → 自爆");
			Ok(EliteAffixHost.PickAffix(0.4f) == EliteAffix.Swift, "roll=0.40 → 迅捷");
			Ok(EliteAffixHost.PickAffix(0.69f) == EliteAffix.Swift, "roll=0.69 → 迅捷");
			Ok(EliteAffixHost.PickAffix(0.7f) == EliteAffix.Tank, "roll=0.70 → 铁壁");
			Ok(EliteAffixHost.PickAffix(1f) == EliteAffix.Tank, "roll=1.0 → 铁壁");

			Ok(EliteAffixHost.Label(EliteAffix.Explosive) == "Explosive", "自爆名称");
			Ok(EliteAffixHost.Label(EliteAffix.Swift) == "Swift", "迅捷名称");
			Ok(EliteAffixHost.Label(EliteAffix.Tank) == "Tank", "铁壁名称");

			// 爆炸伤害：爆心满伤、边缘约 35%、半径外为 0
			Ok(EliteAffixHost.ExplosionDamage(22, 0f, 3.5f) == 22, "爆心伤害 = 22");
			int edge = EliteAffixHost.ExplosionDamage(22, 3.5f, 3.5f);
			Ok(edge >= 7 && edge <= 8, "边缘伤害衰减到 35% = " + edge);
			Ok(EliteAffixHost.ExplosionDamage(22, 5f, 3.5f) == 0, "半径外不受伤");
			Ok(EliteAffixHost.ExplosionDamage(0, 0f, 3.5f) == 0, "基础伤害 0 时不出伤");

			// 挂载幂等
			var probe = new GameObject("AffixProbeEnemy");
			try
			{
				var first = EliteAffixHost.Attach(probe, EliteAffix.Tank);
				var second = EliteAffixHost.Attach(probe, EliteAffix.Tank);
				Ok(first != null && ReferenceEquals(first, second), "重复挂载返回同一组件");
				Ok(probe.GetComponent<EliteAffixHost>() != null, "组件已挂到敌人身上");
				Ok(probe.name.Contains("Tank"), "对象名带上词缀标记 = " + probe.name);
			}
			finally
			{
				Object.DestroyImmediate(probe);
			}

			Debug.Log("[EliteAffix] DONE fails=" + fails);
			EditorApplication.Exit(fails == 0 ? 0 : 1);
		}

		static void Ok(bool condition, string message)
		{
			if (condition) Debug.Log("[EliteAffix] PASS " + message);
			else { fails++; Debug.LogError("[EliteAffix] FAIL " + message); }
		}
	}
}
