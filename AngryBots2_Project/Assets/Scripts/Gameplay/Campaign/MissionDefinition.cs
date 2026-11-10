using System;
using System.Collections.Generic;
using ABGameplay.Enemies;
using UnityEngine;

namespace ABGameplay.Campaign
{
	/// <summary>关卡目标类型。</summary>
	public enum ObjectiveMode
	{
		/// <summary>清场：按波次刷怪，清完全部波次即胜利。</summary>
		ClearWaves = 0,

		/// <summary>护送：NPC 抵达终点；玩家必须跟随，脱离太久失败。</summary>
		Escort = 1,

		/// <summary>防守：在指定区域内坚持 N 秒；玩家离开过久失败。</summary>
		Defend = 2,

		/// <summary>限时：在时限内达到击杀数。</summary>
		Timed = 3,
	}

	/// <summary>单个战役关卡的定义（可资产化，也可运行时用默认列表）。</summary>
	[CreateAssetMenu(fileName = "Mission_", menuName = "ABGameplay/Mission Definition", order = 20)]
	public sealed class MissionDefinition : ScriptableObject
	{
		[Header("Identity")]
		public int index;
		public string displayName = "Mission";
		public string sceneName = "";
		public ObjectiveMode mode = ObjectiveMode.ClearWaves;

		[TextArea(2, 5)]
		public string briefing = "";

		[Header("Waves")]
		[Tooltip("波次总数（清场模式为通关条件，其它模式仅作为难度节奏）。")]
		public int totalWaves = 5;

		[Header("Objective")]
		[Tooltip("限时模式的目标击杀数。")]
		public int targetKills = 20;

		[Tooltip("限时模式 / 防守模式（可选总时限）。0 = 不限制。")]
		public float timeLimit = 180f;

		[Tooltip("防守模式的坚持时长（秒）。")]
		public float defendSeconds = 90f;

		[Tooltip("防守区域半径。")]
		public float defendRadius = 12f;

		[Tooltip("护送距离（米）。0 = 使用场景内的终点标记或默认 40 米。")]
		public float escortDistance = 40f;

		[Header("Rewards")]
		public int rewardCurrency = 150;
		public int rewardScore = 1000;

		/// <summary>默认战役（Universe 内嵌，避免依赖资源资产）。</summary>
		public static List<MissionDefinition> CreateDefaultCampaign()
		{
			return new List<MissionDefinition>
			{
				Make(0, "00 · Training Ground", "_CompletedLevel", ObjectiveMode.ClearWaves, 5,
					"波次生存训练：清空所有波次。", 1000, 120),
				Make(1, "01 · Angry Bots", "AngryBots", ObjectiveMode.ClearWaves, 6,
					"夺回工业区：清空全部 6 波敌人。", 1600, 160),
				Make(2, "02 · Birth", "3-4_Birth", ObjectiveMode.Timed, 5,
					"限时突袭：在 240 秒内击杀 24 个敌人。", 1800, 180),
				Make(3, "03 · Pain", "3-4_Pain", ObjectiveMode.Defend, 8,
					"坚守阵地 90 秒，撑到撤离窗口开启。", 2200, 220),
				Make(4, "04 · Escape", "Escape", ObjectiveMode.Escort, 6,
					"护送幸存者抵达撤离点（40 米）。", 2600, 260),
			};
		}

		static MissionDefinition Make(int index, string name, string scene, ObjectiveMode mode,
			int waves, string briefing, int score, int currency)
		{
			var mission = CreateInstance<MissionDefinition>();
			mission.name = "Mission_" + index.ToString("00");
			mission.index = index;
			mission.displayName = name;
			mission.sceneName = scene;
			mission.mode = mode;
			mission.totalWaves = waves;
			mission.briefing = briefing;
			mission.rewardScore = score;
			mission.rewardCurrency = currency;
			if (mode == ObjectiveMode.Timed) { mission.targetKills = 24; mission.timeLimit = 240f; }
			if (mode == ObjectiveMode.Defend) { mission.defendSeconds = 90f; mission.defendRadius = 12f; }
			if (mode == ObjectiveMode.Escort) { mission.escortDistance = 40f; }
			return mission;
		}
	}
}