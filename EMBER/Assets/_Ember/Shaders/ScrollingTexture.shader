// EMBER - unlit scrolling texture for URP.
// Uses _MainTex / _MainTex_ST on purpose: the class script ScrollingUVs (Session 3) animates
// "_MainTex" offset, which URP/Lit ignores (it samples _BaseMap). Lava, mist, the cloud sea and
// light beams use this shader so ScrollingUVs drives them unchanged.
Shader "Ember/ScrollingTexture"
{
    Properties
    {
        _MainTex ("Texture (scrolled by ScrollingUVs)", 2D) = "white" {}
        [HDR] _Color ("Tint (HDR)", Color) = (1,1,1,1)
        _Emission ("Brightness", Range(0, 20)) = 1
        _DetailTex ("Detail (self-scrolling)", 2D) = "white" {}
        _DetailSpeed ("Detail scroll speed (xy)", Vector) = (0.02, 0.013, 0, 0)
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.5
        _EdgeFade ("Edge fade (beams)", Range(0, 1)) = 0
        _EdgePower ("Edge fade power", Range(0.1, 8)) = 2
        _Pulse ("Pulse amount", Range(0, 1)) = 0
        _PulseSpeed ("Pulse speed", Float) = 1.5
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DetailTex_ST;
                half4 _Color;
                half _Emission;
                float4 _DetailSpeed;
                half _DetailStrength;
                half _EdgeFade;
                half _EdgePower;
                half _Pulse;
                half _PulseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uvDetail   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 viewDirWS  : TEXCOORD3;
                half4  color      : COLOR;
                half   fogFactor  : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvDetail = TRANSFORM_TEX(v.uv, _DetailTex) + _Time.y * _DetailSpeed.xy;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(p.positionWS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 d = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.uvDetail);
                half detail = lerp(1.0h, d.r * 2.0h, _DetailStrength);
                half pulse = 1.0h + _Pulse * sin(_Time.y * _PulseSpeed * 6.2831h);
                half4 c;
                c.rgb = a.rgb * detail * _Color.rgb * _Emission * pulse * i.color.rgb;
                c.a = saturate(a.a * _Color.a * i.color.a * lerp(1.0h, d.r * 1.5h, _DetailStrength * 0.5h));
                half ndv = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewDirWS))));
                c.a *= lerp(1.0h, pow(ndv, _EdgePower), _EdgeFade);
                c.rgb *= lerp(1.0h, pow(ndv, _EdgePower), _EdgeFade);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
