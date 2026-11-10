// AngryBots / URP port —— 全息隔行扫描（alpha 混合）
Shader "Self-Illumin/AngryBots/InterlacePatternBlend"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _TintColor ("TintColor", Color) = (1,1,1,1)
        _InterlacePattern ("InterlacePattern", 2D) = "white" {}
        _Illum ("_Illum", 2D) = "white" {}
        _EmissionLM ("Emission (Lightmapper)", Float) = 1.0
    }

    SubShader
    {
        Tags {"RenderType" = "Transparent" "Queue" = "Transparent" "Reflection" = "RenderReflectionTransparentBlend" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../ABUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_InterlacePattern); SAMPLER(sampler_InterlacePattern);
            TEXTURE2D(_Illum); SAMPLER(sampler_Illum);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _InterlacePattern_ST;
                half4 _TintColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv, _InterlacePattern) + _Time.xx * _InterlacePattern_ST.zw;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                fixed4 colorTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                fixed4 interlace = SAMPLE_TEXTURE2D(_InterlacePattern, sampler_InterlacePattern, i.uv2);
                return colorTex * interlace * _TintColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}