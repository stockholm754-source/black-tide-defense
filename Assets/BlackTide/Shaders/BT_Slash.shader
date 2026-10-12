// 斩击弧面 / 刀光条带（默认画法，发光的写实动漫风）：u 沿挥砍方向（0 尾 → 1 头），v 从内缘往外，刀锋在 v = _EdgeV，再往外是外焰
// 分层：内缘偏色的能量面（带流动的丝）→ 主色 → 靠刀锋一条白热带 → 刀锋外的火舌状外焰；头部扫过、尾部追上、从内侧和尾部碎掉；
// 出手瞬间整条过曝（_Flash）。预乘混合：亮处加色，本体同时压暗背景，在亮地面上也有体量
Shader "BlackTide/Slash"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Color ("Color", Color) = (1,0.5,0.16,1)
        _Core ("Core", Color) = (1,0.95,0.8,1)
        _EdgeCol ("Inner Color", Color) = (0.9,0.1,0.4,1)
        _Glow ("Flame Color", Color) = (1,0.8,0.2,1)
        _Intensity ("Intensity", Float) = 2.4
        _Head ("Head", Float) = 1.1
        _Tail ("Tail", Float) = -1
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _Flow ("Flow", Float) = 2
        _Seed ("Seed", Float) = 0
        _EdgeV ("Blade Edge V", Float) = 1
        _Flash ("Flash", Range(0,1)) = 0
        _Shade ("Shade", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST; half4 _Color, _Core, _EdgeCol, _Glow; half _Intensity, _Head, _Tail, _Dissolve, _Flow, _Seed, _EdgeV, _Flash, _Shade;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 frag (V i) : SV_Target
            {
                float edgeV = max(_EdgeV, 0.01);
                float u = i.uv.x, v = i.uv.y / edgeV;                 // v = 1 是刀锋，>1 是外焰
                float d = _Head - u;                                   // 离头部多远（>0 已扫过）
                half sweep = smoothstep(0.0, 0.06, d) * smoothstep(_Tail, _Tail + 0.45, u);
                half nearHead = exp(-max(d, 0.0) * 4.0);
                half vb = saturate(v);

                // 条纹：贴图 R 是沿 u 拉长的长丝、G 是碎纹（都可无缝平铺），两层不同速度往尾部流
                float t = _Time.y * _Flow;
                half n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 0.7 + t * 0.35 + _Seed, vb * 0.8 + _Seed * 0.37)).r;
                half n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 1.3 + t + _Seed * 1.7, vb * 1.2 + 0.31)).g;
                half streak = saturate(n1 * 0.7 + n2 * 0.45);

                // 溶解：内侧、尾部、旧的部分（顶点色 alpha 小）先碎成丝；头部收尖
                half age = 1.0 - i.col.a;
                half tip = 1.0 - smoothstep(0.0, 0.1, d);
                half thr = _Dissolve * 1.3 + (1.0 - vb) * 0.55 + saturate(d) * 0.3 + age * 0.9 + tip * 0.6 - 0.2;
                half cut = smoothstep(thr - 0.07, thr + 0.07, streak);
                half rim = cut * (1.0 - smoothstep(thr + 0.07, thr + 0.24, streak));

                // 本体：内缘偏色 → 主色，面上是亮丝；靠刀锋一条宽的白热带
                half inBody = step(v, 1.0);
                half core = smoothstep(0.6, 0.95, vb);
                half fib = smoothstep(0.42, 0.85, streak);
                half3 face = lerp(_EdgeCol.rgb, _Color.rgb, smoothstep(0.05, 0.55, vb)) * (0.5 + fib * 1.0);
                half3 cb = face + _Core.rgb * (core * (1.0 + nearHead * 1.3) + rim * 0.8);
                half ab = cut * lerp(0.3, 1.0, smoothstep(0.0, 0.7, vb));

                // 外焰：刀锋外一排火舌，贴着刀锋白热、往外变成外焰色，本体薄的地方火舌也短
                half fl = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 2.2 + t * 1.6 + _Seed * 1.3, 0.37 + t * 0.2)).g;
                half fl2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 5.0 + t * 2.3, 0.71 + v)).r;
                half fw = max(1.0 / edgeV - 1.0, 0.0) * (0.25 + 0.75 * fl) * saturate((1.0 - thr) * 2.0 + 0.2);
                half fv = (v - 1.0) / max(fw, 1e-3);
                half flame = (1.0 - inBody) * saturate(1.0 - fv);
                flame *= flame * (0.6 + 0.8 * fl2) * step(thr, 1.05);
                half3 cf = lerp(_Glow.rgb, _Core.rgb, saturate(1.0 - fv * 2.5));

                half lum = inBody * ab + flame;
                half3 c = (inBody * cb * ab + cf * flame) * _Intensity;
                c += _Core.rgb * lum * 3.0 * _Flash;                   // 出手瞬间整条过曝
                c *= sweep * _Color.a * i.col.rgb;
                half occ = inBody * cut * sweep * smoothstep(0.0, 0.5, vb) * _Shade * _Color.a;
                return half4(c, occ);
            }
            ENDHLSL
        }
    }
}
