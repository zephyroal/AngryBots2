using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 战役 HUD + 暂停 / 商店 / 设置 / 结算界面。全部运行时构建，不依赖场景内 UI。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class CampaignUI : MonoBehaviour
	{
		public static CampaignUI Instance { get; private set; }

		Canvas canvas;
		Text missionText;
		Text objectiveText;
		Text scoreText;
		Text comboText;
		Text currencyText;
		Text statusText;
		Text hintText;

		GameObject pausePanel;
		GameObject shopPanel;
		GameObject settingsPanel;
		GameObject resultPanel;
		Text resultTitle;
		Text resultBody;
		Text shopInfo;
		Text[] shopLabels;
		Text settingsInfo;

		MissionController mission;
		ScoreSystem score;
		UpgradeSystem upgrades;
		PlayerHealth playerHealth;
		WeaponSystem weapons;

		bool paused;
		float refreshTimer;
		bool built;
		Text resultHint;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		void OnDestroy()
		{
			if (Instance == this) Instance = null;
			if (mission != null) mission.Finished -= OnMissionFinished;
			if (score != null)
			{
				score.ScoreChanged -= OnScoreChanged;
				score.ComboChanged -= OnComboChanged;
				score.CurrencyChanged -= OnCurrencyChanged;
			}
			if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
		}

		void Start()
		{
			Build();
			Bind();
		}

		void Build()
		{
			if (built) return;
			built = true;

			canvas = UiFactory.CreateCanvas("[CampaignUI]");
			DontDestroyOnLoad(canvas.gameObject);

			// ─── HUD 左上：分数 / 连击 / 货币 ───
			var stats = UiFactory.CreateText(canvas.transform, "Stats", "", 26, TextAnchor.UpperLeft);
			scoreText = stats;
			UiFactory.Anchor(stats.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
				new Vector2(32f, -28f), new Vector2(520f, 150f));

			var combo = UiFactory.CreateText(canvas.transform, "Combo", "", 22, TextAnchor.UpperLeft);
			comboText = combo;
			UiFactory.Anchor(combo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
				new Vector2(32f, -150f), new Vector2(520f, 60f));

			var currency = UiFactory.CreateText(canvas.transform, "Currency", "", 24, TextAnchor.UpperRight);
			currencyText = currency;
			UiFactory.Anchor(currency.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
				new Vector2(-32f, -28f), new Vector2(420f, 60f));

			// ─── HUD 顶部中央：关卡 + 目标 ───
			var missionLabel = UiFactory.CreateText(canvas.transform, "Mission", "", 24, TextAnchor.UpperCenter);
			missionText = missionLabel;
			UiFactory.Anchor(missionLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
				new Vector2(0f, -24f), new Vector2(900f, 44f));

			var objective = UiFactory.CreateText(canvas.transform, "Objective", "", 28, TextAnchor.UpperCenter);
			objectiveText = objective;
			objectiveText.color = new Color(1f, 0.88f, 0.55f);
			UiFactory.Anchor(objective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
				new Vector2(0f, -68f), new Vector2(1100f, 60f));

			// ─── HUD 左下：血量 / 弹药 ───
			var status = UiFactory.CreateText(canvas.transform, "Status", "", 22, TextAnchor.LowerLeft);
			statusText = status;
			UiFactory.Anchor(status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
				new Vector2(32f, 26f), new Vector2(760f, 90f));

			var hint = UiFactory.CreateText(canvas.transform, "Hint", "", 20, TextAnchor.LowerCenter);
			hintText = hint;
			hintText.color = new Color(0.75f, 0.85f, 0.9f);
			UiFactory.Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
				new Vector2(0f, 26f), new Vector2(1200f, 40f));
			hintText.text = "[ESC] PAUSE   [1/2] WEAPON   [R] RELOAD   [RMB] GRENADE";

			BuildPause();
			BuildShop();
			BuildSettings();
			BuildResult();
		}

		void BuildPause()
		{
			var root = UiFactory.FullScreenPanel(canvas.transform, "Pause", new Color(0f, 0f, 0f, 0.72f));
			pausePanel = root.gameObject;

			var title = UiFactory.CreateText(root, "Title", "PAUSED", 56, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 220f), new Vector2(600f, 80f));

			UiFactory.CreateButton(root, "Resume", "RESUME", new Vector2(0f, 120f), new Vector2(360f, 56f), TogglePause);
			UiFactory.CreateButton(root, "Restart", "RESTART MISSION", new Vector2(0f, 48f), new Vector2(360f, 56f), () =>
			{
				TogglePause();
				if (CampaignManager.Instance != null) CampaignManager.Instance.Retry();
			});
			UiFactory.CreateButton(root, "Shop", "UPGRADE SHOP", new Vector2(0f, -24f), new Vector2(360f, 56f), OpenShop);
			UiFactory.CreateButton(root, "Settings", "SETTINGS", new Vector2(0f, -96f), new Vector2(360f, 56f), OpenSettings);
			UiFactory.CreateButton(root, "Menu", "MAIN MENU", new Vector2(0f, -168f), new Vector2(360f, 56f), () =>
			{
				if (CampaignManager.Instance != null) CampaignManager.Instance.ReturnToMenu();
			});

			pausePanel.SetActive(false);
		}

		void BuildShop()
		{
			var root = UiFactory.FullScreenPanel(canvas.transform, "Shop", new Color(0.02f, 0.03f, 0.05f, 0.92f));
			shopPanel = root.gameObject;

			var title = UiFactory.CreateText(root, "Title", "UPGRADE SHOP", 48, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 300f), new Vector2(700f, 70f));

			shopInfo = UiFactory.CreateText(root, "Info", "", 24, TextAnchor.MiddleCenter);
			UiFactory.Anchor(shopInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 240f), new Vector2(800f, 50f));

			shopLabels = new Text[3];
			for (int i = 0; i < 3; i++)
			{
				int kindIndex = i;
				var kind = (UpgradeKind)i;
				var label = UiFactory.CreateText(root, "Level" + i, "", 24, TextAnchor.MiddleLeft);
				shopLabels[i] = label;
				UiFactory.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
					new Vector2(-260f, 160f - i * 100f), new Vector2(520f, 56f));

				UiFactory.CreateButton(root, "Buy" + i, "BUY", new Vector2(240f, 160f - i * 100f), new Vector2(200f, 52f), () =>
				{
					var system = upgrades != null ? upgrades : UpgradeSystem.EnsureInstance();
					if (system.Buy(kind)) RefreshShop();
				});
			}

			UiFactory.CreateButton(root, "Close", "BACK", new Vector2(0f, -220f), new Vector2(320f, 56f), CloseShop);
			shopPanel.SetActive(false);
		}

		void BuildSettings()
		{
			var root = UiFactory.FullScreenPanel(canvas.transform, "Settings", new Color(0.02f, 0.03f, 0.05f, 0.94f));
			settingsPanel = root.gameObject;

			var title = UiFactory.CreateText(root, "Title", "SETTINGS", 48, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 260f), new Vector2(700f, 70f));

			settingsInfo = UiFactory.CreateText(root, "Info", "", 26, TextAnchor.MiddleCenter);
			UiFactory.Anchor(settingsInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 150f), new Vector2(900f, 160f));

			UiFactory.CreateButton(root, "SensDown", "SENS -", new Vector2(-240f, 40f), new Vector2(200f, 54f), () => ChangeSensitivity(-0.2f));
			UiFactory.CreateButton(root, "SensUp", "SENS +", new Vector2(240f, 40f), new Vector2(200f, 54f), () => ChangeSensitivity(0.2f));
			UiFactory.CreateButton(root, "VolDown", "VOL -", new Vector2(-240f, -40f), new Vector2(200f, 54f), () => ChangeVolume(-0.1f));
			UiFactory.CreateButton(root, "VolUp", "VOL +", new Vector2(240f, -40f), new Vector2(200f, 54f), () => ChangeVolume(0.1f));

			UiFactory.CreateButton(root, "Reset", "RESET PROGRESS", new Vector2(0f, -150f), new Vector2(320f, 54f), () =>
			{
				SaveSystem.ResetProgress();
				RefreshSettings();
			});
			UiFactory.CreateButton(root, "Close", "BACK", new Vector2(0f, -240f), new Vector2(320f, 54f), CloseSettings);

			settingsPanel.SetActive(false);
		}

		void BuildResult()
		{
			var root = UiFactory.FullScreenPanel(canvas.transform, "Result", new Color(0.01f, 0.02f, 0.04f, 0.9f));
			resultPanel = root.gameObject;

			resultTitle = UiFactory.CreateText(root, "Title", "", 60, TextAnchor.MiddleCenter);
			UiFactory.Anchor(resultTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 220f), new Vector2(1000f, 90f));

			resultBody = UiFactory.CreateText(root, "Body", "", 28, TextAnchor.MiddleCenter);
			UiFactory.Anchor(resultBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 90f), new Vector2(1000f, 160f));

			UiFactory.CreateButton(root, "Next", "NEXT MISSION", new Vector2(0f, -30f), new Vector2(360f, 56f), () =>
			{
				if (CampaignManager.Instance != null) CampaignManager.Instance.NextMission();
			});
			UiFactory.CreateButton(root, "Retry", "RETRY", new Vector2(0f, -102f), new Vector2(360f, 56f), () =>
			{
				if (CampaignManager.Instance != null) CampaignManager.Instance.Retry();
			});
			UiFactory.CreateButton(root, "Menu", "MAIN MENU", new Vector2(0f, -174f), new Vector2(360f, 56f), () =>
			{
				if (CampaignManager.Instance != null) CampaignManager.Instance.ReturnToMenu();
			});

			resultHint = UiFactory.CreateText(root, "Hint", "", 20, TextAnchor.MiddleCenter);
			UiFactory.Anchor(resultHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, -250f), new Vector2(900f, 40f));

			resultPanel.SetActive(false);
		}

		void Bind()
		{
			mission = MissionController.Instance != null ? MissionController.Instance : FindObjectOfType<MissionController>();
			score = ScoreSystem.Instance != null ? ScoreSystem.Instance : ScoreSystem.EnsureInstance();
			upgrades = UpgradeSystem.Instance != null ? UpgradeSystem.Instance : UpgradeSystem.EnsureInstance();

			var player = PlayerSource.Player;
			if (player != null)
			{
				playerHealth = player.GetComponent<PlayerHealth>();
				weapons = player.GetComponent<WeaponSystem>();
			}

			if (mission != null)
			{
				mission.ObjectiveUpdated += OnObjectiveUpdated;
				mission.Finished += OnMissionFinished;
				OnObjectiveUpdated(mission.Progress, mission.ObjectiveText);
				if (mission.Definition != null) missionText.text = mission.Definition.displayName;
				missionText.text += "   ·   " + DescribeMode(mission.Definition);
			}

			if (score != null)
			{
				score.ScoreChanged += OnScoreChanged;
				score.ComboChanged += OnComboChanged;
				score.CurrencyChanged += OnCurrencyChanged;
				OnScoreChanged(score.Score, 0, score.Combo);
				OnCurrencyChanged(score.Currency);
			}

			if (playerHealth != null) playerHealth.Died += OnPlayerDied;

			RefreshStatus();
		}

		static string DescribeMode(MissionDefinition definition)
		{
			if (definition == null) return "FREE PLAY";
			switch (definition.mode)
			{
				case ObjectiveMode.ClearWaves: return "CLEAR WAVES";
				case ObjectiveMode.Timed: return "TIMED ASSAULT";
				case ObjectiveMode.Defend: return "DEFEND";
				default: return "ESCORT";
			}
		}

		void Update()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				if (shopPanel != null && shopPanel.activeSelf) CloseShop();
				else if (settingsPanel != null && settingsPanel.activeSelf) CloseSettings();
				else TogglePause();
			}

			refreshTimer -= Time.deltaTime;
			if (refreshTimer > 0f) return;
			refreshTimer = 0.2f;
			RefreshStatus();
		}

		void RefreshStatus()
		{
			if (statusText == null) return;

			string hp = playerHealth != null ? "HP " + playerHealth.CurrentHealth + " / " + playerHealth.MaxHealth : "HP --";
			string ammo = weapons != null
				? "AMMO " + weapons.CurrentMag + " / " + weapons.CurrentMagSize + (weapons.Reloading ? "  RELOADING" : "") +
				  "   GRENADE " + weapons.Grenades
				: "AMMO --";

			statusText.text = hp + "    " + ammo;
		}

		void OnObjectiveUpdated(float progress, string text)
		{
			if (objectiveText != null) objectiveText.text = text;
		}

		void OnScoreChanged(int total, int delta, int combo)
		{
			if (scoreText != null)
				scoreText.text = "SCORE  " + total.ToString("N0") +
					(delta > 0 ? "   <color=#8fe08f>+" + delta + "</color>" : string.Empty) +
					"\nBEST   " + SaveSystem.Data.bestScore.ToString("N0");
		}

		void OnComboChanged(int combo)
		{
			if (comboText == null) return;
			comboText.text = combo > 1
				? "COMBO x" + combo + "    MULT x" + (ScoreSystem.Instance != null ? ScoreSystem.Instance.Multiplier.ToString("0.0") : "1.0")
				: string.Empty;
		}

		void OnCurrencyChanged(int amount)
		{
			if (currencyText != null) currencyText.text = "CREDITS  " + amount.ToString("N0");
		}

		void OnPlayerDied()
		{
			if (mission != null) mission.Finish(false);
		}

		void OnMissionFinished(bool won)
		{
			if (resultPanel == null) return;
			resultPanel.SetActive(true);
			pausePanel.SetActive(false);
			shopPanel.SetActive(false);
			settingsPanel.SetActive(false);

			resultTitle.text = won ? "MISSION COMPLETE" : "MISSION FAILED";
			resultTitle.color = won ? new Color(0.55f, 1f, 0.62f) : new Color(1f, 0.45f, 0.4f);

			var builder = new StringBuilder();
			if (score != null)
			{
				builder.AppendLine("SCORE      " + score.Score.ToString("N0"));
				builder.AppendLine("BEST COMBO x" + score.BestCombo);
				builder.AppendLine("CREDITS    " + score.Currency.ToString("N0"));
			}
			if (mission != null && mission.Definition != null)
			{
				builder.AppendLine("MISSION    " + mission.Definition.displayName);
				if (won) builder.AppendLine("REWARD     +" + mission.Definition.rewardCurrency + " CREDITS");
			}
			resultBody.text = builder.ToString();

			bool hasNext = CampaignManager.Instance != null && mission != null && mission.Definition != null &&
				mission.Definition.index + 1 < CampaignManager.Instance.Missions.Count;
			resultHint.text = hasNext ? "Press NEXT MISSION to continue the campaign." : "Campaign finished — thanks for playing!";
		}

		void TogglePause()
		{
			if (resultPanel != null && resultPanel.activeSelf) return;

			paused = !paused;
			pausePanel.SetActive(paused);
			Time.timeScale = paused ? 0f : 1f;
			Cursor.visible = paused;
			Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
		}

		void OpenShop()
		{
			shopPanel.SetActive(true);
			pausePanel.SetActive(false);
			RefreshShop();
		}

		void CloseShop()
		{
			shopPanel.SetActive(false);
			if (paused) pausePanel.SetActive(true);
		}

		void RefreshShop()
		{
			if (shopLabels == null) return;
			var system = upgrades != null ? upgrades : UpgradeSystem.EnsureInstance();
			if (system == null) return;

			shopInfo.text = "CREDITS  " + (ScoreSystem.Instance != null ? ScoreSystem.Instance.Currency.ToString("N0") : "0");

			shopLabels[0].text = "WEAPON DAMAGE      LV " + system.GetLevel(UpgradeKind.WeaponDamage) + " / " + system.maxLevel;
			shopLabels[1].text = "AMMO CAPACITY      LV " + system.GetLevel(UpgradeKind.AmmoCapacity) + " / " + system.maxLevel;
			shopLabels[2].text = "ARMOR / MAX HP     LV " + system.GetLevel(UpgradeKind.ArmorHealth) + " / " + system.maxLevel;

			for (int i = 0; i < shopLabels.Length; i++)
			{
				var kind = (UpgradeKind)i;
				shopLabels[i].text += system.IsMaxed(kind)
					? "    MAX"
					: "    COST " + system.GetCost(kind);
			}
		}

		void OpenSettings()
		{
			settingsPanel.SetActive(true);
			pausePanel.SetActive(false);
			RefreshSettings();
		}

		void CloseSettings()
		{
			settingsPanel.SetActive(false);
			if (paused) pausePanel.SetActive(true);
		}

		void RefreshSettings()
		{
			settingsInfo.text = "MOUSE SENSITIVITY   " + SaveSystem.Data.sensitivity.ToString("0.0") +
				"\nMASTER VOLUME      " + Mathf.RoundToInt(SaveSystem.Data.volume * 100f) + "%";
		}

		void ChangeSensitivity(float delta)
		{
			SaveSystem.Data.sensitivity = Mathf.Clamp(SaveSystem.Data.sensitivity + delta, 0.4f, 8f);
			SaveSystem.MarkDirty();
			SaveSystem.Save();
			RefreshSettings();
		}

		void ChangeVolume(float delta)
		{
			SaveSystem.Data.volume = Mathf.Clamp01(SaveSystem.Data.volume + delta);
			AudioListener.volume = SaveSystem.Data.volume;
			SaveSystem.MarkDirty();
			SaveSystem.Save();
			RefreshSettings();
		}

		public static CampaignUI EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<CampaignUI>();
			if (existing != null) return existing;
			var go = new GameObject("[CampaignUI]");
			return go.AddComponent<CampaignUI>();
		}
	}
}