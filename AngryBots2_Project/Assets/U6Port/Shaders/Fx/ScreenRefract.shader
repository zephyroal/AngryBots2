// AngryBots / URP port —— 屏幕热浪折射
// GrabPass 在 URP 中由 _CameraOpaqueTexture 取代
Shader "AngryBots/FX/ScreenRefract"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _ShimmerDistort ("Distort (in RG channels)", 2D) = "black" {}
        _Distort ("Distort", Range(0.0,0.25)) = 0.05
    }

    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../ABUrpCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShimmerDistort); SAMPLER(sampler_ShimmerDistort);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Distort;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uvScreen = i.screenPos.xy / i.screenPos.w;
                fixed4 normal = SAMPLE_TEXTURE2D(_ShimmerDistort, sampler_ShimmerDistort, i.uv) - 0.5;
                uvScreen += normal.xy * _Distort;

                half4 screen = half4(SampleSceneColor(uvScreen), 1.0);
                screen.a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                return screen;
            }
            ENDHLSL
        }
    }

    FallBack Off
}