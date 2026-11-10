using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 受击手感反馈：顿帧（hitstop）、屏幕震动、命中音效。参数全部可在 Inspector 调整。
/// <para>
/// 顿帧通过临时压低 <see cref="Time.timeScale"/> 实现，恢复时使用 unscaled 计时，
/// 因此不会与被压慢的时间互相卡死。场景暂停（timeScale == 0）时不会触发顿帧。
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class HitFeedback : MonoBehaviour
{
	[Header("Hit Stop")]
	[Tooltip("普通命中的顿帧时长（秒，真实时间）。0 表示关闭。")]
	public float hitStopDuration = 0.035f;

	[Tooltip("顿帧期间的时间缩放。")]
	[Range(0.001f, 1f)]
	public float hitStopScale = 0.04f;

	[Tooltip("击杀时额外顿帧时长。")]
	public float killStopBonus = 0.05f;

	[Tooltip("两次顿帧之间的最小间隔（避免霰弹枪多弹丸把时间一直按停）。")]
	public float hitStopCooldown = 0.08f;

	[Header("Camera Shake")]
	[Tooltip("每次命中叠加的震动强度（米）。")]
	public float hitShake = 0.02f;

	[Tooltip("击杀叠加的震动强度（米）。")]
	public float killShake = 0.06f;

	[Tooltip("震动衰减速度。")]
	public float shakeDamping = 12f;

	[Tooltip("震动频率（次/秒）。")]
	public float shakeFrequency = 26f;

	[Tooltip("最大震动幅度（米），防止叠加过猛。")]
	public float maxShake = 0.15f;

	[Header("Audio")]
	[Tooltip("命中音效池（随机播放）。")]
	public AudioClip[] hitClips;

	[Tooltip("暴击音效（优先于普通命中音效）。")]
	public AudioClip[] criticalClips;

	[Tooltip("击杀音效。")]
	public AudioClip[] killClips;

	[Tooltip("命中音效音量。")]
	[Range(0f, 1f)]
	public float hitVolume = 0.35f;

	[Tooltip("自动创建的音效 AudioSource 数量。")]
	public int audioVoices = 3;

	[Header("References")]
	[Tooltip("震动的摄像机。留空则用 Camera.main。")]
	public Camera shakeCamera;

	[Tooltip("是否自动订阅事件总线。")]
	public bool autoSubscribe = true;

	[Tooltip("玩家受伤时也触发反馈（红闪 + 强震动）。")]
	public bool feedbackOnPlayerDamage = true;

	// ─── 运行时状态 ───

	float shakeAmplitude;
	float shakeSeed;
	float stopUntil;
	float nextStopAllowedAt;
	float pendingRestoreTime = 1f;
	bool stopActive;
	float originalTimeScale = 1f;
	Transform cameraParent;
	Vector3 cameraRestPosition;

	readonly List<AudioSource> voices = new List<AudioSource>();
	int nextVoice;

	/// <summary>已触发的顿帧次数（自检 / 调试）。</summary>
	public int HitStopCount { get; private set; }

	/// <summary>已触发的命中音效次数。</summary>
	public int HitAudioCount { get; private set; }

	/// <summary>当前是否有顿帧在生效。</summary>
	public bool IsHitStopActive { get { return stopActive; } }

	/// <summary>当前震动振幅。</summary>
	public float CurrentShake { get { return shakeAmplitude; } }

	void Awake()
	{
		shakeSeed = Random.Range(0f, 100f);
		EnsureVoices();
	}

	void OnEnable()
	{
		if (autoSubscribe)
		{
			GameEvents.EnemyDamaged += OnEnemyDamaged;
			GameEvents.EnemyKilled += OnEnemyKilled;
			if (feedbackOnPlayerDamage) GameEvents.PlayerDamaged += OnPlayerDamaged;
		}
	}

	void OnDisable()
	{
		if (autoSubscribe)
		{
			GameEvents.EnemyDamaged -= OnEnemyDamaged;
			GameEvents.EnemyKilled -= OnEnemyKilled;
			GameEvents.PlayerDamaged -= OnPlayerDamaged;
		}
		RestoreTimeScale();
		RestoreCamera();
	}

	void Update()
	{
		UpdateHitStop();
		UpdateShake();
	}

	/// <summary>触发一次命中反馈（供武器 / AI 直接调用）。</summary>
	public void TriggerHit(Vector3 worldPosition, bool critical = false, bool kill = false)
	{
		if (!kill) PlayAudio(critical && criticalClips != null && criticalClips.Length > 0 ? criticalClips : hitClips);

		if (kill)
		{
			PlayAudio(killClips);
			AddShake(killShake);
			RequestHitStop(hitStopDuration + killStopBonus);
		}
		else
		{
			AddShake(hitShake);
			RequestHitStop(hitStopDuration);
		}
	}

	/// <summary>只触发震屏（例如爆炸）。</summary>
	public void TriggerShake(float amplitude)
	{
		AddShake(amplitude);
	}

	// ─── 顿帧 ───

	void RequestHitStop(float duration)
	{
		if (duration <= 0f) return;
		// 暂停菜单（timeScale == 0）里不能改 timeScale，否则恢复时会误伤暂停状态。
		if (Mathf.Approximately(Time.timeScale, 0f)) return;
		if (Time.unscaledTime < nextStopAllowedAt) return;

		nextStopAllowedAt = Time.unscaledTime + hitStopCooldown;

		if (!stopActive)
		{
			originalTimeScale = Time.timeScale;
			Time.timeScale = Mathf.Min(originalTimeScale, hitStopScale);
			stopActive = true;
			HitStopCount++;
		}
		stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + duration);
	}

	void UpdateHitStop()
	{
		if (!stopActive) return;
		if (Time.unscaledTime < stopUntil) return;
		RestoreTimeScale();
	}

	void RestoreTimeScale()
	{
		if (!stopActive) return;
		stopActive = false;
		// 只还原自己写过的缩放，避免覆盖暂停菜单。
		if (Time.timeScale == hitStopScale || Time.timeScale < originalTimeScale)
			Time.timeScale = originalTimeScale;
	}

	// ─── 震屏 ───

	void AddShake(float amplitude)
	{
		shakeAmplitude = Mathf.Min(maxShake, shakeAmplitude + Mathf.Max(0f, amplitude));
	}

	void UpdateShake()
	{
		var camera = shakeCamera != null ? shakeCamera : Camera.main;

		if (shakeAmplitude <= 0.0005f || camera == null)
		{
			RestoreCamera();
			shakeAmplitude = 0f;
			return;
		}

		if (cameraParent == null)
		{
			cameraParent = camera.transform;
			cameraRestPosition = cameraParent.localPosition;
		}

		float t = Time.unscaledTime * shakeFrequency + shakeSeed;
		Vector3 offset = new Vector3(
			Noise(t, 0.3f) - 0.5f,
			Noise(0.7f, t) - 0.5f,
			Noise(t, t) - 0.5f) * (shakeAmplitude * 2f);

		cameraParent.localPosition = cameraRestPosition + offset;
		shakeAmplitude = Mathf.Max(0f, shakeAmplitude - shakeAmplitude * shakeDamping * Time.unscaledDeltaTime);
	}

	static float Noise(float x, float y)
	{
		return Mathf.PerlinNoise(x, y);
	}

	void RestoreCamera()
	{
		if (cameraParent == null) return;
		cameraParent.localPosition = cameraRestPosition;
		cameraParent = null;
	}

	// ─── 音效 ───

	void EnsureVoices()
	{
		for (int i = voices.Count; i < audioVoices; i++)
		{
			var source = gameObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 0f;
			source.volume = hitVolume;
			voices.Add(source);
		}
	}

	void PlayAudio(AudioClip[] clips)
	{
		if (clips == null || clips.Length == 0 || voices.Count == 0) return;

		var clip = clips[Random.Range(0, clips.Length)];
		if (clip == null) return;

		var source = voices[nextVoice % voices.Count];
		nextVoice++;
		source.volume = hitVolume;
		source.PlayOneShot(clip);
		HitAudioCount++;
	}

	// ─── 事件入口 ───

	void OnEnemyDamaged(GameObject target, float amount, DamageInfo info)
	{
		if (info.Feedback == HitFeedbackLevel.None) return;
		TriggerHit(info.HitPoint, info.IsCritical || info.Feedback == HitFeedbackLevel.Critical, false);
	}

	void OnEnemyKilled(EnemyKind kind, Vector3 position, GameObject killer, int score)
	{
		TriggerHit(position, false, true);
	}

	void OnPlayerDamaged(int current, int max, DamageInfo info)
	{
		AddShake(killShake);
		PlayAudio(criticalClips != null && criticalClips.Length > 0 ? criticalClips : hitClips);
	}

	/// <summary>确保运行时存在实例（幂等）。</summary>
	public static HitFeedback EnsureInstance()
	{
		var existing = Object.FindObjectOfType<HitFeedback>();
		if (existing != null) return existing;
		var go = new GameObject("[HitFeedback]");
		return go.AddComponent<HitFeedback>();
	}
}