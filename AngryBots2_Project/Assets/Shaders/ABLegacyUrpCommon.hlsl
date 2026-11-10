#ifndef AB_URP_LEGACY_COMMON
#define AB_URP_LEGACY_COMMON

// AngryBots 旧 BiRP shader 的 URP 最小等价实现：
// 只保留「主光 + 环境光 + 自发光 + 立方体反射」这几个原版视觉要素，
// 不追求 PBR 精度，目的是让 U6 战役场景在 URP 14 下正常着色而不是粉色。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct ABAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    float2 uv1        : TEXCOORD1;
    float4 color      : COLOR;
};

struct ABVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float2 uv1        : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    float3 positionWS : TEXCOORD3;
    float4 color      : COLOR;
};

ABVaryings ABVert(ABAttributes v)
{
    ABVaryings o;
    VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
    VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS);
    o.positionCS = pos.positionCS;
    o.positionWS = pos.positionWS;
    o.normalWS = nrm.normalWS;
    o.uv = v.uv;
    o.uv1 = v.uv1;
    o.color = v.color;
    return o;
}

// 简单 Lambert：主光 + 球谐环境光 + 自发光
float3 ABShade(float3 albedo, float3 normalWS, float3 emission)
{
    Light mainLight = GetMainLight();
    float ndl = saturate(dot(normalize(normalWS), mainLight.direction));
    float3 ambient = SampleSH(normalize(normalWS));
    return albedo * (mainLight.color * ndl + ambient) + emission;
}

float3 ABReflectionDir(float3 normalWS, float3 positionWS)
{
    float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
    return reflect(-viewDir, normalize(normalWS));
}

#endif // AB_URP_LEGACY_COMMON