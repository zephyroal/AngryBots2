// URP port: 原 AngryBots/Fallback（BiRP 顶点光照）的最小等价实现
Shader "AngryBots/Fallback"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

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
                half4 _Color;
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
                return half4(tex.rgb * _Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}