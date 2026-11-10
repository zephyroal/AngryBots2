// URP port: 原 AngryBots/Character/CharacterSelfIlluminationReflective
Shader "AngryBots/Character/CharacterSelfIlluminationReflective"
{
    Properties
    {
        _MainTex ("Base (RGB) Gloss (A)", 2D) = "grey" {}
        _BumpMap ("Normalmap", 2D) = "bump" {}
        _Cube ("Cube", CUBE) = "black" {}
        _SelfIllumStrength ("_SelfIllumStrength", Range(0.0, 1.5)) = 1.0
        _RoomReflectionAmount ("RoomReflectionAmount", Range(0.0, 3.5)) = 3.0
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
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _SelfIllumStrength;
                half _RoomReflectionAmount;
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
                float3 refl = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, ABReflectionDir(i.normalWS, i.positionWS), 0).rgb;
                float3 emission = tex.rgb * tex.a * _SelfIllumStrength + refl * tex.a * _RoomReflectionAmount;
                return half4(ABShade(tex.rgb, i.normalWS, emission), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}