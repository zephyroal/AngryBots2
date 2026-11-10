// AngryBots / URP port —— 雨滴溅射（6 帧序列帧动画）
Shader "AngryBots/FX/RainSplash"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Intensity ("Intensity", Range (0.5, 4.0)) = 1.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcColor

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../ABUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                half timeVal = frac(_Time.z * 0.5 + v.uv2.x) * 2.0;
                o.uv = v.uv;
                o.uv.x = o.uv.x / 6 + floor(timeVal * 6) / 6;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = saturate(1.0 - timeVal) * _Intensity;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}