// AngryBots / URP port —— 激光瞄准镜
Shader "AngryBots/FX/LaserScope"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _NoiseTex ("NoiseTex", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Reflection" = "LaserScope" "Queue" = "Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../ABUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv, _NoiseTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) *
                       SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv2);
            }
            ENDHLSL
        }
    }

    FallBack Off
}