// U6UrpCommon.hlsl
// -----------------------------------------------------------------------------
// P1 资产移植 —— U6 legacy shader 的 URP 公共 include
//
// U6 里的 11 个 Built-in legacy fx shader（Additive / Multiply / ParticleAdditive /
// ScreenRefract / Rain ...）在 URP 下无法工作（它们用 UNITY_MATRIX_MVP、unity_Fog
// 等旧宏，且没有 UniversalForward Pass）。这里统一提供 URP 下的等价工具函数。
//
// 设计原则：
//   * 保持与原 shader 完全相同的属性名（_MainTex / _TintColor / _Color / _InvFade ...）
//     ，这样从 U6 迁入的 .mat 不需要改属性名，只需换 shader 引用。
//   * 混合语义（SrcBlend/DstBlend/ZWrite/RenderType）与 U6 原 shader 逐条对齐，
//     绝不把透明/加色变成不透明。
// -----------------------------------------------------------------------------
#ifndef AB_U6_URP_COMMON_INCLUDED
#define AB_U6_URP_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP ShaderLibrary 没有 UnityCG 的固定宽度 typedef
#ifndef AB_FIXED_TYPEDEFS
#define AB_FIXED_TYPEDEFS
#define fixed  half
#define fixed2 half2
#define fixed3 half3
#define fixed4 half4
#define fixed2x2 half2x2
#define fixed3x3 half3x3
#define fixed4x4 half4x4
#endif

// ---- 软粒子（与 U6 的 unity_SoftParticlesFactor 等价） ----------------------
TEXTURE2D_X(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

half AB_SoftParticleFade(float4 screenPos)
{
#if defined(SOFTPARTICLES_ON)
    float2 uv = screenPos.xy / max(screenPos.w, 1e-4);
    #if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
    #endif
    float sceneZ = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r, _ZBufferParams);
    float partZ = screenPos.w;
    return saturate((sceneZ - partZ) * 4.0); // 简化常量，原 shader 由脚本注入 _InvFade
#else
    return 1.0;
#endif
}

// ---- 屏幕扰动（ScreenRefract 用），URP 下用不透明纹理近似 --------------------
TEXTURE2D_X(_CameraOpaqueTexture);

// ---- 简单主光 + SH 环境光（无贴图物体用） ----------------------------------
half3 AB_SimpleMainLight(half3 normalWS)
{
    Light mainLight = GetMainLight();
    half ndl = saturate(dot(normalWS, mainLight.direction));
    return mainLight.color * ndl + SampleSH(normalWS);
}

// ---- ShadowCaster / DepthOnly 公共顶点 -------------------------------------
float3 _LightDirection;
float3 _LightPosition;

struct ABDepthAttribs
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct ABDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
};

ABDepthVaryings ABDepthVertex(ABDepthAttribs i)
{
    ABDepthVaryings o = (ABDepthVaryings)0;
    float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
    float3 normalWS   = TransformObjectToWorldNormal(i.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = positionCS;
    o.uv = i.uv;
    return o;
}

half4 ABDepthFragment(ABDepthVaryings i) : SV_Target { return 0; }

// ---- Alpha clip 公共（_Cutoff） --------------------------------------------
half AB_AlphaClip(half alpha, half cutoff, half clipOn)
{
    return clipOn > 0.5 ? (alpha - cutoff) : 1;
}

#endif // AB_U6_URP_COMMON_INCLUDED