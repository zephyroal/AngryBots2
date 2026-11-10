using System.Collections.Generic;
using ABGameplay.Enemies;
using UnityEngine;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 战役运行时配置（放在 Resources/ABCampaign/CampaignConfig）。
	/// 只做资源引用，不含逻辑；缺失时 CampaignManager 会回退到内置默认战役。
	/// </summary>
	[CreateAssetMenu(fileName = "CampaignConfig", menuName = "ABGameplay/Campaign Config", order = 21)]
	public sealed class CampaignConfig : ScriptableObject
	{
		public List<MissionDefinition> missions = new List<MissionDefinition>();
		public GameObject playerPrefab;
		public GameObject fallbackEnemyPrefab;

		[Tooltip("敌人刷怪表（含 Spider / Mech / Buzzer / Kamikaze / Boss 定义）。")]
		public EnemySpawnTable enemyTable;
	}
}