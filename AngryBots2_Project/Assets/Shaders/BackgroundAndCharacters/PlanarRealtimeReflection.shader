// URP port: 原 AngryBots/PlanarRealtimeReflection
Shader "AngryBots/PlanarRealtimeReflection"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _ReflectionTex ("Internal reflection", 2D) = "black" {}
        _CubeReflTex ("Cube", CUBE) = "black" {}
        _Normals ("Normal", 2D) = "bump" {}
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
            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            TEXTURECUBE(_CubeReflTex); SAMPLER(sampler_CubeReflTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
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
                float2 screenUV = i.positionCS.xy / _ScreenParams.xy;
                float3 planar = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, screenUV).rgb;
                float3 cube = SAMPLE_TEXTURECUBE_LOD(_CubeReflTex, sampler_CubeReflTex, ABReflectionDir(i.normalWS, i.positionWS), 0).rgb;
                return half4(ABShade(albedo, i.normalWS, planar * 0.7 + cube * 0.3), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}