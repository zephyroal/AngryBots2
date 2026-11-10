// URP port: 原 AngryBots/ReflectiveBackgroundPlanarGeometry
Shader "AngryBots/ReflectiveBackgroundPlanarGeometry"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Cube ("Cube", Cube) = "" {}
        _2DReflect ("2D Reflection", 2D) = "" {}
        _Normal ("Normal", 2D) = "bump" {}
        _EmissionLM ("Emission (Lightmapper)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../ABLegacyUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            TEXTURE2D(_2DReflect); SAMPLER(sampler_2DReflect);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _EmissionLM;
            CBUFFER_END

            ABVaryings vert(ABAttributes v)
            {
                ABVaryings o = ABVert(v);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(ABVaryings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 refl = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, ABReflectionDir(i.normalWS, i.positionWS), 0).rgb;
                float2 screenUV = i.positionCS.xy / _ScreenParams.xy;
                float3 planar = SAMPLE_TEXTURE2D(_2DReflect, sampler_2DReflect, screenUV).rgb;
                float3 emission = refl * 0.5 + planar * 0.5 + albedo * _EmissionLM;
                return half4(ABShade(albedo, i.normalWS, emission), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}