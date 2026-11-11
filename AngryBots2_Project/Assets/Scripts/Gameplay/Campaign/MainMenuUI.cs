using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ABGameplay.Campaign
{
	/// <summary>主菜单：继续 / 新战役 / 关卡选择 / 升级商店 / 设置 / 退出。</summary>
	[DisallowMultipleComponent]
	public sealed class MainMenuUI : MonoBehaviour
	{
		public static MainMenuUI Instance { get; private set; }

		Canvas canvas;
		GameObject rootPanel;
		GameObject levelPanel;
		GameObject shopPanel;
		GameObject settingsPanel;
		Text levelListText;
		Text shopInfo;
		Text settingsInfo;

		CampaignManager manager;

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
		}

		void Start()
		{
			Build();
			manager = CampaignManager.EnsureInstance();
		}

		void Build()
		{
			canvas = UiFactory.CreateCanvas("[MainMenuUI]");
			DontDestroyOnLoad(canvas.gameObject);

			var background = UiFactory.FullScreenPanel(canvas.transform, "Background", new Color(0.03f, 0.04f, 0.06f, 1f));
			background.SetAsFirstSibling();

			var title = UiFactory.CreateText(canvas.transform, "Title", "ANGRY BOTS", 88, TextAnchor.MiddleCenter);
			title.color = new Color(0.95f, 0.75f, 0.35f);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 280f), new Vector2(1200f, 110f));

			var subtitle = UiFactory.CreateText(canvas.transform, "Subtitle", "CAMPAIGN", 30, TextAnchor.MiddleCenter);
			subtitle.color = new Color(0.7f, 0.8f, 0.9f);
			UiFactory.Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 200f), new Vector2(800f, 50f));

			rootPanel = new GameObject("MenuButtons", typeof(RectTransform));
			rootPanel.transform.SetParent(canvas.transform, false);
			var rect = rootPanel.GetComponent<RectTransform>();
			UiFactory.Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				Vector2.zero, new Vector2(500f, 600f));

			UiFactory.CreateButton(rootPanel.transform, "Continue", "CONTINUE", new Vector2(0f, 90f), new Vector2(420f, 60f), () =>
			{
				manager = CampaignManager.EnsureInstance();
				manager.Continue();
			});
			UiFactory.CreateButton(rootPanel.transform, "New", "NEW CAMPAIGN", new Vector2(0f, 10f), new Vector2(420f, 60f), () =>
			{
				SaveSystem.ResetProgress();
				manager = CampaignManager.EnsureInstance();
				manager.StartMission(0);
			});
			UiFactory.CreateButton(rootPanel.transform, "Levels", "LEVEL SELECT", new Vector2(0f, -70f), new Vector2(420f, 60f), OpenLevels);
			UiFactory.CreateButton(rootPanel.transform, "Shop", "UPGRADE SHOP", new Vector2(0f, -150f), new Vector2(420f, 60f), OpenShop);
			UiFactory.CreateButton(rootPanel.transform, "Settings", "SETTINGS", new Vector2(0f, -230f), new Vector2(420f, 60f), OpenSettings);
			UiFactory.CreateButton(rootPanel.transform, "Quit", "QUIT", new Vector2(0f, -310f), new Vector2(420f, 60f), () =>
			{
#if UNITY_EDITOR
				UnityEditor.EditorApplication.isPlaying = false;
#else
				Application.Quit();
#endif
			});

			BuildLevelPanel();
			BuildShopPanel();
			BuildSettingsPanel();
		}

		void BuildLevelPanel()
		{
			var panel = UiFactory.FullScreenPanel(canvas.transform, "LevelSelect", new Color(0.02f, 0.03f, 0.05f, 0.94f));
			levelPanel = panel.gameObject;

			var title = UiFactory.CreateText(panel, "Title", "LEVEL SELECT", 48, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 290f), new Vector2(800f, 70f));

			levelListText = UiFactory.CreateText(panel, "List", "", 24, TextAnchor.UpperLeft);
			UiFactory.Anchor(levelListText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
				new Vector2(0f, 220f), new Vector2(900f, 380f));

			for (int i = 0; i < 5; i++)
			{
				int index = i;
				var button = UiFactory.CreateButton(panel, "Mission" + i, (i + 1).ToString("00"), new Vector2(-360f + i * 180f, -140f),
					new Vector2(160f, 56f), () =>
					{
						manager = CampaignManager.EnsureInstance();
						if (index <= SaveSystem.Data.highestUnlockedMission) manager.StartMission(index);
					});
				button.name = "MissionButton" + i;
			}

			UiFactory.CreateButton(panel, "Back", "BACK", new Vector2(0f, -260f), new Vector2(320f, 56f), () => levelPanel.SetActive(false));
			levelPanel.SetActive(false);
		}

		void OpenLevels()
		{
			manager = CampaignManager.EnsureInstance();
			var builder = new StringBuilder();
			for (int i = 0; i < manager.Missions.Count; i++)
			{
				var mission = manager.Missions[i];
				bool unlocked = i <= SaveSystem.Data.highestUnlockedMission;
				bool completed = SaveSystem.Data.IsCompleted(mission.index);
				builder.AppendLine((unlocked ? "[ " + (i + 1) + " ]  " : "[ LOCKED ]  ") +
					mission.displayName + "   " + mission.mode +
					(completed ? "   ✔" : string.Empty));
			}
			levelListText.text = builder.ToString();
			builder.AppendLine();
			builder.AppendLine("LOCAL RECORDS (TOP 5)");
			var runs = SaveSystem.Data.bestRuns;
			if (runs == null || runs.Count == 0)
			{
				builder.AppendLine("  --");
			}
			else
			{
				for (int i = 0; i < runs.Count; i++)
					builder.AppendLine("  #" + (i + 1) + "   " + runs[i].ToString("N0"));
			}
			levelListText.text = builder.ToString();
			levelPanel.SetActive(true);
		}

		void BuildShopPanel()
		{
			var panel = UiFactory.FullScreenPanel(canvas.transform, "Shop", new Color(0.02f, 0.03f, 0.05f, 0.94f));
			shopPanel = panel.gameObject;

			var title = UiFactory.CreateText(panel, "Title", "UPGRADE SHOP", 48, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 280f), new Vector2(800f, 70f));

			shopInfo = UiFactory.CreateText(panel, "Info", "", 26, TextAnchor.MiddleCenter);
			UiFactory.Anchor(shopInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 210f), new Vector2(800f, 50f));

			string[] names = { "WEAPON DAMAGE", "AMMO CAPACITY", "ARMOR / MAX HP" };
			for (int i = 0; i < 3; i++)
			{
				var kind = (UpgradeKind)i;
				UiFactory.CreateButton(panel, "Buy" + i, names[i] + "   BUY", new Vector2(0f, 110f - i * 90f), new Vector2(560f, 58f), () =>
				{
					var system = UpgradeSystem.EnsureInstance();
					if (system.Buy(kind)) RefreshShop();
				});
			}

			UiFactory.CreateButton(panel, "Back", "BACK", new Vector2(0f, -220f), new Vector2(320f, 56f), () => shopPanel.SetActive(false));
			shopPanel.SetActive(false);
		}

		void OpenShop()
		{
			RefreshShop();
			shopPanel.SetActive(true);
		}

		void RefreshShop()
		{
			var system = UpgradeSystem.EnsureInstance();
			shopInfo.text = "CREDITS  " + SaveSystem.Data.currency.ToString("N0") +
				"    ·    DMG LV " + system.GetLevel(UpgradeKind.WeaponDamage) +
				"    AMMO LV " + system.GetLevel(UpgradeKind.AmmoCapacity) +
				"    HP LV " + system.GetLevel(UpgradeKind.ArmorHealth);
		}

		void BuildSettingsPanel()
		{
			var panel = UiFactory.FullScreenPanel(canvas.transform, "Settings", new Color(0.02f, 0.03f, 0.05f, 0.94f));
			settingsPanel = panel.gameObject;

			var title = UiFactory.CreateText(panel, "Title", "SETTINGS", 48, TextAnchor.MiddleCenter);
			UiFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 260f), new Vector2(800f, 70f));

			settingsInfo = UiFactory.CreateText(panel, "Info", "", 26, TextAnchor.MiddleCenter);
			UiFactory.Anchor(settingsInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
				new Vector2(0f, 150f), new Vector2(900f, 160f));

			UiFactory.CreateButton(panel, "SensDown", "SENS -", new Vector2(-240f, 40f), new Vector2(200f, 54f), () => ChangeSensitivity(-0.2f));
			UiFactory.CreateButton(panel, "SensUp", "SENS +", new Vector2(240f, 40f), new Vector2(200f, 54f), () => ChangeSensitivity(0.2f));
			UiFactory.CreateButton(panel, "VolDown", "VOL -", new Vector2(-240f, -40f), new Vector2(200f, 54f), () => ChangeVolume(-0.1f));
			UiFactory.CreateButton(panel, "VolUp", "VOL +", new Vector2(240f, -40f), new Vector2(200f, 54f), () => ChangeVolume(0.1f));
			UiFactory.CreateButton(panel, "Back", "BACK", new Vector2(0f, -200f), new Vector2(320f, 56f), () => settingsPanel.SetActive(false));

			settingsPanel.SetActive(false);
		}

		void OpenSettings()
		{
			RefreshSettings();
			settingsPanel.SetActive(true);
		}

		void RefreshSettings()
		{
			settingsInfo.text = "MOUSE SENSITIVITY   " + SaveSystem.Data.sensitivity.ToString("0.0") +
				"\nMASTER VOLUME      " + Mathf.RoundToInt(SaveSystem.Data.volume * 100f) + "%" +
				"\nGAMEPAD            LEFT STICK MOVE · RIGHT STICK / MOUSE AIM (LEGACY AXES)";
		}

		void ChangeSensitivity(float delta)
		{
			SaveSystem.Data.sensitivity = Mathf.Clamp(SaveSystem.Data.sensitivity + delta, 0.4f, 8f);
			SaveSystem.Save();
			RefreshSettings();
		}

		void ChangeVolume(float delta)
		{
			SaveSystem.Data.volume = Mathf.Clamp01(SaveSystem.Data.volume + delta);
			AudioListener.volume = SaveSystem.Data.volume;
			SaveSystem.Save();
			RefreshSettings();
		}

		public static MainMenuUI EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<MainMenuUI>();
			if (existing != null) return existing;
			var go = new GameObject("[MainMenuUI]");
			return go.AddComponent<MainMenuUI>();
		}
	}
}