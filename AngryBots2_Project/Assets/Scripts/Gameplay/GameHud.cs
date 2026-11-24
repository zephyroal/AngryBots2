using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD：血条、弹药 / 武器、波次、倒计时、结算面板与受击红闪。
/// 通过事件订阅刷新，避免每帧拼接字符串。
/// </summary>
public class GameHud : MonoBehaviour
{
	[Header("Health")]
	public Image healthFill;
	public Text healthText;

	[Header("Weapon")]
	public Text ammoText;

	[Header("Mission")]
	public Text waveText;
	public Text timeText;

	[Header("Result")]
	public GameObject resultPanel;
	public Text resultText;

	[Header("Feedback")]
	public Image damageFlash;
	public Text hintText;

	[Header("Combat Feedback")]
	public Image[] hitMarkerParts;
	public Text waveBanner;

	[Header("Power Ups")]
	public Text powerUpText;

	/// <summary>命中 / 击杀标记触发次数，便于自动化验证。</summary>
	public int HitFlashCount { get; private set; }
	public int KillFlashCount { get; private set; }

	PlayerHealth playerHealth;
	WeaponSystem weapons;
	WaveDirector director;
	PowerUpSystem powerUps;
	readonly System.Text.StringBuilder powerUpBuilder = new System.Text.StringBuilder(64);
	float powerUpRefreshTimer;

	float flashAlpha;
	float hitMarkerAlpha;
	float bannerAlpha;
	Color hitMarkerColor = Color.white;
	int lastTimeSeconds = -1;
	int lastWave = -1;
	int lastAlive = -1;
	int lastCountdown = -2;
	float hintClearAt;

	void Start()
	{
		EnsureFillSprite();
		EnsurePowerUpText();

		var player = GameObject.FindGameObjectWithTag("Player");
		if (player != null)
		{
			playerHealth = player.GetComponent<PlayerHealth>();
			weapons = player.GetComponent<WeaponSystem>();
			powerUps = player.GetComponent<PowerUpSystem>();
			if (powerUps != null)
			{
				powerUps.Activated += OnPowerUpActivated;
				powerUps.Expired += OnPowerUpExpired;
			}
		}

		director = FindObjectOfType<WaveDirector>();

		if (playerHealth != null)
		{
			playerHealth.HealthChanged += OnHealthChanged;
			playerHealth.Damaged += OnDamaged;
			OnHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
		}

		if (weapons != null)
		{
			weapons.AmmoChanged += OnAmmoChanged;
			OnAmmoChanged();
		}

		if (director != null)
		{
			director.WaveStarted += OnWaveStarted;
			director.MissionEnded += OnMissionEnded;
			OnWaveStarted();
		}

		if (resultPanel != null) resultPanel.SetActive(false);
		if (damageFlash != null) damageFlash.color = new Color(1f, 0.1f, 0.05f, 0f);
		if (hintText != null) hintText.text = "";

		ProjectileBehaviour.EnemyHit += OnEnemyHit;
		EnemyHealth.EnemyDied += OnEnemyDied;
		EliteAffixHost.Applied += OnEliteAffixApplied;

		SetHitMarkerAlpha(0f);
		if (waveBanner != null) waveBanner.color = new Color(1f, 0.9f, 0.55f, 0f);
	}

	/// <summary>老场景的 HUD 是在编辑器里装配的，没有增益文本时运行期自动补一个，避免要重跑装配脚本。</summary>
	void EnsurePowerUpText()
	{
		if (powerUpText != null) return;

		var canvas = GetComponentInParent<Canvas>();
		if (canvas == null) canvas = FindObjectOfType<Canvas>();
		if (canvas == null) return;

		var go = new GameObject("PowerUpText", typeof(RectTransform), typeof(Text));
		go.transform.SetParent(canvas.transform, false);

		var rect = go.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.zero;
		rect.pivot = Vector2.zero;
		rect.anchoredPosition = new Vector2(36f, 100f);
		rect.sizeDelta = new Vector2(760f, 28f);

		var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

		var text = go.GetComponent<Text>();
		text.font = font;
		text.fontSize = 20;
		text.alignment = TextAnchor.MiddleLeft;
		text.color = new Color(0.55f, 0.95f, 1f);
		text.raycastTarget = false;
		powerUpText = text;
	}

