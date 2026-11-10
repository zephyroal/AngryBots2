using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 伤害飘字的可调参数（放在 <see cref="DamageNumberSpawner"/> 或 <c>EnemyAgent</c> 上共享）。
/// </summary>
[System.Serializable]
public sealed class DamageNumberSettings
{
	[Tooltip("普通伤害颜色。")]
	public Color normalColor = new Color(1f, 0.95f, 0.65f, 1f);

	[Tooltip("暴击 / 弱点颜色。")]
	public Color criticalColor = new Color(1f, 0.55f, 0.15f, 1f);

	[Tooltip("玩家受伤颜色。")]
	public Color playerDamagedColor = new Color(1f, 0.3f, 0.25f, 1f);

	[Tooltip("飘字字符大小。")]
	public float characterSize = 0.16f;

	[Tooltip("向上飘移速度（米/秒）。")]
	public float riseSpeed = 1.6f;

	[Tooltip("飘字持续时间（秒）。")]
	public float lifetime = 0.75f;

	[Tooltip("暴击字号额外倍率。")]
	public float criticalScale = 1.45f;

	[Tooltip("每次生成时在 X 轴额外抖动半径，避免数字重叠。")]
	public float horizontalJitter = 0.22f;
}

/// <summary>
/// 世界空间伤害飘字。由 <see cref="DamageNumberSpawner"/> 从 <see cref="PoolManager"/> 取出，
/// 自行上飘 + 淡出后归还。用 TextMesh（兼容 URP，无需 TMP 字体资产）实现，
/// 坐标始终朝向主摄像机。
/// </summary>
[RequireComponent(typeof(TextMesh))]
public sealed class DamageNumber : MonoBehaviour, PoolManager.IPoolable
{
	TextMesh textMesh;
	MeshRenderer meshRenderer;
	float elapsed;
	float lifetime = 0.75f;
	float riseSpeed = 1.6f;
	float scaleBase = 1f;
	Color baseColor = Color.white;
	Camera billboardCamera;

	/// <summary>飘字是否还在播放。</summary>
	public bool IsPlaying { get; private set; }

	void Awake()
	{
		textMesh = GetComponent<TextMesh>();
		meshRenderer = GetComponent<MeshRenderer>();
		EnsureMaterial();
	}

	/// <summary>播放一次飘字。文本组件缺失（例如 batchmode 无字体）时只做位置动画，不抛异常。</summary>
	public void Play(Vector3 worldPosition, string content, Color color, float duration, float rise, float sizeScale)
	{
		if (textMesh == null) textMesh = GetComponent<TextMesh>();
		EnsureMaterial();

		if (meshRenderer != null) meshRenderer.sortingOrder = 3000;

		transform.position = worldPosition;
		transform.rotation = Quaternion.identity;
		transform.localScale = Vector3.one * Mathf.Max(0.01f, sizeScale);

		if (textMesh != null)
		{
			textMesh.text = content;
			textMesh.color = color;
			textMesh.anchor = TextAnchor.MiddleCenter;
			textMesh.alignment = TextAlignment.Center;
			textMesh.fontSize = 64;
			textMesh.characterSize = 0.06f * Mathf.Max(0.01f, sizeScale);
		}

		baseColor = color;
		lifetime = Mathf.Max(0.05f, duration);
		riseSpeed = rise;
		scaleBase = Mathf.Max(0.01f, sizeScale);
		elapsed = 0f;
		billboardCamera = Camera.main;
		IsPlaying = true;
	}

	void Update()
	{
		if (!IsPlaying) return;

		elapsed += Time.deltaTime;
		float t = Mathf.Clamp01(elapsed / lifetime);

		transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

		if (billboardCamera == null) billboardCamera = Camera.main;
		if (billboardCamera != null)
		{
			// 只绕 Y 轴面向相机，保证数字永远是「立着」的。
			var forward = transform.position - billboardCamera.transform.position;
			forward.y = 0f;
			if (forward.sqrMagnitude > 0.0001f)
				transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
		}

		float fade = 1f - t * t;
		if (textMesh != null)
		{
			var color = baseColor;
			color.a *= fade;
			textMesh.color = color;
		}

		// 末段略微放大，形成弹出感。
		float pop = t < 0.15f ? Mathf.Lerp(0.75f, 1f, t / 0.15f) : 1f;
		transform.localScale = Vector3.one * scaleBase * pop;

		if (t >= 1f) Despawn();
	}

