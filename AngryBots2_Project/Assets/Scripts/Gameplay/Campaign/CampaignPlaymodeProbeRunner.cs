using System.IO;
using System.Linq;
using System.Text;
using ABGameplay.Campaign;
using ABGameplay.Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// Play Mode 冒烟执行器：由编辑器探针临时挂到场景里，运行 8 秒后自检并退出。
	/// 放在运行时代码里，避免依赖 batchmode 下不可靠的 EditorApplication.update。
	/// </summary>
	public sealed class CampaignPlaymodeProbeRunner : MonoBehaviour
	{
		public string reportPath = @"E:\MyPlayGround\_logs\campaign_playmode.md";
		public float warmupSeconds = 8f;

		readonly StringBuilder report = new StringBuilder();
		int failures;
		float startedAt;

		void Start()
		{
			startedAt = Time.realtimeSinceStartup;
			report.AppendLine("=== 战役 Play Mode 冒烟 === " + System.DateTime.Now);
		}

		void Update()
		{
			if (Time.realtimeSinceStartup - startedAt < warmupSeconds) return;
			RunChecks();
			Finish();
		}

		void Check(bool condition, string message)
		{
			report.AppendLine((condition ? "[ok]   " : "[FAIL] ") + message);
			if (!condition) failures++;
		}

		void RunChecks()
		{
			var player = GameObject.FindGameObjectWithTag("Player");
			Check(player != null, "场景内存在 Player 对象");
			Check(player != null && player.GetComponent<PlayerHealth>() != null, "玩家带 PlayerHealth");
			Check(player != null && player.GetComponent<WeaponSystem>() != null, "玩家带 WeaponSystem");

			var mission = MissionController.Instance != null ? MissionController.Instance : Object.FindObjectOfType<MissionController>();
			Check(mission != null, "MissionController 已创建");
			if (mission != null)
			{
				Check(mission.Running, "任务处于运行状态");
				report.AppendLine("[info] " + mission.DescribeState());
			}

			var director = WaveDirectorEx.Instance != null ? WaveDirectorEx.Instance : Object.FindObjectOfType<WaveDirectorEx>();
			Check(director != null, "WaveDirectorEx 已创建");
			if (director != null)
			{
				Check(director.TotalSpawned > 0, "波次已生成敌人（" + director.TotalSpawned + "）");
				report.AppendLine("[info] " + director.DescribeState());
			}

			Check(ScoreSystem.Instance != null, "ScoreSystem 单例存在");

			var ui = CampaignUI.Instance != null ? CampaignUI.Instance : Object.FindObjectOfType<CampaignUI>();
			Check(ui != null, "CampaignUI 已构建");

			var agents = Object.FindObjectsOfType<NavMeshAgent>();
			int onMesh = agents.Count(a => a != null && a.isOnNavMesh);
			Check(agents.Length > 0, "场景内存在 NavMeshAgent（" + agents.Length + "）");
			Check(onMesh > 0, "至少一个 NavMeshAgent 位于 NavMesh 上（" + onMesh + "）");

			report.AppendLine("[info] 存活敌人 = " + EnemyRegistry.AliveCount +
				"，NavMesh 三角面 = " + NavMesh.CalculateTriangulation().indices.Length / 3);
		}

		void Finish()
		{
			report.AppendLine();
			report.AppendLine(failures == 0 ? "PLAYMODE RESULT: PASS" : "PLAYMODE RESULT: FAIL (" + failures + ")");
			try
			{
				var dir = Path.GetDirectoryName(reportPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
			}
			catch (System.Exception exception)
			{
				Debug.LogError("[CampaignProbe] 写报告失败: " + exception.Message);
			}

			Debug.Log(report.ToString());
			enabled = false;

#if UNITY_EDITOR
			UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
			Application.Quit(failures == 0 ? 0 : 1);
#endif
		}
	}
}