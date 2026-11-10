// URP port: 原 AngryBots/SimpleSelfIllumination（基于贴图 alpha 的自发光，透明叠加）
Shader "AngryBots/SimpleSelfIllumination"
{
    Properties
    {
        _MainTex ("Base", 2D) = "grey" {}
        _SelfIllumination ("Self Illumination", Range(0.0,2.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../ABLegacyUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _SelfIllumination;
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
                return half4(tex.rgb * tex.a * _SelfIllumination, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}