	/// <summary>立即归还池。</summary>
	public void Despawn()
	{
		IsPlaying = false;
		PoolManager.Despawn(gameObject);
	}

	void LateUpdate()
	{
		// 池复用对象在禁用状态下不应继续计费。
		if (!IsPlaying && elapsed > 0f) elapsed = 0f;
	}

	public void OnSpawned()
	{
		elapsed = 0f;
	}

	public void OnDespawned()
	{
		IsPlaying = false;
	}

	void EnsureMaterial()
	{
		if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
		if (meshRenderer == null) return;

		if (meshRenderer.sharedMaterial != null) return;

		var shader = Shader.Find("GUI/Text Shader");
		if (shader == null) shader = Shader.Find("Sprites/Default");
		if (shader == null) shader = Shader.Find("Unlit/Color");
		if (shader == null) return;
		if (cachedDefault != null)
		{
			meshRenderer.sharedMaterial = cachedDefault;
			return;
		}

		var material = new Material(shader);
		material.name = "DamageNumber_Material";
		meshRenderer.sharedMaterial = material;
	}

	static Material cachedDefault;
}

/// <summary>
/// 伤害飘字生成器（场景单例）。订阅 <see cref="GameEvents.EnemyDamaged"/> 与 <see cref="GameEvents.PlayerDamaged"/>，
/// 自动把伤害数字飘到命中点。其他系统也可以直接调静态 <see cref="SpawnEnemyDamage"/> 在自己想要的时机生成。
/// </summary>
[DisallowMultipleComponent]
public sealed class DamageNumberSpawner : MonoBehaviour
{

	/// <summary>场景单例。</summary>
	public static DamageNumberSpawner Instance { get; private set; }

	[Header("Template")]
	[Tooltip("飘字使用的字体。留空则自动取 LegacyRuntime.ttf。")]
	public Font font;

	[Header("Settings")]
	[Tooltip("默认飘字参数。")]
	public DamageNumberSettings settings = new DamageNumberSettings();

	[Tooltip("是否自动订阅事件总线。")]
	public bool autoSubscribe = true;

	[Tooltip("玩家受伤时也飘字。")]
	public bool showPlayerDamage = true;

	[Tooltip("同一位置同时存在的飘字上限，超过后丢弃最老的。")]
	public int maxConcurrent = 48;

	[Tooltip("预制体预热数量。")]
	public int prewarm = 12;

	GameObject template;
	readonly List<DamageNumber> active = new List<DamageNumber>();

	/// <summary>已生成的飘字总数（自检 / 调试用）。</summary>
	public int SpawnedCount { get; private set; }

