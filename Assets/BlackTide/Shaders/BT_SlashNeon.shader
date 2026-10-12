// 霓虹故障风格的刀光：对标 BlackTide/Slash，属性名保持一致（SlashFx.SetColors 不用改接口）。
// 高饱和的连续刀锋 + 窄外光晕 + 顺刀路消退。RGB 按实际覆盖预乘，透明区不发光。
// 赛博语言落在锐利弧面和灯管结构；不做屏幕分行撕裂、白芯过曝或随机熄灭。
Shader "BlackTide/SlashNeon"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Color ("Color", Color) = (0.3,0.95,0.9,1)
        _Core ("Core", Color) = (1,1,1,1)
        _EdgeCol ("Inner Color", Color) = (0.9,0.15,0.6,1)
        _Glow ("Flame Color", Color) = (1,0.5,0.2,1)
        _Intensity ("Intensity", Float) = 1.65
        _Head ("Head", Float) = 1.1
        _Tail ("Tail", Float) = -1
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _Flow ("Flow", Float) = 2
        _Seed ("Seed", Float) = 0
        _EdgeV ("Blade Edge V", Float) = 1
        _Flash ("Flash", Range(0,1)) = 0
        _Shade ("Shade", Range(0,1)) = 0.2

        _Glitch ("Glitch", Range(0,1)) = 0.7
        _BandN ("Bands", Float) = 9
        _Rate ("Step Rate (Hz)", Float) = 14
        _Chroma ("Chroma Split", Range(0,0.2)) = 0.05
        _Ghost ("Ghost Echo", Range(0,1)) = 0.5
        _Flick ("Flicker", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+14" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
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
                float4 _Noise_ST; float4 _Color, _Core, _EdgeCol, _Glow;
                float _Intensity, _Head, _Tail, _Dissolve, _Flow, _Seed, _EdgeV, _Flash, _Shade;
                float _Glitch, _BandN, _Rate, _Chroma, _Ghost, _Flick;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            V vert (A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = i.uv;
                o.col = i.col;
                return o;
            }

            // 刀光本体在某一个 u 偏移下的取样：返回颜色与透明度
            void blade(float2 uv, float u_off, float edgeV, float tq, float band_id, out float3 c, out float a)
            {
                float u = uv.x + u_off;
                float v = uv.y / max(edgeV, 0.01);

                float d = _Head - u;
                float sweep = smoothstep(0.0, 0.05, d) * smoothstep(_Tail, _Tail + 0.4, u);
                float vb = saturate(v);
                float nearHead = exp(-max(d, 0.0) * 5.0);

                // 灯管芯：只在刀锋附近一条窄带内高亮，外侧迅速归零（硬边，不做柔衰减）
                float tube = smoothstep(0.78, 0.90, vb) * (1.0 - smoothstep(0.995, 1.06, v));
                // 内侧能量：很暗的一层衬底，让管子有"座"
                float body = smoothstep(0.15, 0.8, vb) * 0.12;
                // 外辉光：贴在管外的一窄圈
                float halo = smoothstep(0.96, 1.02, v) * (1.0 - smoothstep(1.04, 1.22, v)) * 0.22;

                // 断口：噪声决定哪几段灭掉（霓虹灯管坏掉一节一节的样子），头部永远亮
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 3.0 + _Seed + _Time.y * 0.08, vb * 0.6)).r;
                float thr = _Dissolve * 1.15 + (1.0 - vb) * 0.35 + saturate(d) * 0.18 + (1.0 - u) * 0.25;
                float on = smoothstep(thr - 0.08, thr + 0.08, n * 0.9 + 0.12);
                // 沿刀长的整段熄灭（按条带随机），错乱感来源之一
                float seg = 1.0;

                c = _Color.rgb * body + _Core.rgb * tube * (0.65 + nearHead * 0.25) + _Glow.rgb * halo;
                c += _EdgeCol.rgb * saturate(vb * 1.2) * 0.18;
                a = (tube * 1.0 + halo * 0.8 + body * 0.5) * on * seg * sweep * saturate(1.0 - d * 0.2);
            }

            half4 frag (V i) : SV_Target
            {
                float edgeV = max(_EdgeV, 0.01);
                float tq = _Time.y;

                // 屏幕空间分行撕裂：每行一个水平 u 偏移
                float bandH = max(6.0, _ScreenParams.y / max(_BandN, 2.0));
                float band = floor(i.pos.y / bandH);
                float br = hash11(band + tq * 13.7);
                // Local geometry carries the style. Never tear an attack by screen row.
                float tear = 0.0;

                float3 c; float a;
                // 主芯（绿通道，即中心取样）
                blade(i.uv, tear, edgeV, tq, band, c, a);
                // RGB 色散：左右各偏一份，取红/蓝通道
                float3 cR; float aR; blade(i.uv, tear + _Chroma, edgeV, tq, band, cR, aR);
                float3 cB; float aB; blade(i.uv, tear - _Chroma, edgeV, tq, band, cB, aB);
                float3 rgb = c * a;
                float alpha = max(a, max(aR, aB) * 0.75);

                // 重影错版：偶发一份偏移放大的旧帧刀光，像信号延迟留下的残像
                float gr = 1.0;
                if (_Ghost > 0.0)
                {
                    float3 cg; float ag;
                    blade(i.uv, -0.045, edgeV, tq, band, cg, ag);
                    rgb += _Glow.rgb * ag * _Ghost * 0.12;
                    alpha = max(alpha, ag * 0.12 * _Ghost);
                }

                // 阶梯闪烁：整片按步进跳亮度，不做正弦（正弦太"呼吸"，赛博要的是不稳定）
                float fl = 1.0;
                fl = max(fl, 0.3);

                rgb *= _Intensity * fl;
                rgb += _Color.rgb * a * 0.18 * _Flash;
                rgb *= _Color.a * i.col.rgb * i.col.a;
                alpha *= i.col.a * _Color.a;

                // 霓虹不压暗背景，纯加色发光；_Shade 只留一个很小的自遮挡项防止叠成一片死白
                return half4(rgb, alpha * (1.0 - _Shade * 0.35));
            }
            ENDHLSL
        }
    }
}
