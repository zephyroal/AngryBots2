using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 版平面反射：用一台镜像相机把场景渲染进 RenderTexture，再写进材质贴图槽。
/// 替代 BiRP 的 ReflectionFx（其 Camera.RenderWithShader / replacement shader 在 URP 下不可用）。
/// 只对应 AngryBots 老场景的低画质平面反射路径（shader LOD 200），默认 Highest 仍走 cube 反射。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class UrpPlanarReflection : MonoBehaviour
{
    [Tooltip("反射平面；为空时用本物体所在平面（y 轴向上）")]
    public Transform planeTransform;

    public LayerMask reflectionMask = ~0;

    [Tooltip("需要写入反射贴图的材质")]
    public Material[] reflectiveMaterials;

    [Tooltip("材质上的贴图属性名，老 shader 用 _2DReflect")]
    public string reflectionSampler = "_2DReflect";

    public float clipPlaneOffset = 0.07f;

    [Range(64, 2048)]
    public int textureSize = 512;

    public Color clearColor = Color.black;

    [Tooltip("渲染反射时临时隐藏的物体（通常是反射平面本身）")]
    public Transform[] hideWhileRendering;

    Camera mirrorCamera;
    RenderTexture reflectionTexture;
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    MaterialPropertyBlock block;

    void OnEnable()
    {
        CreateCamera();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

        if (reflectiveMaterials != null)
        {
            foreach (var m in reflectiveMaterials)
                if (m != null) m.SetTexture(reflectionSampler, null);
        }

        if (mirrorCamera != null)
        {
            mirrorCamera.targetTexture = null;
            if (Application.isPlaying) Destroy(mirrorCamera.gameObject);
            else DestroyImmediate(mirrorCamera.gameObject);
            mirrorCamera = null;
        }

        if (reflectionTexture != null)
        {
            reflectionTexture.Release();
            if (Application.isPlaying) Destroy(reflectionTexture);
            else DestroyImmediate(reflectionTexture);
            reflectionTexture = null;
        }
    }

    void CreateCamera()
    {
        if (mirrorCamera != null) return;

        var go = new GameObject("~PlanarReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
        mirrorCamera = go.AddComponent<Camera>();
        mirrorCamera.CopyFrom(Camera.main);
        mirrorCamera.clearFlags = CameraClearFlags.SolidColor;
        mirrorCamera.backgroundColor = clearColor;
        mirrorCamera.useOcclusionCulling = false;
        mirrorCamera.allowHDR = true;
        mirrorCamera.targetTexture = null;
        mirrorCamera.depth = -100; // 先于主相机渲染
        mirrorCamera.enabled = true; // 由 URP 作为普通相机渲染到 targetTexture

        var data = mirrorCamera.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
        }
    }

    void LateUpdate()
    {
        if (mirrorCamera == null) CreateCamera();
        var cam = Camera.main;
        if (cam == null) return;

        var plane = planeTransform != null ? planeTransform : transform;
        Vector3 normal = plane.up;
        Vector3 planePos = plane.position;

        EnsureTexture(cam);
        UpdateMirrorCamera(cam, planePos, normal);
        mirrorCamera.depth = cam.depth - 1f;

        if (reflectiveMaterials != null)
        {
            foreach (var m in reflectiveMaterials)
                if (m != null) m.SetTexture(reflectionSampler, reflectionTexture);
        }
    }

    void EnsureTexture(Camera cam)
    {
        if (reflectionTexture != null && reflectionTexture.width == textureSize) return;
        if (reflectionTexture != null)
        {
            reflectionTexture.Release();
            DestroyImmediate(reflectionTexture);
        }
        reflectionTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.DefaultHDR)
        {
            name = "~PlanarReflection",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp
        };
        reflectionTexture.Create();
        mirrorCamera.targetTexture = reflectionTexture;
    }

    void UpdateMirrorCamera(Camera cam, Vector3 planePos, Vector3 normal)
    {
        float d = -Vector3.Dot(normal, planePos) - clipPlaneOffset;
        Vector3 camPos = cam.transform.position;

        Vector3 mirroredPos = camPos - 2f * normal * (Vector3.Dot(normal, camPos) + d);
        Vector3 mirroredForward = Vector3.Reflect(cam.transform.forward, normal);
        Vector3 mirroredUp = Vector3.Reflect(cam.transform.up, normal);

        mirrorCamera.transform.position = mirroredPos;
        mirrorCamera.transform.rotation = Quaternion.LookRotation(mirroredForward, mirroredUp);
        mirrorCamera.fieldOfView = cam.fieldOfView;
        mirrorCamera.aspect = cam.aspect;
        mirrorCamera.nearClipPlane = cam.nearClipPlane;
        mirrorCamera.farClipPlane = cam.farClipPlane;
        mirrorCamera.cullingMask = reflectionMask;

        // 斜面裁剪：把镜像相机近平面贴合到反射平面
        Vector4 clipPlane = CameraSpacePlane(mirrorCamera, planePos, normal, 1f);
        mirrorCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);
    }

    Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != mirrorCamera) return;

        hiddenRenderers.Clear();
        if (hideWhileRendering == null) return;
        foreach (var t in hideWhileRendering)
        {
            if (t == null) continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
        }
    }

    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != mirrorCamera) return;
        foreach (var r in hiddenRenderers)
            if (r != null) r.enabled = true;
        hiddenRenderers.Clear();
    }
}