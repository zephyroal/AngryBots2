using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>增益掉落权重表：按关卡名 / 场景名 / 关卡序号匹配一组权重。</summary>
[CreateAssetMenu(menuName = "AngryBots/PowerUp Drop Table", fileName = "PowerUpDropTable")]
public sealed class PowerUpDropTable : ScriptableObject
{
	[Serializable]
	public sealed class Row
	{
		[Tooltip("匹配键：关卡显示名、场景名，或纯数字表示关卡序号（不区分大小写，包含即命中）。")]
		public string match = "";

		[Tooltip("伤害强化的权重。")]
		public float damageWeight = 1f;

		[Tooltip("快速射击的权重。")]
		public float rapidWeight = 1f;

		[Tooltip("护盾的权重。")]
		public float shieldWeight = 1f;
	}

	[Tooltip("按顺序匹配，第一条命中的行生效。")]
	public List<Row> rows = new List<Row>();

	[Tooltip("没有命中任何行时使用的默认权重（伤害 / 射速 / 护盾）。")]
	public float[] defaultWeights = { 1f, 1f, 1f };

	/// <summary>解析当前关卡的权重；未命中返回默认权重。</summary>
	public float[] Resolve(string missionName, string sceneName, int missionIndex)
	{
		for (int i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			if (row == null) continue;
			if (!Matches(row.match, missionName, sceneName, missionIndex)) continue;
			return new[] { Mathf.Max(0f, row.damageWeight), Mathf.Max(0f, row.rapidWeight), Mathf.Max(0f, row.shieldWeight) };
		}

		if (defaultWeights == null || defaultWeights.Length < PowerUpSystem.KindCount)
			return new[] { 1f, 1f, 1f };

		return new[]
		{
			Mathf.Max(0f, defaultWeights[0]),
			Mathf.Max(0f, defaultWeights[1]),
			Mathf.Max(0f, defaultWeights[2]),
		};
	}

	static bool Matches(string key, string missionName, string sceneName, int missionIndex)
	{
		if (string.IsNullOrEmpty(key)) return false;
		string trimmed = key.Trim();
		if (trimmed.Length == 0) return false;

		if (int.TryParse(trimmed, out int index)) return index == missionIndex;

		if (!string.IsNullOrEmpty(missionName) && missionName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (!string.IsNullOrEmpty(sceneName) && sceneName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	/// <summary>按权重与 [0,1) 随机数选一种增益（纯函数，便于自检）。</summary>
	public static PowerUpKind PickKind(float[] weights, float roll01)
	{
		if (weights == null || weights.Length < PowerUpSystem.KindCount) return PowerUpKind.DamageBoost;

		float total = 0f;
		for (int i = 0; i < PowerUpSystem.KindCount; i++) total += Mathf.Max(0f, weights[i]);
		if (total <= 0f) return PowerUpKind.DamageBoost;

		float target = Mathf.Clamp01(roll01) * total;
		float accumulated = 0f;
		for (int i = 0; i < PowerUpSystem.KindCount; i++)
		{
			accumulated += Mathf.Max(0f, weights[i]);
			if (target < accumulated) return (PowerUpKind)i;
		}
		return (PowerUpKind)(PowerUpSystem.KindCount - 1);
	}
}
