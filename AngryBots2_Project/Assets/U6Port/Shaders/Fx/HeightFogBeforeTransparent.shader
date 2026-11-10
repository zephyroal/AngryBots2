// AngryBots / URP port —— 高度雾（全屏四边形，脚本传入 _FrustumCornersWS / _CameraWS）
Shader "Hidden/AngryBots/HeightFogBeforeTransparent"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _FogColor ("FogColor", COLOR) = (.0,.0,.0,.0)
        _Exponent ("Exponent", Range(0.1, 4.0)) = 0.3
        _Y ("Y", Range(-30.0, 30.0)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-1" }

        Pass
        {
            Name "HeightFog"
            Tags { "LightMode"="UniversalForward" }
            ZTest Always
            Cull Off
            ZWrite Off
            Blend OneMinusSrcAlpha SrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../ABUrpCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _FogColor;
                float _Exponent;
                float _Y;
            CBUFFER_END

            // 由 RenderFogPlane.cs 每帧写入
            float4x4 _FrustumCornersWS;
            float4 _CameraWS;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float4 interpolatedRay : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                int index = (int)v.uv.y;

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = ComputeScreenPos(o.positionCS);

                float4 ray = _FrustumCornersWS[index];
                ray.w = index;
                o.interpolatedRay = ray;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.uv.xy / i.uv.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float dpth = Linear01Depth(rawDepth, _ZBufferParams);

                float4 wsDir = (_CameraWS + dpth * i.interpolatedRay);
                float fogIntensity = (_Y - wsDir.y);
                fogIntensity = saturate(exp(-fogIntensity * _Exponent));

                half4 outColor = _FogColor;
                outColor.a = fogIntensity;
                return outColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}