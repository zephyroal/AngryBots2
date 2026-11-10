using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 恢复 BiRP 版 MobileBloom/ColoredNoise 的受伤反馈：
/// 原实现靠 OnRenderImage 改 bloom 的 agonyTint 与 noise 的 localNoiseAmount，
/// 在 URP 下这两个回调不再执行，这里把同样的状态映射到全局 Volume 的
/// ColorAdjustments（红移/降饱和）与 FilmGrain（噪点）。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class UrpLegacyDamageFeedback : MonoBehaviour
{
    public MobileBloom bloom;
    public ColoredNoise noise;

    [Tooltip("受伤时画面偏红的强度")]
    [Range(0f, 1f)] public float tintStrength = 0.65f;

    [Tooltip("受伤时饱和度下降")]
    [Range(0f, 100f)] public float desaturation = 30f;

    [Tooltip("基础噪点强度（对应 ColoredNoise.globalNoiseAmount）")]
    [Range(0f, 1f)] public float baseGrain = 0.12f;

    Volume volume;
    VolumeProfile profile;
    ColorAdjustments grading;
    FilmGrain grain;
    float lastAgony = -1f;
    float lastLocalNoise = -1f;

    void Start()
    {
        if (bloom == null) bloom = GetComponent<MobileBloom>();
        if (noise == null) noise = GetComponent<ColoredNoise>();

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.DontSave;

        grading = profile.Add<ColorAdjustments>(true);
        grading.colorFilter.Override(Color.white);
        grading.saturation.Override(0f);

        grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(baseGrain);
        grain.response.Override(0.8f);

        var go = new GameObject("~LegacyDamageFeedbackVolume") { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f; // 覆盖场景的 Global Volume
        volume.weight = 0f;     // 未受伤时不参与混合
        volume.sharedProfile = profile;
    }

    void Update()
    {
        if (bloom == null && noise == null) return;

        float agony = bloom != null ? bloom.agonyTint : 0f;
        float localNoise = noise != null ? Mathf.Abs(noise.localNoiseAmount) : 0f;

        if (Mathf.Approximately(agony, lastAgony) && Mathf.Approximately(localNoise, lastLocalNoise)) return;
        lastAgony = agony;
        lastLocalNoise = localNoise;

        // 该 Volume 优先级高于场景全局 Volume，只有在真正受伤/噪点抖动时才接管分级，
        // 否则会把基础 ColorAdjustments 完全压掉。
        float weight = Mathf.Clamp01(Mathf.Max(agony, localNoise));
        volume.weight = weight;
        if (weight <= 0f) return;

        Color tint = Color.Lerp(Color.white, new Color(1f, 0.42f, 0.38f), Mathf.Clamp01(agony * tintStrength));
        grading.colorFilter.Override(tint);
        grading.saturation.Override(-desaturation * Mathf.Clamp01(agony));
        grain.intensity.Override(Mathf.Clamp01(baseGrain + localNoise));
    }

    void OnDestroy()
    {
        if (volume != null) Destroy(volume.gameObject);
        if (profile != null) Destroy(profile);
    }
}