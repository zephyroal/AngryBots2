// URP port: 原 Self-Illumin/AngryBots/InterlacePatternAdditive
Shader "Self-Illumin/AngryBots/InterlacePatternAdditive"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _TintColor ("TintColor", Color) = (1,1,1,1)
        _InterlacePattern ("InterlacePattern", 2D) = "white" {}
        _Illum ("_Illum", 2D) = "white" {}
        _EmissionLM ("Emission (Lightmapper)", Float) = 1.0
        _Alpha ("Alpha", Range(0, 1)) = 0.5
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
            TEXTURE2D(_InterlacePattern); SAMPLER(sampler_InterlacePattern);
            TEXTURE2D(_Illum); SAMPLER(sampler_Illum);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _EmissionLM;
                half _Alpha;
            CBUFFER_END

            ABVaryings vert(ABAttributes v)
            {
                ABVaryings o = ABVert(v);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(ABVaryings i) : SV_Target
            {
                half3 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half3 pattern = SAMPLE_TEXTURE2D(_InterlacePattern, sampler_InterlacePattern, i.uv).rgb;
                half3 illum = SAMPLE_TEXTURE2D(_Illum, sampler_Illum, i.uv).rgb;
                half3 color = base * _TintColor.rgb * (pattern + illum * _EmissionLM);
                return half4(color, _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}