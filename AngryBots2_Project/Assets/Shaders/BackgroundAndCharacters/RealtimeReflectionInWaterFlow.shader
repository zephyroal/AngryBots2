// URP port: 原 AngryBots/RealtimeReflectionInWaterFlow
Shader "AngryBots/RealtimeReflectionInWaterFlow"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _ReflectionTex ("_ReflectionTex", 2D) = "black" {}
        _FakeReflect ("Fake reflection", 2D) = "black" {}
        _DirectionUv ("Wet scroll direction (2 samples)", Vector) = (1.0,1.0, -0.2,-0.2)
        _TexAtlasTiling ("Tex atlas tiling", Vector) = (8.0,8.0, 4.0,4.0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "../ABLegacyUrpCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            TEXTURE2D(_FakeReflect); SAMPLER(sampler_FakeReflect);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DirectionUv;
                float4 _TexAtlasTiling;
            CBUFFER_END

            ABVaryings vert(ABAttributes v)
            {
                ABVaryings o = ABVert(v);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(ABVaryings i) : SV_Target
            {
                float2 flow = _Time.y * _DirectionUv.xy * 0.05;
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + flow).rgb;
                float2 screenUV = i.positionCS.xy / _ScreenParams.xy;
                float2 distortion = SAMPLE_TEXTURE2D(_FakeReflect, sampler_FakeReflect, i.uv * _TexAtlasTiling.xy + flow).rg - 0.5;
                float3 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, screenUV + distortion * 0.02).rgb;
                return half4(ABShade(albedo, i.normalWS, refl * 0.55), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}