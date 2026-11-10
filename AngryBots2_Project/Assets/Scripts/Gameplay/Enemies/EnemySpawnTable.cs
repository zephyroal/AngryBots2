using System;
using System.Collections.Generic;
using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// 单个敌人的生成权重项。
	/// </summary>
	[Serializable]
	public sealed class EnemySpawnWeight
	{
		[Tooltip("敌人定义（决定种类、血量、移动速度、Prefab）。")]
		public EnemyDefinition definition;

		[Tooltip("权重（相对值）。0 表示该波不刷这种敌人。")]
		[Min(0f)]
		public float weight = 1f;

		[Tooltip("该敌人最小出现波次。0 表示第 1 波就会出现。")]
		public int minWave = 1;

		[Tooltip("该敌人最大出现波次（0 = 不限）。")]
		public int maxWave = 0;

		/// <summary>在给定波次是否可选。</summary>
		public bool IsAvailable(int wave)
		{
			if (definition == null || weight <= 0f) return false;
			if (wave < minWave) return false;
			if (maxWave > 0 && wave > maxWave) return false;
			return true;
		}
	}

	/// <summary>
	/// 一波的编排：敌人组成、数量曲线、生成节奏。
	/// </summary>
	[Serializable]
	public sealed class WaveTemplate
	{
		[Tooltip("该波敌人总数（会再叠加难度曲线的数量加成）。")]
		[Min(1)]
		public int baseCount = 4;

		[Tooltip("每波递增的敌人数量。")]
		public int countPerWave = 1;

		[Tooltip("本波可用的敌人种类与权重。为空则使用 SpawnTable 的全局权重表。")]
		public List<EnemySpawnWeight> composition = new List<EnemySpawnWeight>();

		[Tooltip("是否为本波的 Boss 波（覆盖 SpawnTable 的每 N 波 Boss 规则）。")]
		public bool forceBossWave;

		[Tooltip("Boss 波使用的敌人定义。")]
		public EnemyDefinition bossDefinition;

		[Tooltip("本波限时（秒）。0 表示使用 SpawnTable 的默认限时。")]
		public float timeLimitOverride;

		[Tooltip("本波生成间隔。0 表示使用 SpawnTable 的默认值。")]
		public float spawnIntervalOverride;
	}

	/// <summary>
	/// 生成点分组：不同区域 / 不同类型的刷怪点。
	/// </summary>
	[Serializable]
	public sealed class SpawnGroup
	{
		[Tooltip("分组名（日志 / 调试用）。")]
		public string name = "Group A";

		[Tooltip("分组内的刷怪点。")]
		public Transform[] points;

		[Tooltip("同时最多存活多少来自该组的敌人（0 = 不限）。")]
		public int maxConcurrent = 0;

		[Tooltip("该组的可用权重（在波次组成之上再做一次权重筛选）。")]
		[Range(0f, 1f)]
		public float availability = 1f;

		/// <summary>随机取一个刷怪点。</summary>
		public Transform PickPoint()
		{
			if (points == null || points.Length == 0) return null;

			for (int attempt = 0; attempt < 4; attempt++)
			{
				var candidate = points[UnityEngine.Random.Range(0, points.Length)];
				if (candidate != null) return candidate;
			}
			return null;
		}

		/// <summary>分组内有效点数。</summary>
		public int ValidPointCount
		{
			get
			{
				if (points == null) return 0;
				int count = 0;
				for (int i = 0; i < points.Length; i++)
					if (points[i] != null) count++;
				return count;
			}
		}
	}

	/// <summary>
	/// 敌人刷怪表（ScriptableObject）：按关卡配置波次的敌人种类与权重、生成节奏、难度曲线。
	/// <para>
	/// 供 <see cref="WaveDirectorEx"/> 读取。没有配置资产时，<see cref="WaveDirectorEx"/> 会
	/// 使用内置默认表（<see cref="CreateRuntimeDefault"/>），保证开箱即可运行。
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "EnemySpawnTable_", menuName = "ABGameplay/Enemy Spawn Table", order = 12)]
	public sealed class EnemySpawnTable : ScriptableObject
	{
		[Header("Global Composition")]
		[Tooltip("全局敌人权重表：当某波没有自己的组成时使用。")]
		public List<EnemySpawnWeight> composition = new List<EnemySpawnWeight>();

		[Header("Wave Structure")]
		[Tooltip("总波数（0 = 无限波）。")]
		public int totalWaves = 10;

		[Tooltip("每 N 波出现一个 Boss 波（0 = 关闭）。")]
		public int bossEveryNWaves = 5;

		[Tooltip("Boss 波使用的敌人定义。")]
		public EnemyDefinition bossDefinition;

		[Header("Pacing")]
		[Tooltip("每波限时（秒）。")]
		public float waveTimeLimit = 90f;

		[Tooltip("波与波之间的间隔（秒）。")]
		public float waveDelay = 4f;

		[Tooltip("波内逐个敌人的生成间隔（秒）。")]
		public float spawnInterval = 0.45f;

		[Tooltip("开局到第一波的延迟（秒）。")]
		public float firstWaveDelay = 1.5f;

		[Header("Difficulty Curve")]
		[Tooltip("血量随波次的线性增长（每波 +X%）。")]
		public float healthGrowthPerWave = 0.12f;

		[Tooltip("伤害随波次的线性增长（每波 +X%）。")]
		public float damageGrowthPerWave = 0.06f;

		[Tooltip("移动速度随波次的线性增长（每波 +X%，上限见 MaxSpeedScale）。")]
		public float speedGrowthPerWave = 0.02f;

		[Tooltip("速度缩放上限。")]
		public float maxSpeedScale = 1.5f;

		[Header("Rewards")]
		[Tooltip("每波结束给玩家的手雷补给。")]
		public int grenadeRefillPerWave = 1;

		[Tooltip("每波结束给玩家的血量回复。")]
		public int healPerWave = 15;

		[Tooltip("每波开场给玩家的无敌时间（秒）。")]
		public float spawnProtectionTime = 1.5f;

		/// <summary>指定波次是否 Boss 波。</summary>
		public bool IsBossWave(int wave)
		{
			if (bossEveryNWaves <= 0) return false;
			if (bossDefinition == null) return false;
			return wave > 0 && wave % bossEveryNWaves == 0;
		}

		/// <summary>某一波的血量缩放。</summary>
		public float GetHealthScale(int wave)
		{
			return 1f + Mathf.Max(0, wave - 1) * Mathf.Max(0f, healthGrowthPerWave);
		}

		/// <summary>某一波的伤害缩放。</summary>
		public float GetDamageScale(int wave)
		{
			return 1f + Mathf.Max(0, wave - 1) * Mathf.Max(0f, damageGrowthPerWave);
		}

		/// <summary>某一波的移速缩放（有上限）。</summary>
		public float GetSpeedScale(int wave)
		{
			float raw = 1f + Mathf.Max(0, wave - 1) * Mathf.Max(0f, speedGrowthPerWave);
			return Mathf.Min(raw, Mathf.Max(1f, maxSpeedScale));
		}

		/// <summary>某一波的敌人数量。</summary>
		public int GetWaveCount(int wave, WaveTemplate template)
		{
			int baseCount = template != null ? Mathf.Max(1, template.baseCount) : 4;
			int perWave = template != null ? template.countPerWave : 1;
			return Mathf.Max(1, baseCount + Mathf.Max(0, wave - 1) * Mathf.Max(0, perWave));
		}

		/// <summary>按权重为某一波抽取一个敌人定义。</summary>
		public EnemyDefinition PickEnemy(int wave, WaveTemplate template, System.Random rng)
		{
			var pool = template != null && template.composition != null && template.composition.Count > 0
				? template.composition
				: composition;

			if (pool == null || pool.Count == 0) return null;

			float total = 0f;
			for (int i = 0; i < pool.Count; i++)
				if (pool[i] != null && pool[i].IsAvailable(wave)) total += pool[i].weight;

			if (total <= 0f)
			{
				// 没有可用项：退化到第一个有定义的项，保证刷怪不空转。
				for (int i = 0; i < pool.Count; i++)
					if (pool[i] != null && pool[i].definition != null) return pool[i].definition;
				return null;
			}

			float pick = (float)(rng != null ? rng.NextDouble() : UnityEngine.Random.value) * total;
			for (int i = 0; i < pool.Count; i++)
			{
				if (pool[i] == null || !pool[i].IsAvailable(wave)) continue;
				pick -= pool[i].weight;
				if (pick <= 0f) return pool[i].definition;
			}

			return pool[pool.Count - 1] != null ? pool[pool.Count - 1].definition : null;
		}

		/// <summary>
		/// 生成一份默认刷怪表（运行时内存资产，不落盘）。
		/// 用于没有美术/配置资产的场景（batchmode 自检、临时关卡）。
		/// </summary>
		public static EnemySpawnTable CreateRuntimeDefault(GameObject spiderPrefab = null)
		{
			var table = CreateInstance<EnemySpawnTable>();
			table.name = "EnemySpawnTable(Default)";
			table.totalWaves = 10;
			table.bossEveryNWaves = 5;

			table.composition = new List<EnemySpawnWeight>
			{
				MakeWeight(EnemyKind.Spider, 3f, 1),
				MakeWeight(EnemyKind.Kamikaze, 2f, 2),
				MakeWeight(EnemyKind.Buzzer, 1.5f, 3),
				MakeWeight(EnemyKind.Mech, 1f, 3),
			};

			table.bossDefinition = MakeDefinition(EnemyKind.Boss);

			if (spiderPrefab != null)
			{
				var spider = table.composition[0];
				if (spider.definition != null) spider.definition.prefab = spiderPrefab;
			}

			return table;
		}

		static EnemySpawnWeight MakeWeight(EnemyKind kind, float weight, int minWave)
		{
			return new EnemySpawnWeight
			{
				definition = MakeDefinition(kind),
				weight = weight,
				minWave = minWave,
			};
		}

		static EnemyDefinition MakeDefinition(EnemyKind kind)
		{
			var definition = EnemyDefinition.CreateRuntimeDefault(kind);
			return definition;
		}
	}
}