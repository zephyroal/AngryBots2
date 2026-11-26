using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 镜头抖动：渲染前给相机加位移 / 滚转，渲染后立刻还原。
/// <para>
/// 之所以不在 Update / LateUpdate 里改相机变换：跟随脚本与 Cinemachine 都在那里写相机，
/// 谁先谁后不确定会互相覆盖，而且抖动值一旦被跟随循环读到还会形成反馈漂移。
/// 这里用「渲染前后成对偏移」：URP 走 <see cref="RenderPipelineManager"/> 的
/// begin/endCameraRendering（SRP 下 OnPostRender 不保证触发），内置管线回退到 OnPreCull/OnPostRender。
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraShake : MonoBehaviour
{
	public static CameraShake Instance { get; private set; }

	[Header("Limits")]
	[Tooltip("最大位移（米）。")]
	public float maxOffset = 0.32f;

	[Tooltip("最大滚转角（度）。")]
	public float maxRoll = 1.6f;

	/// <summary>渲染期真正施加过偏移的次数（自检用）。为 0 说明渲染回调没被触发。</summary>
	public int AppliedCount { get; private set; }

	/// <summary>当前是否有偏移还没还原（自检用）。帧与帧之间应为 false。</summary>
	public bool OffsetPending { get { return appliedOffset != Vector3.zero || !Mathf.Approximately(appliedRoll, 0f); } }

	/// <summary>最近一次施加的偏移量（自检用）。</summary>
	public Vector3 LastAppliedOffset { get; private set; }

	float strength;
	float duration = 0.15f;
	float endTime;
	Vector3 appliedOffset;
	float appliedRoll;
	Camera targetCamera;

	void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(this); return; }
		Instance = this;
		targetCamera = GetComponent<Camera>();
	}

	void OnEnable()
	{
		RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
	}

	void OnDisable()
	{
		RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
		RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
		RestoreApplied();
	}

	void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	/// <summary>触发一次抖动。strength 0~1，duration 秒（默认 0.15）。</summary>
	public static void Shake(float strength, float duration = 0.15f)
	{
		var target = EnsureInstance();
		if (target == null) return;

		float clamped = Mathf.Clamp01(strength);
		if (clamped <= 0f) return;

		float length = Mathf.Max(0.02f, duration);
		float now = Time.unscaledTime;

		if (now >= target.endTime || clamped > target.strength)
		{
			// 新一波（或更强的一波）：以它自己的强度与时长为基准重新计时。
			target.strength = clamped;
			target.duration = length;
			target.endTime = now + length;
		}
		else
		{
			// 已有更强的抖动在播：只顺延一半，避免连发把强抖动无限拉长。
			target.endTime = Mathf.Max(target.endTime, now + length * 0.5f);
		}
	}

	/// <summary>抖动的衰减曲线：剩余时间比例 × 强度（纯函数，便于自检）。</summary>
	public static float PowerAt(float strength, float remaining01)
	{
		return Mathf.Clamp01(strength) * Mathf.Clamp01(remaining01);
	}

	public static CameraShake EnsureInstance()
	{
		if (Instance != null) return Instance;

		var camera = Camera.main;
		if (camera == null) camera = Object.FindObjectOfType<Camera>();
		if (camera == null) return null;

		var existing = camera.GetComponent<CameraShake>();
		if (existing != null) { Instance = existing; return Instance; }

		return camera.gameObject.AddComponent<CameraShake>();
	}

	/// <summary>剩余强度 0~1（已结束返回 0）。</summary>
	float CurrentPower()
	{
		if (strength <= 0f || Time.unscaledTime >= endTime) return 0f;
		float remaining = Mathf.Clamp01((endTime - Time.unscaledTime) / Mathf.Max(0.02f, duration));
		return PowerAt(strength, remaining);
	}

	void ApplyShake()
	{
		// 上一帧若因异常没还原（相机中途禁用 / 切换渲染目标），先回到原位再加新偏移。
		RestoreApplied();

		float power = CurrentPower();
		if (power <= 0f)
		{
			strength = 0f;
			return;
		}

		appliedOffset = Random.insideUnitSphere * (maxOffset * power);
		appliedOffset.z = 0f;
		appliedRoll = Random.Range(-maxRoll, maxRoll) * power;

		AppliedCount++;
		LastAppliedOffset = appliedOffset;

		transform.position += appliedOffset;
		transform.rotation *= Quaternion.Euler(0f, 0f, appliedRoll);
	}

	void RestoreApplied()
	{
		if (appliedOffset != Vector3.zero) transform.position -= appliedOffset;
		if (!Mathf.Approximately(appliedRoll, 0f)) transform.rotation *= Quaternion.Euler(0f, 0f, -appliedRoll);

		appliedOffset = Vector3.zero;
		appliedRoll = 0f;
	}

	// ─── SRP（URP / HDRP）───

	void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
	{
		if (camera != null && camera == targetCamera) ApplyShake();
	}

	void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
	{
		if (camera != null && camera == targetCamera) RestoreApplied();
	}

	// ─── 内置渲染管线回退 ───

	void OnPreCull()
	{
		if (GraphicsSettings.currentRenderPipeline != null) return;
		ApplyShake();
	}

	void OnPostRender()
	{
		if (GraphicsSettings.currentRenderPipeline != null) return;
		RestoreApplied();
	}
}
