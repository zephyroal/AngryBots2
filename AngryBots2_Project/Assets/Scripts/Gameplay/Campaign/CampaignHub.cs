using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 战役自动接线：任何场景加载后判断是否为战役关卡，补齐管理器、计分、升级、
	/// 目标控制器与 UI；主菜单场景则只构建菜单。
	/// </summary>
	public static class CampaignHub
	{
		public static CampaignConfig Config { get; private set; }
		public static bool Disabled { get; set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Install()
		{
			if (Disabled) return;

			Config = Resources.Load<CampaignConfig>("ABCampaign/CampaignConfig");
			if (Config != null) CampaignManager.SetConfig(Config);

			AudioListener.volume = SaveSystem.Data.volume;

			string scene = SceneManager.GetActiveScene().name;
			if (scene == CampaignManager.MenuSceneName)
			{
				MainMenuUI.EnsureInstance();
				return;
			}

			CampaignManager.EnsureInstance();
			ScoreSystem.EnsureInstance();
			UpgradeSystem.EnsureInstance();
			ComboReward.EnsureInstance();

			var mission = CampaignManager.Instance != null && CampaignManager.Instance.Current != null
				? CampaignManager.Instance.Current
				: CampaignManager.DefaultMissionForScene(scene);

			if (mission == null && FindAnyPlayerLike() == null && Object.FindObjectOfType<EnemyHealth>() == null)
				return; // 非战役场景（工具场景 / 空场景）不接管

			EnsurePlayer(scene);
			BindSceneEnemies(GameObject.FindGameObjectWithTag("Player"));

			if (mission != null)
			{
				var controller = MissionController.Instance != null
					? MissionController.Instance
					: Object.FindObjectOfType<MissionController>();
				if (controller == null)
				{
					var go = new GameObject("[MissionController]");
					controller = go.AddComponent<MissionController>();
				}
				controller.Definition = mission;
			}

			CampaignUI.EnsureInstance();

			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
		}

		/// <summary>
		/// U6 战役场景里预摆放的敌人没有绑定玩家引用（原工程由关卡脚本赋值），
		/// 这里统一补齐，避免 EnemyChaseMovement / EnemyAttack 每帧空引用。
		/// </summary>
		static void BindSceneEnemies(GameObject player)
		{
			if (player == null) return;
			var playerTransform = player.transform;

			foreach (var chase in Object.FindObjectsOfType<EnemyChaseMovement>())
			{
				if (chase == null) continue;
				if (chase.player == null) chase.player = playerTransform;
				if (chase.lineOfSightOrigin == null) chase.lineOfSightOrigin = chase.transform;
				if (chase.agent == null) chase.agent = chase.GetComponent<UnityEngine.AI.NavMeshAgent>();
			}

			foreach (var patrol in Object.FindObjectsOfType<EnemyPatrol>())
			{
				if (patrol == null) continue;
				if (patrol.agent == null) patrol.agent = patrol.GetComponent<UnityEngine.AI.NavMeshAgent>();
				if (patrol.chaseMovement == null) patrol.chaseMovement = patrol.GetComponent<EnemyChaseMovement>();
			}

			foreach (var attack in Object.FindObjectsOfType<EnemyAttack>())
			{
				if (attack != null && attack.player == null) attack.player = playerTransform;
			}
		}

		static GameObject FindAnyPlayerLike()
		{
			var player = GameObject.FindGameObjectWithTag("Player");
			if (IsUsablePlayer(player)) return player;
			var health = Object.FindObjectOfType<PlayerHealth>();
			return health != null ? health.gameObject : null;
		}

		/// <summary>只有带战役所需组件（PlayerHealth / WeaponSystem）的对象才算“可玩玩家”。</summary>
		static bool IsUsablePlayer(GameObject candidate)
		{
			if (candidate == null) return false;
			if (candidate.GetComponent<PlayerHealth>() != null) return true;
			if (candidate.GetComponent<WeaponSystem>() != null) return true;
			return false;
		}

		/// <summary>U6 战役场景没有玩家：按配置实例化玩家预制体。</summary>
		static void EnsurePlayer(string sceneName)
		{
			if (FindAnyPlayerLike() != null) return;

			// 场景里可能存在只带 Player tag 的空占位物（原工程遗留），先摘掉 tag 再生成真玩家。
			var placeholder = GameObject.FindGameObjectWithTag("Player");
			if (placeholder != null && !IsUsablePlayer(placeholder))
			{
				placeholder.tag = "Untagged";
				// 旧战役遗留的玩家控制器没有战役所需的血量/武器系统，直接停用避免与新玩家抢输入。
				if (placeholder.GetComponent<PlayerMoveController>() != null ||
					placeholder.GetComponent<PlayerMovementAndLook>() != null)
					placeholder.SetActive(false);
			}

			if (Config == null || Config.playerPrefab == null)
			{
				Debug.LogWarning("[CampaignHub] 场景 " + sceneName + " 没有玩家，且未配置 playerPrefab");
				return;
			}

			Vector3 position = Vector3.zero;
			Quaternion rotation = Quaternion.identity;

			var spawnPoint = GameObject.Find("SpawnPoint");
			if (spawnPoint != null)
			{
				position = spawnPoint.transform.position;
				rotation = spawnPoint.transform.rotation;
			}
			else
			{
				var camera = Camera.main;
				if (camera != null)
				{
					position = camera.transform.position + Vector3.up * 1.2f - camera.transform.forward * 2f;
					rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
				}
			}

			if (UnityEngine.AI.NavMesh.SamplePosition(position, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
				position = hit.position + Vector3.up * 0.1f;

			var instance = Object.Instantiate(Config.playerPrefab, position, rotation);
			instance.name = "Player";
			instance.tag = "Player";

			// 若玩家预制体自带相机，禁用场景中的独立相机，避免双相机叠加。
			var playerCamera = instance.GetComponentInChildren<Camera>();
			if (playerCamera != null)
			{
				foreach (var camera in Object.FindObjectsOfType<Camera>())
				{
					if (camera != null && camera != playerCamera && !camera.transform.IsChildOf(instance.transform))
						camera.enabled = false;
				}
			}

			Debug.Log("[CampaignHub] 已为 " + sceneName + " 生成玩家: " + position);
		}
	}
}