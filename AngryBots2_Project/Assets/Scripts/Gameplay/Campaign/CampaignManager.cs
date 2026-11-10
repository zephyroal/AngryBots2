using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 战役管理：关卡列表、当前进度、胜负结算、续关与场景切换。
	/// 进度写入 <see cref="SaveSystem"/>；关卡内容由 <see cref="MissionController"/> 执行。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class CampaignManager : MonoBehaviour
	{
		public static CampaignManager Instance { get; private set; }

		/// <summary>Resources/ABCampaign/CampaignConfig（可为空，回退到内置默认战役）。</summary>
		public static CampaignConfig Config { get; private set; }

		public const string MenuSceneName = "MainMenu";

		static List<MissionDefinition> defaults;

		public List<MissionDefinition> Missions { get; private set; }
		public int CurrentIndex { get; private set; } = -1;
		public MissionDefinition Current
		{
			get { return Missions != null && CurrentIndex >= 0 && CurrentIndex < Missions.Count ? Missions[CurrentIndex] : null; }
		}

		public int HighestUnlocked { get { return Mathf.Clamp(SaveSystem.Data.highestUnlockedMission, 0, Mathf.Max(0, Missions.Count - 1)); } }
		public int LastResultWon { get; private set; } = -1;

		/// <summary>关卡开始（UI 刷新用）。</summary>
		public event Action<MissionDefinition> MissionStarted;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			LoadConfig();
			Missions = Config != null && Config.missions != null && Config.missions.Count > 0
				? new List<MissionDefinition>(Config.missions)
				: DefaultCampaign();
		}

		void Start()
		{
			CurrentIndex = IndexOfScene(gameObject.scene.name);
			if (CurrentIndex >= 0 && Current != null && MissionStarted != null) MissionStarted(Current);
		}

		static void LoadConfig()
		{
			if (Config != null) return;
			Config = Resources.Load<CampaignConfig>("ABCampaign/CampaignConfig");
		}

		/// <summary>由 <see cref="CampaignHub"/> 在启动时注入配置。</summary>
		public static void SetConfig(CampaignConfig config)
		{
			if (config != null) Config = config;
		}

		public static List<MissionDefinition> DefaultCampaign()
		{
			if (defaults == null) defaults = MissionDefinition.CreateDefaultCampaign();
			return defaults;
		}

		public static MissionDefinition DefaultMissionForScene(string sceneName)
		{
			var list = DefaultCampaign();
			for (int i = 0; i < list.Count; i++)
				if (list[i] != null && list[i].sceneName == sceneName) return list[i];
			return null;
		}

		public int IndexOfScene(string sceneName)
		{
			if (Missions == null) return -1;
			for (int i = 0; i < Missions.Count; i++)
				if (Missions[i] != null && Missions[i].sceneName == sceneName) return i;
			return -1;
		}

		/// <summary>开始指定关卡（会切换场景）。</summary>
		public bool StartMission(int index)
		{
			if (Missions == null || index < 0 || index >= Missions.Count) return false;
			var mission = Missions[index];
			if (mission == null || string.IsNullOrEmpty(mission.sceneName)) return false;

			CurrentIndex = index;
			SaveSystem.Data.checkpointMission = index;
			SaveSystem.Data.checkpointScore = 0;
			SaveSystem.MarkDirty();

			Debug.Log("[CampaignManager] 载入关卡 " + index + ": " + mission.displayName + " → " + mission.sceneName);
			SceneManager.LoadScene(mission.sceneName);
			return true;
		}

		/// <summary>继续游戏：从检查点，否则从最高解锁关卡。</summary>
		public void Continue()
		{
			int index = SaveSystem.Data.checkpointMission;
			if (index < 0) index = HighestUnlocked;
			StartMission(Mathf.Clamp(index, 0, Mathf.Max(0, Missions.Count - 1)));
		}

		/// <summary>由 MissionController 在胜负判定后调用。</summary>
		public void ReportResult(bool won)
		{
			LastResultWon = won ? 1 : 0;
			var save = SaveSystem.Data;

			if (won && Current != null)
			{
				save.MarkCompleted(Current.index);
				save.checkpointMission = Mathf.Min(Current.index + 1, Mathf.Max(0, Missions.Count - 1));
				save.checkpointScore = 0;

				var score = ScoreSystem.Instance;
				if (score != null && Current.rewardCurrency > 0) score.AddCurrency(Current.rewardCurrency);
			}
			else if (Current != null)
			{
				save.checkpointMission = Current.index;
			}

			SaveSystem.Save();
		}

		public void Retry()
		{
			int index = CurrentIndex >= 0 ? CurrentIndex : 0;
			StartMission(index);
		}

		/// <summary>下一关；已是最后一关则返回主菜单。</summary>
		public void NextMission()
		{
			int next = CurrentIndex + 1;
			if (Missions != null && next < Missions.Count) StartMission(next);
			else ReturnToMenu();
		}

		public void ReturnToMenu()
		{
			SaveSystem.Save();
			if (Application.CanStreamedLevelBeLoaded(MenuSceneName)) SceneManager.LoadScene(MenuSceneName);
			else SceneManager.LoadScene(0);
		}

		public static CampaignManager EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<CampaignManager>();
			if (existing != null) return existing;
			var go = new GameObject("[CampaignManager]");
			return go.AddComponent<CampaignManager>();
		}

		/// <summary>自检用：只读状态描述。</summary>
		public string DescribeState()
		{
			return "campaign missionIndex=" + CurrentIndex +
				" current=" + (Current != null ? Current.displayName : "<none>") +
				" highestUnlocked=" + HighestUnlocked +
				" missions=" + (Missions != null ? Missions.Count : 0) +
				" lastResultWon=" + LastResultWon;
		}
	}
}