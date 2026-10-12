// 保留历史 shader 名称供现有材料查找；现在只用于稳定的本色霓虹能量。
// 不再把所有技能的贴片变成屏幕故障：形状、颜色、生命周期全部来自实际技能。
// 配色纪律（HANDOFF §6）：黑潮青 + 熔岩橙为主，红色只用于攻击预警，不使用紫色——所以这里不引入品红。
// 黑底 RGB 图片用亮度遮罩；颜色和顶点 alpha 控制所有输出，消退时不会残留亮芯。
Shader "BlackTide/Glitch"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0.3,0.95,0.9,1)
        _Intensity ("Intensity", Float) = 2.2
        _Glitch ("Glitch Amount", Range(0,1)) = 0.55
        _BandH ("Band Height (px)", Float) = 12
        _Rate ("Glitch Rate (Hz)", Float) = 11
        _Chroma ("Chroma Split", Range(0,0.15)) = 0.018
        _Scan ("Scanline", Range(0,1)) = 0.3
        _Flick ("Flicker", Range(0,1)) = 0.45
        _Pix ("Pixelate", Range(0,1)) = 0.3
        _Block ("Block Corruption", Range(0,1)) = 0.35
        _Ghost ("Ghost Echo", Range(0,1)) = 0.35
        _Swap ("Channel Swap", Range(0,1)) = 0.2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _Color;
                float _Intensity, _Glitch, _BandH, _Rate, _Chroma, _Scan, _Flick, _Pix;
                float _Block, _Ghost, _Swap;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };

            float hash12(float2 p)
            {
                p = frac(p * float2(0.0671, 0.2531) + float2(0.8176, 0.2361));
                return frac((p.x + p.y * 51.32) * (p.x + p.y) * 47.07 + p.x);
            }

            V vert (A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.col = i.col;
                return o;
            }

            // Stable, local neon shading. Coverage and lifetime affect every color term.
            half4 frag (V i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float mask = saturate(max(t.r, max(t.g, t.b)) * t.a);
                float lum = dot(t.rgb, float3(0.299, 0.587, 0.114));
                float3 tint = _Color.rgb * i.col.rgb;
                float3 hue = saturate(lerp(lum.xxx, t.rgb, 1.2)) * tint;
                // Monochrome masks inherit their skill color; colored source art retains its detail.
                float3 core = hue * (0.75 + 0.35 * smoothstep(0.5, 0.95, mask));
                float trace = 1.0 - _Scan * 0.2 * step(0.75, frac(i.uv.y * 48.0));
                float coverage = mask * _Color.a * i.col.a;
                return half4(core * _Intensity * trace, coverage);
            }
            ENDHLSL
        }
    }
}