	/// <summary>当前存活飘字数量。</summary>
	public int ActiveCount { get { return active.Count; } }

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(this);
			return;
		}
		Instance = this;

		if (font == null) font = LoadDefaultFont();
		EnsureTemplate();
	}

	void OnEnable()
	{
		if (autoSubscribe)
		{
			GameEvents.EnemyDamaged += OnEnemyDamaged;
			GameEvents.PlayerDamaged += OnPlayerDamaged;
		}
	}

	void OnDisable()
	{
		if (autoSubscribe)
		{
			GameEvents.EnemyDamaged -= OnEnemyDamaged;
			GameEvents.PlayerDamaged -= OnPlayerDamaged;
		}
	}

	void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	void Update()
	{
		// 清理已被外部归还 / 销毁的引用，保持 active 列表紧凑。
		for (int i = active.Count - 1; i >= 0; i--)
		{
			if (active[i] == null || !active[i].IsPlaying) active.RemoveAt(i);
		}
	}

	void OnEnemyDamaged(GameObject target, float amount, DamageInfo info)
	{
		if (amount <= 0f) return;
		Vector3 point = info.HitPoint == Vector3.zero && target != null
			? target.transform.position + Vector3.up
			: info.HitPoint;

		var local = target != null ? target.GetComponent<EnemyAgent>() : null;
		var localSettings = local != null ? local.damageNumberSettings : null;
		Spawn(point, amount, info.IsCritical, localSettings);
	}

	void OnPlayerDamaged(int current, int max, DamageInfo info)
	{
		if (!showPlayerDamage) return;
		var player = PlayerSource.Player;
		if (player == null) return;
		Spawn(player.position + Vector3.up * 1.8f, info.Amount, true, null, settings.playerDamagedColor);
	}

	/// <summary>
	/// 静态入口：在指定位置生成敌人伤害飘字。没有场景实例时静默不做（避免自检环境刷错误日志）。
	/// </summary>
	public static void SpawnEnemyDamage(Vector3 position, float amount, bool critical, DamageNumberSettings overrideSettings)
	{
		var spawner = Instance;
		if (spawner == null || amount <= 0f) return;
		spawner.Spawn(position, amount, critical, overrideSettings);
	}

	/// <summary>在指定位置生成一次飘字。</summary>
	public void Spawn(Vector3 position, float amount, bool critical, DamageNumberSettings overrideSettings = null, Color? overrideColor = null)
	{
		var config = overrideSettings != null ? overrideSettings : settings;

		if (position == Vector3.zero) return;
		position += new Vector3(
			Random.Range(-config.horizontalJitter, config.horizontalJitter),
			Random.Range(-0.05f, 0.15f),
			Random.Range(-config.horizontalJitter, config.horizontalJitter));

		if (active.Count >= Mathf.Max(1, maxConcurrent) && active.Count > 0)
		{
			var oldest = active[0];
			active.RemoveAt(0);
			if (oldest != null) oldest.Despawn();
		}

		EnsureTemplate();
		var go = PoolManager.Spawn(template, position, Quaternion.identity);
		if (go == null) return;

		var number = go.GetComponent<DamageNumber>();
		if (number == null) number = go.AddComponent<DamageNumber>();

		Color color = overrideColor.HasValue
			? overrideColor.Value
			: (critical ? config.criticalColor : config.normalColor);

		float scale = critical ? config.criticalScale : 1f;
		string content = FormatAmount(amount, critical, overrideColor != null && !critical);
		number.Play(position, content, color, config.lifetime, config.riseSpeed, scale);

		active.Add(number);
		SpawnedCount++;
	}

	/// <summary>清空所有飘字（切场景 / 关 UI 时用）。</summary>
	public void ClearAll()
	{
		for (int i = 0; i < active.Count; i++)
			if (active[i] != null) active[i].Despawn();
		active.Clear();
	}

	static string FormatAmount(float amount, bool critical, bool isPlayerDamage)
	{
		var builder = new StringBuilder(8);
		int rounded = Mathf.Max(1, Mathf.RoundToInt(amount));
		if (isPlayerDamage) builder.Append('-');
		builder.Append(rounded);
		if (critical) builder.Append('!');
		return builder.ToString();
	}

	void EnsureTemplate()
	{
		if (template != null) return;

		template = new GameObject("[DamageNumber]");
		template.transform.SetParent(transform, false);
		template.SetActive(false);

		var resolvedFont = font != null ? font : LoadDefaultFont();
		if (font == null) font = resolvedFont;

		var text = template.AddComponent<TextMesh>();
		text.font = resolvedFont;
		text.fontSize = 64;
		text.characterSize = 0.06f;
		text.anchor = TextAnchor.MiddleCenter;
		text.alignment = TextAlignment.Center;

		var renderer = template.GetComponent<MeshRenderer>();
		if (renderer != null)
		{
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			// 只有拿到字体时才用字体材质，否则交给 DamageNumber 自己找 GUI/Text Shader。
			if (resolvedFont != null && resolvedFont.material != null)
				renderer.sharedMaterial = resolvedFont.material;
		}

		template.AddComponent<DamageNumber>();

		if (prewarm > 0 && Application.isPlaying)
			PoolManager.Instance.Prewarm(template, prewarm);

		template.SetActive(false);
	}

	/// <summary>取内置字体（Unity 2022 里 Arial.ttf 已改名为 LegacyRuntime.ttf）。batchmode 下可能为 null。</summary>
	public static Font LoadDefaultFont()
	{
		Font resource = null;
		try
		{
			resource = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			if (resource == null) resource = Resources.GetBuiltinResource<Font>("Arial.ttf");
		}
		catch (System.Exception)
		{
			// batchmode / 特殊平台下内置字体可能不可用，静默降级。
			resource = null;
		}

		if (resource == null)
		{
			try { resource = Font.CreateDynamicFontFromOSFont("Arial", 16); }
			catch (System.Exception) { resource = null; }
		}
		return resource;
	}

	/// <summary>确保运行时存在实例（幂等）。</summary>
	public static DamageNumberSpawner EnsureInstance()
	{
		if (Instance != null) return Instance;
		var go = new GameObject("[DamageNumberSpawner]");
		return go.AddComponent<DamageNumberSpawner>();
	}
}