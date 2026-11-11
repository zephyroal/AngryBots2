using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ABGameplay.Campaign
{
	/// <summary>可序列化的存档数据（JSON 落盘到 persistentDataPath）。</summary>
	[Serializable]
	public class SaveData
	{
		public int version = 1;

		// 战役进度
		public int highestUnlockedMission;
		public int checkpointMission = -1;
		public int checkpointScore;
		public List<int> completedMissions = new List<int>();

		// 累计成绩
		public int totalScore;
		public int bestScore;
		public int bestCombo;
		public int currency;
		public int totalKills;

		/// <summary>本地排行榜（最多 5 条，降序）。</summary>
		public List<int> bestRuns = new List<int>();

		// 升级等级（商店）
		public int damageLevel;
		public int ammoLevel;
		public int healthLevel;

		// 设置
		public float sensitivity = 2f;
		public float volume = 0.8f;

		public bool IsCompleted(int missionIndex)
		{
			return completedMissions != null && completedMissions.Contains(missionIndex);
		}

		public void MarkCompleted(int missionIndex)
		{
			if (completedMissions == null) completedMissions = new List<int>();
			if (!completedMissions.Contains(missionIndex)) completedMissions.Add(missionIndex);
			highestUnlockedMission = Mathf.Max(highestUnlockedMission, missionIndex + 1);
		}
	}

	/// <summary>
	/// 存档系统：读 / 写 / 重置。<see cref="Data"/> 永远非 null。
	/// 写入失败只记录警告，不影响游戏流程（批量自检环境可能不可写）。
	/// </summary>
	public static class SaveSystem
	{
		const string FileName = "ab2_campaign_save.json";

		static SaveData data;
		static bool loaded;
		static bool dirty;

		public static SaveData Data
		{
			get
			{
				if (!loaded) Load();
				if (data == null) data = new SaveData();
				return data;
			}
		}

		public static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

		public static void Load()
		{
			loaded = true;
			try
			{
				if (File.Exists(FilePath))
				{
					string json = File.ReadAllText(FilePath);
					data = JsonUtility.FromJson<SaveData>(json);
				}
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SaveSystem] 读取存档失败，使用新存档: " + exception.Message);
				data = null;
			}

			if (data == null) data = new SaveData();
			if (data.completedMissions == null) data.completedMissions = new List<int>();
			if (data.bestRuns == null) data.bestRuns = new List<int>();
			dirty = false;
		}

		/// <summary>记录一次通关成绩，维护本地 Top 5（降序）。</summary>
		public static void RecordRun(int score)
		{
			if (score <= 0) return;
			var save = Data;
			if (save.bestRuns == null) save.bestRuns = new List<int>();
			save.bestRuns.Add(score);
			save.bestRuns.Sort((a, b) => b.CompareTo(a));
			if (save.bestRuns.Count > 5) save.bestRuns.RemoveRange(5, save.bestRuns.Count - 5);
			MarkDirty();
		}

		public static void Save()
		{
			if (!loaded) Load();
			try
			{
				string dir = Path.GetDirectoryName(FilePath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
				dirty = false;
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SaveSystem] 写入存档失败: " + exception.Message);
			}
		}

		/// <summary>标记需要保存（在关卡结束时统一落盘，减少 IO）。</summary>
		public static void MarkDirty()
		{
			dirty = true;
		}

		public static void FlushIfDirty()
		{
			if (dirty) Save();
		}

		public static void ResetProgress()
		{
			data = new SaveData();
			loaded = true;
			dirty = true;
			Save();
		}

		/// <summary>自检 / 测试用：替换内存存档但不落盘。</summary>
		public static void ReplaceInMemory(SaveData replacement)
		{
			data = replacement ?? new SaveData();
			if (data.completedMissions == null) data.completedMissions = new List<int>();
			if (data.bestRuns == null) data.bestRuns = new List<int>();
			loaded = true;
			dirty = false;
		}

		/// <summary>自检用：清空内存缓存，下次访问重新读盘。</summary>
		public static void ForgetCache()
		{
			loaded = false;
			data = null;
		}
	}
}