	void EnsureFillSprite()	{
		// Image 没有 Sprite 时会忽略 Filled 模式，血条需要一张最小白色 Sprite。
		if (healthFill == null || healthFill.sprite != null) return;

		var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
		var pixels = new Color[16];
		for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
		texture.SetPixels(pixels);
		texture.Apply();
		healthFill.sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
		healthFill.type = Image.Type.Filled;
		healthFill.fillMethod = Image.FillMethod.Horizontal;
	}

	void OnDestroy()
	{
		ProjectileBehaviour.EnemyHit -= OnEnemyHit;
		EnemyHealth.EnemyDied -= OnEnemyDied;
		EliteAffixHost.Applied -= OnEliteAffixApplied;

		if (powerUps != null)
		{
			powerUps.Activated -= OnPowerUpActivated;
			powerUps.Expired -= OnPowerUpExpired;
		}

		if (playerHealth != null)
		{
			playerHealth.HealthChanged -= OnHealthChanged;
			playerHealth.Damaged -= OnDamaged;
		}
		if (weapons != null) weapons.AmmoChanged -= OnAmmoChanged;
		if (director != null)
		{
			director.WaveStarted -= OnWaveStarted;
			director.MissionEnded -= OnMissionEnded;
		}
	}

	void Update()
	{
		if (damageFlash != null && flashAlpha > 0f)
		{
			flashAlpha = Mathf.Max(0f, flashAlpha - Time.deltaTime * 2.2f);
			damageFlash.color = new Color(1f, 0.1f, 0.05f, flashAlpha * 0.45f);
		}

		if (hitMarkerAlpha > 0f)
		{
			hitMarkerAlpha = Mathf.Max(0f, hitMarkerAlpha - Time.deltaTime * 4.5f);
			SetHitMarkerAlpha(hitMarkerAlpha);
		}

		if (bannerAlpha > 0f)
		{
			bannerAlpha = Mathf.Max(0f, bannerAlpha - Time.deltaTime * 0.7f);
			if (waveBanner != null) waveBanner.color = new Color(1f, 0.9f, 0.55f, bannerAlpha);
		}

		// 增益倒计时（0.2s 刷新一次，避免每帧拼字符串）
		powerUpRefreshTimer -= Time.deltaTime;
		if (powerUpText != null && powerUpRefreshTimer <= 0f)
		{
			powerUpRefreshTimer = 0.2f;
			RefreshPowerUps();
		}

		// 增益提示自动清除
		if (hintClearAt > 0f && Time.unscaledTime >= hintClearAt)
		{
			hintClearAt = 0f;
			if (hintText != null) hintText.text = "";
		}

		if (director == null) return;

		int seconds = Mathf.CeilToInt(director.TimeLeft);
		if (seconds != lastTimeSeconds)
		{
			lastTimeSeconds = seconds;
			if (timeText != null) timeText.text = "TIME  " + seconds;
		}

		if (waveText != null)
		{
			// 只在波次 / 剩余敌人数 / 倒计时秒数变化时重建字符串（原先每帧拼 2~3 次，持续产生 GC）
			int countdown = director.NextWaveCountdown > 0f
				? Mathf.CeilToInt(director.waveDelay - director.NextWaveCountdown)
				: -1;

			if (director.Wave != lastWave || director.AliveEnemies != lastAlive || countdown != lastCountdown)
			{
				lastWave = director.Wave;
				lastAlive = director.AliveEnemies;
				lastCountdown = countdown;

				waveText.text = "WAVE " + director.Wave + " / " + director.wavesToWin
					+ "    ENEMIES " + director.AliveEnemies;
				if (countdown >= 0) waveText.text += "    NEXT WAVE IN " + countdown;
			}
		}

		if (director.MissionOver && Input.GetKeyDown(KeyCode.R))
			director.Restart();
	}

	/// <summary>精英带词缀出场时提示一次。</summary>
	void OnEliteAffixApplied(EliteAffixHost host)
	{
		if (hintText == null || host == null) return;
		hintText.text = "ELITE  " + EliteAffixHost.Label(host.affix);
		hintClearAt = Time.unscaledTime + 1.6f;
	}

	/// <summary>拾取增益时在提示行弹一条短提示。</summary>
	void OnPowerUpActivated(PowerUpKind kind, float seconds)
	{
		if (hintText == null) return;
		hintText.text = PowerUpLabel(kind) + "  " + Mathf.RoundToInt(seconds) + "s";
		hintClearAt = Time.unscaledTime + 1.6f;
	}

	void OnPowerUpExpired(PowerUpKind kind)
	{
		if (hintText == null) return;
		hintText.text = PowerUpLabel(kind) + " ENDED";
		hintClearAt = Time.unscaledTime + 1.2f;
	}

