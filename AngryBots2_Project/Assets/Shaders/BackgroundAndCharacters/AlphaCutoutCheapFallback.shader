// URP port: 原 AngryBots/AlphaCutoutCheapFallback
Shader "AngryBots/AlphaCutoutCheapFallback"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _Cube ("Cube", CUBE) = "black" {}
        _Color ("Unused main color for depth texture pickup", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../ABLegacyUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Cutoff;
            CBUFFER_END

            ABVaryings vert(ABAttributes v)
            {
                ABVaryings o = ABVert(v);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(ABVaryings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                float3 refl = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, ABReflectionDir(i.normalWS, i.positionWS), 0).rgb * 0.25;
                float3 color = ABShade(tex.rgb * _Color.rgb, i.normalWS, refl * tex.a);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}