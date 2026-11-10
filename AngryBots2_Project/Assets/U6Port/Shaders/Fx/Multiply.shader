// AngryBots / URP port —— 正片叠底特效
Shader "AngryBots/FX/Multiply"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        Blend Zero SrcColor

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
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            }
            ENDHLSL
        }
    }

    FallBack Off
}