// EMBER - three-colour gradient sky with sun disc, glow and stars.
// _Blend (0..1) cross-fades palette A (night) to palette B (dawn); SkyBlend.cs animates it in the finale.
Shader "Ember/SkyGradient"
{
    Properties
    {
        _TopA ("Top A", Color) = (0.06, 0.07, 0.16, 1)
        _HorizonA ("Horizon A", Color) = (0.20, 0.17, 0.33, 1)
        _BottomA ("Bottom A", Color) = (0.03, 0.03, 0.07, 1)
        _TopB ("Top B", Color) = (0.24, 0.36, 0.62, 1)
        _HorizonB ("Horizon B", Color) = (1.00, 0.70, 0.52, 1)
        _BottomB ("Bottom B", Color) = (0.55, 0.42, 0.50, 1)
        _Blend ("Blend A -> B", Range(0, 1)) = 0
        _HorizonSharpness ("Horizon sharpness", Range(0.2, 6)) = 1.6
        _SunDirection ("Sun direction", Vector) = (0.3, 0.08, 1, 0)
        [HDR] _SunColor ("Sun colour", Color) = (4, 2.6, 1.6, 1)
        _SunSize ("Sun size", Range(0.0005, 0.1)) = 0.012
        _SunGlow ("Sun glow", Range(0, 3)) = 0.6
        _SunVisible ("Sun visible", Range(0, 1)) = 0
        _Stars ("Star density", Range(0, 1)) = 0.6
        _StarBrightness ("Star brightness", Range(0, 6)) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopA, _HorizonA, _BottomA, _TopB, _HorizonB, _BottomB;
                half _Blend, _HorizonSharpness;
                float4 _SunDirection;
                half4 _SunColor;
                half _SunSize, _SunGlow, _SunVisible, _Stars, _StarBrightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 top = lerp(_TopA.rgb, _TopB.rgb, _Blend);
                half3 hor = lerp(_HorizonA.rgb, _HorizonB.rgb, _Blend);
                half3 bot = lerp(_BottomA.rgb, _BottomB.rgb, _Blend);
                half up = pow(saturate(h), 1.0 / _HorizonSharpness);
                half down = pow(saturate(-h), 0.6);
                half3 col = h >= 0 ? lerp(hor, top, up) : lerp(hor, bot, down);

                float3 sd = normalize(_SunDirection.xyz);
                float s = dot(d, sd);
                half disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.6, s);
                half glow = pow(saturate(s), 6.0) * 0.35 + pow(saturate(s), 48.0) * 0.6;
                col += _SunColor.rgb * (disc + glow * _SunGlow) * _SunVisible;

                float3 cell = floor(d * 260.0);
                float r = Hash31(cell);
                half star = step(1.0 - 0.0035 * _Stars, r) * saturate(h * 5.0) * (1.0 - _Blend);
                star *= 0.6 + 0.4 * sin(_Time.y * (1.0 + r * 3.0) + r * 40.0);
                col += star * _StarBrightness;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