	static string PowerUpLabel(PowerUpKind kind)
	{
		switch (kind)
		{
			case PowerUpKind.RapidFire: return "RAPID FIRE";
			case PowerUpKind.Shield: return "SHIELD";
			default: return "DAMAGE BOOST";
		}
	}

	/// <summary>把当前生效的增益与剩余秒数拼成一行（无增益时清空）。</summary>
	void RefreshPowerUps()
	{
		if (powerUpText == null) return;

		if (powerUps == null || !powerUps.AnyActive)
		{
			if (powerUpText.text.Length > 0) powerUpText.text = "";
			return;
		}

		powerUpBuilder.Length = 0;
		AppendBuff(PowerUpKind.DamageBoost, "DMG");
		AppendBuff(PowerUpKind.RapidFire, "RATE");
		AppendBuff(PowerUpKind.Shield, "SHIELD");

		powerUpText.text = powerUpBuilder.ToString();
	}

	void AppendBuff(PowerUpKind kind, string label)
	{
		float remaining = powerUps.Remaining(kind);
		if (remaining <= 0f) return;

		if (powerUpBuilder.Length > 0) powerUpBuilder.Append("    ");
		powerUpBuilder.Append(label).Append(' ').Append(remaining.ToString("0.0")).Append('s');
	}

	void OnHealthChanged(int current, int max)	{
		if (healthFill != null)
		{
			float ratio = max > 0 ? (float)current / max : 0f;
			healthFill.fillAmount = ratio;
			healthFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.35f), ratio);
		}
		if (healthText != null) healthText.text = current + " / " + max;
	}

	void OnDamaged()
	{
		flashAlpha = 1f;
	}

	void OnAmmoChanged()
	{
		if (weapons == null || ammoText == null) return;

		string ammo = weapons.Mode == WeaponSystem.WeaponMode.Rifle
			? weapons.RifleAmmo + " / " + weapons.rifleMagSize
			: (weapons.Mode == WeaponSystem.WeaponMode.Shotgun
				? weapons.ShotgunAmmo + " / " + weapons.shotgunMagSize
				: (weapons.Mode == WeaponSystem.WeaponMode.Sniper
					? weapons.SniperAmmo + " / " + weapons.sniperMagSize
					: (weapons.Mode == WeaponSystem.WeaponMode.Launcher
						? weapons.LauncherAmmo + " / " + weapons.launcherMagSize
						: weapons.SmgAmmo + " / " + weapons.smgMagSize)));

		ammoText.text = (weapons.Reloading ? "RELOADING...  " : "") +
			"AMMO  " + ammo +
			"    GRENADE " + weapons.Grenades +
			"\n[1] RIFLE   [2] SHOTGUN   [3] SMG   [4] SNIPER   [5] LAUNCHER   [R] RELOAD   [RMB] GRENADE";
	}

	void OnWaveStarted()
	{
		if (hintText != null && director != null)
			hintText.text = "WAVE " + director.Wave;
		if (waveBanner != null && director != null)
		{
			waveBanner.text = "WAVE " + director.Wave;
			bannerAlpha = 1f;
			waveBanner.color = new Color(1f, 0.9f, 0.55f, 1f);
		}
	}

	void OnEnemyHit(Vector3 position)
	{
		HitFlashCount++;
		hitMarkerColor = Color.white;
		hitMarkerAlpha = 1f;
		SetHitMarkerAlpha(1f);
	}

	void OnEnemyDied(Vector3 position)
	{
		KillFlashCount++;
		hitMarkerColor = new Color(1f, 0.45f, 0.2f);
		hitMarkerAlpha = 1f;
		SetHitMarkerAlpha(1f);
	}

	void SetHitMarkerAlpha(float alpha)
	{
		if (hitMarkerParts == null) return;
		for (int i = 0; i < hitMarkerParts.Length; i++)
		{
			var part = hitMarkerParts[i];
			if (part == null) continue;
			var color = hitMarkerColor;
			color.a = alpha;
			part.color = color;
		}
	}

	void OnMissionEnded(bool victory)
	{
		if (resultPanel != null) resultPanel.SetActive(true);
		if (resultText != null)
		{
			resultText.text = victory
				? "MISSION COMPLETE\n<size=28>All " + director.wavesToWin + " waves cleared - Press R to play again</size>"
				: "MISSION FAILED\n<size=28>Press R to try again</size>";
			resultText.color = victory ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.45f, 0.4f);
		}
	}
}