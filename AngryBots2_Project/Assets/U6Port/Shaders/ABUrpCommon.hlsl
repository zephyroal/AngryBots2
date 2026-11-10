#ifndef AB_URP_COMMON_INCLUDED
#define AB_URP_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP 的 ShaderLibrary 不提供 UnityCG 的 fixed/lowp typedef，旧 shader 里大量使用
#define fixed half
#define fixed2 half2
#define fixed3 half3
#define fixed4 half4
#define fixed2x2 half2x2
#define fixed3x3 half3x3
#define fixed4x4 half4x4

// ---- 光照明亮图 ----------------------------------------------------------
// URP 已经在顶点里做了 unity_LightmapST 变换，这里直接取 transform 后的 UV。
float2 AB_LightmapUV(float4 texcoord1)
{
    return texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
}

half3 AB_SampleLightmap(float2 uvLM, half3 normalWS)
{
#if defined(LIGHTMAP_ON)
    return SampleLightmap(uvLM, normalWS);
#else
    return half3(1, 1, 1);
#endif
}

// ---- 简单的 URP 顶点光照（替代 built-in 的 unity_LightPosition） ----------
half3 AB_SimpleLighting(half3 positionWS, half3 normalWS)
{
    Light mainLight = GetMainLight();
    half ndl = saturate(dot(normalWS, mainLight.direction));
    half3 color = mainLight.color * ndl;

    half3 ambient = SampleSH(normalWS);
    return color + ambient;
}

// ---- 切线空间 ------------------------------------------------------------
void AB_TangentBasis(float3 normalOS, float4 tangentOS, out half3 t, out half3 b, out half3 n)
{
    n = TransformObjectToWorldNormal(normalOS);
    t = TransformObjectToWorldDir(tangentOS.xyz);
    b = cross(n, t) * (tangentOS.w * GetOddNegativeScale());
}

// ---- ShadowCaster / DepthOnly --------------------------------------------
float3 _LightDirection;
float3 _LightPosition;

struct ABShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

struct ABShadowVaryings
{
    float4 positionCS : SV_POSITION;
};

ABShadowVaryings ABShadowVertex(ABShadowAttributes i)
{
    ABShadowVaryings o;
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
    return o;
}

half4 ABShadowFragment(ABShadowVaryings i) : SV_Target
{
    return 0;
}

#endif // AB_URP_COMMON_INCLUDED