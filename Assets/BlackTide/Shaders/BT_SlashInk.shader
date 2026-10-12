// 美漫风斩击弧面 / 刀光条带（u 沿挥砍方向 0 尾 → 1 头，v 从内缘往外；刀锋在 v = _EdgeV，再往外是一圈外焰）
// 全部硬边：平涂主色 + 刀锋高光带 + 内侧半调网点压成暗色 + 黑色墨线勾边 + 往内错开一圈的撞色（印刷错版）+ 刀锋外的锯齿外焰；条纹按 _Fps 抽帧跳动
Shader "BlackTide/SlashInk"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Color ("Main", Color) = (1,0.4,0,1)
        _Hi ("Highlight", Color) = (1,0.97,0.75,1)
        _Lo ("Shade", Color) = (0.8,0,0.4,1)
        _Pop ("Offset Color", Color) = (0,0.85,1,1)
        _Pop2 ("Flame Color", Color) = (1,0.9,0.05,1)
        _Ink ("Ink", Color) = (0.04,0.02,0.07,1)
        _Head ("Head", Float) = 1.1
        _Tail ("Tail", Float) = -1
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _Seed ("Seed", Float) = 0
        _EdgeV ("Blade Edge V", Float) = 1
        _Dots ("Halftone Density", Float) = 46
        _Fps ("Step FPS", Float) = 12
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float4 _Noise_ST; half4 _Color, _Hi, _Lo, _Pop, _Pop2, _Ink; half _Head, _Tail, _Dissolve, _Seed, _EdgeV, _Dots, _Fps;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 frag (V i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y / _EdgeV;                 // v = 1 是刀锋，>1 是外焰
                float d = _Head - u;                                   // 离头部多远（>0 已扫过）

                // 条纹只取模糊过的长丝（边缘干净，不出噪点），一拍一拍地往尾部跳
                float tq = floor(_Time.y * _Fps) / _Fps;
                half streak = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, float2(u * 0.6 + tq * 1.3 + _Seed, v * 0.7 + _Seed * 0.37), 2.5).r;

                // 形状：v 超过门槛的部分才是刀光。内缘被条纹切成尖齿，越靠尾部、越旧、溶解越多，门槛越高；头部收成尖
                half age = 1.0 - i.col.a;
                half tailCut = 1.0 - smoothstep(_Tail, _Tail + 0.45, u);
                half tip = smoothstep(0.14, 0.0, d);
                half thr = 0.14 + (1.0 - streak) * 0.5 + saturate(d) * 0.3 + age * 0.8 + _Dissolve * 1.1 + tailCut + tip * 0.85;
                half field = v - thr;
                half swept = step(0.0, d);
                half body = step(v, 1.0) * step(0.0, field) * swept;
                half pop = step(v, 1.0) * step(0.0, field + 0.05) * swept * (1.0 - body);   // 往内错开一圈的撞色边

                // 外焰：刀锋外面一排朝外的尖齿，本体越薄齿越短
                half fl = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, float2(u * 2.6 + tq * 2.1 + _Seed * 1.3, 0.37), 1.0).g;
                half flameW = (1.0 / _EdgeV - 1.0) * (0.2 + 0.8 * fl) * saturate((1.0 - thr) * 2.5);
                half flame = step(1.0, v) * step(v - 1.0, flameW) * swept * step(thr, 0.95);
                half flameInk = step(flameW - 0.035, v - 1.0);

                // 面上几道顺着挥砍方向的墨色速度线
                half lineN = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, float2(u * 0.35 + tq * 0.7 + _Seed, v * 5.0), 1.0).g;
                half speed = step(0.74, lineN) * step(0.14, field) * step(v, 0.8);

                half ink = saturate(step(field, 0.05) + step(0.95, v) + speed);
                half hi = step(0.84, v) * step(0.1, field);

                // 半调网点（屏幕空间、斜 45°）：靠内缘点大到连成片，往刀锋越来越小
                float2 sp = i.pos.xy / _ScreenParams.y * _Dots;
                sp = float2(sp.x + sp.y, sp.x - sp.y) * 0.7071;
                half dots = step(length(frac(sp) - 0.5), lerp(0.68, 0.0, saturate((field - 0.05) / 0.38)));

                half3 c = lerp(_Color.rgb, _Lo.rgb, dots);
                c = lerp(c, _Hi.rgb, hi);
                c = lerp(c, _Ink.rgb, ink);
                c = lerp(_Pop.rgb, c, body);
                c = lerp(c, lerp(_Pop2.rgb, _Ink.rgb, flameInk), flame);
                return half4(c * i.col.rgb, saturate(body + pop + flame));
            }
            ENDHLSL
        }
    }
}
