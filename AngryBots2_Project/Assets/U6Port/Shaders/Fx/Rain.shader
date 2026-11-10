// AngryBots / URP port —— 雨丝（顶点沿 texcoord1 偏移）
Shader "AngryBots/FX/Rain"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
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
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionOS = v.positionOS.xyz;
                // 原版: v.vertex.yzx += v.texcoord1.xyy
                positionOS.yzx += float3(v.uv2.xyy);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.positionCS = TransformObjectToHClip(positionOS);
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