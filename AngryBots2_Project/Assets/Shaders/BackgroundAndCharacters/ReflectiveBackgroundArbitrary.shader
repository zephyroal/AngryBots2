// URP port: 原 AngryBots/ReflectiveBackgroundArbitraryGeometry
Shader "AngryBots/ReflectiveBackgroundArbitraryGeometry"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _Cube ("Cube", CUBE) = "black" {}
        _OneMinusReflectivity ("OneMinusReflectivity", Range(0.0, 1.0)) = 0.05
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
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _OneMinusReflectivity;
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
                float3 emission = refl * saturate(1.0 - _OneMinusReflectivity);
                return half4(ABShade(albedo, i.normalWS, emission), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}