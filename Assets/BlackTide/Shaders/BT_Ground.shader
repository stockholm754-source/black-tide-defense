// 开放地图地面（2026-10-09 打磨）：按位置混合多种地面，打破「一张石板铺满整张图」
//   野外：泥土和苔藓用大尺度噪声交错（两个尺度采样，消除重复感）
//   _Mask（MapBuilder 运行时生成，覆盖整块地面）：R 节点广场石板、G 道路卵石、B 静态腐化（巢穴、裂隙、内圈）、A 植被许可
//   海岸：离地图边缘越近越偏沙地（顶点在边界外沉入海里）
//   黑潮：_TideCenter / _TideRadius 每帧更新，覆盖范围内变成焦油状黑潮地，边缘一圈发光裂纹——侵蚀扩散在地面上看得见
// 用户的地面贴图（Design/art_batch5_ground.md）到之前，各层用现有贴图乘色调代替（*_Tint）
Shader "BlackTide/Ground"
{
    Properties
    {
        _Mask ("Mask (R plaza, G road, B corrupt, A veg)", 2D) = "black" {}
        _Earth ("Earth", 2D) = "gray" {}       _EarthTint ("Earth Tint", Color) = (1,1,1,1)
        _Moss ("Moss", 2D) = "gray" {}         _MossTint ("Moss Tint", Color) = (1,1,1,1)
        _Sand ("Sand", 2D) = "gray" {}         _SandTint ("Sand Tint", Color) = (1,1,1,1)
        _Cobble ("Cobble", 2D) = "gray" {}     _CobbleTint ("Cobble Tint", Color) = (1,1,1,1)
        _Stone ("Stone", 2D) = "gray" {}       _StoneTint ("Stone Tint", Color) = (1,1,1,1)
        _Tide ("Tide ground", 2D) = "black" {} _TideTint ("Tide Tint", Color) = (1,1,1,1)
        _TideEm ("Tide emission mask", 2D) = "black" {}
        _Normal ("Detail normal", 2D) = "bump" {}
        _Noise ("Noise", 2D) = "gray" {}
        _MapExt ("Mask half extent", Float) = 150
        _PlayHalf ("Play area half", Float) = 110
        _EarthScale ("Earth scale (1/m)", Float) = 0.11
        _StoneScale ("Stone scale", Float) = 0.14
        _TideCenter ("Tide center xz", Vector) = (88,88,0,0)
        _TideRadius ("Tide radius", Float) = 0
        _Glow ("Tide glow color", Color) = (0.25,1,0.92,1)
        _GlowStrength ("Glow strength", Float) = 1.4
        _ProceduralTide ("Procedural tide (no texture yet)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_Earth); SAMPLER(sampler_Earth);
            TEXTURE2D(_Moss); TEXTURE2D(_Sand); TEXTURE2D(_Cobble); TEXTURE2D(_Stone); TEXTURE2D(_Tide); TEXTURE2D(_TideEm);
            TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                half4 _EarthTint, _MossTint, _SandTint, _CobbleTint, _StoneTint, _TideTint, _Glow;
                float _MapExt, _PlayHalf, _EarthScale, _StoneScale, _TideRadius, _GlowStrength, _ProceduralTide;
                float4 _TideCenter;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; float4 t : TEXCOORD2; float fog : TEXCOORD3; };

            V vert (A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.n);
                o.t = float4(TransformObjectToWorldDir(i.t.xyz), i.t.w);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half N(float2 uv) { return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r; }
            // 同一张贴图两个尺度、错开旋转再按噪声混合：大面积看不出重复
            half3 Tile2(TEXTURE2D_PARAM(tex, smp), float2 p, float s, half mixN)
            {
                half3 a = SAMPLE_TEXTURE2D(tex, smp, p * s).rgb;
                float2 q = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8);
                half3 b = SAMPLE_TEXTURE2D(tex, smp, q * s * 0.41 + 0.37).rgb;
                return lerp(a, b, mixN);
            }

            half4 frag (V i) : SV_Target
            {
                float2 p = i.wp.xz;
                half macro = N(p * 0.0045);                 // 约 220 米一个周期
                half macro2 = N(p * 0.011 + 0.5);
                half micro = N(p * 0.06);
                half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, p / (2.0 * _MapExt) + 0.5);
                // 区域主题（2026-10-09）：按到黑潮中心的距离分三圈，边界按噪声错开
                //   外圈 > 165 米：海岸渔村，苔藓多、路是踩实的土路；中圈：古城遗迹，卵石路和石板广场；内圈 < 120 米：腐地，褪色发灰、苔藓枯死
                float zd = distance(p, _TideCenter.xy);
                half outerW = saturate((zd - 165.0 + (macro - 0.5) * 30.0) / 25.0);
                half innerW = saturate((120.0 - zd + (macro - 0.5) * 30.0) / 25.0);

                // 野外：泥土 ↔ 苔藓
                half3 earth = Tile2(TEXTURE2D_ARGS(_Earth, sampler_Earth), p, _EarthScale, macro) * _EarthTint.rgb;
                half3 moss = Tile2(TEXTURE2D_ARGS(_Moss, sampler_Earth), p + 13.7, _EarthScale * 0.9, macro2) * _MossTint.rgb;
                // 苔藓：90 米周期的大片 + 30 米周期的小块，原来只有大片，一个镜头里常常全是泥土
                half mossN = max(macro2, N(p * 0.033 + 0.21) * 0.92);
                half mossW = saturate((mossN - 0.45 - outerW * 0.08 + innerW * 0.25) * 3.2 + (micro - 0.5) * 1.2) * m.a;
                half3 col = lerp(earth, moss, mossW);
                // 宏观明暗：大片的深浅变化，避免平涂
                col *= lerp(0.82, 1.12, macro);

                // 海岸沙地
                float edge = max(abs(p.x), abs(p.y));
                half sandW = saturate((edge - (_PlayHalf - 24.0) + (micro - 0.5) * 10.0) / 10.0);
                half3 sand = Tile2(TEXTURE2D_ARGS(_Sand, sampler_Earth), p, _EarthScale * 1.3, micro) * _SandTint.rgb;
                col = lerp(col, sand, sandW);

                // 道路（卵石）：边缘按噪声啃掉，看起来是走出来的路
                half roadW = saturate(m.g * 1.7 - micro * 0.75);
                half3 cobble = SAMPLE_TEXTURE2D(_Cobble, sampler_Earth, p * _StoneScale * 1.6).rgb * _CobbleTint.rgb;
                half3 dirtRoad = lerp(earth, sand, 0.35) * half3(0.86, 0.8, 0.72);   // 外圈：踩实的土路
                col = lerp(col, lerp(cobble, dirtRoad, outerW * 0.9), roadW);

                // 节点广场（石板）
                half plazaW = saturate(m.r * 1.6 - micro * 0.6);
                half3 stone = SAMPLE_TEXTURE2D(_Stone, sampler_Earth, p * _StoneScale).rgb * _StoneTint.rgb;
                col = lerp(col, lerp(stone, lerp(stone, sand, 0.45), outerW), plazaW);
                // 内圈腐地：褪色、偏冷、压暗
                half lum = dot(col, half3(0.3, 0.55, 0.15));
                col = lerp(col, lum * half3(0.78, 0.86, 0.9) * 0.85, innerW * 0.7);

                // 黑潮：静态腐化（巢穴、裂隙、内圈）+ 随侵蚀扩散的覆盖范围
                // 覆盖范围内是「逐渐被污染」：整体压暗，焦油斑块按噪声分布；越深入越密
                float d = distance(p, _TideCenter.xy);
                float depth = _TideRadius - d;
                half inside = saturate(depth / 10.0 + (micro - 0.5) * 0.9);
                half blot = saturate((N(p * 0.021 + 0.3) - 0.5 + saturate(depth / 120.0) * 0.35) * 3.0);
                half dyn = inside * lerp(0.25, 0.9, blot);
                half tideW = saturate(max(dyn, m.b * 0.85));
                col *= lerp(1.0, 0.72, inside);
                half3 tideCol;
                half veins;
                if (_ProceduralTide > 0.5)
                {
                    half n2 = N(p * 0.09 + _Time.y * 0.004);
                    tideCol = lerp(half3(0.012, 0.02, 0.025), half3(0.035, 0.06, 0.07), n2) * _TideTint.rgb;
                    veins = smoothstep(0.016, 0.0, abs(N(p * 0.03) - 0.5)) * smoothstep(0.45, 0.75, n2 + 0.15);
                }
                else
                {
                    tideCol = Tile2(TEXTURE2D_ARGS(_Tide, sampler_Earth), p, _EarthScale, macro2) * _TideTint.rgb;
                    veins = SAMPLE_TEXTURE2D(_TideEm, sampler_Earth, p * _EarthScale).r;
                }
                col = lerp(col, tideCol, tideW);

                // 覆盖范围的前沿：一圈脉动的亮边
                half front = smoothstep(1.1, 0.0, abs(d - _TideRadius + (micro - 0.5) * 6.0)) * step(1.0, _TideRadius);   // 细的一条线（原来 8 米宽，像霓虹河）
                half pulse = 0.65 + 0.35 * sin(_Time.y * 2.0 - d * 0.15);
                // 发光裂纹只在扩散前沿往里 25 米内和静态腐化区（巢穴、裂隙、黑潮之心）
                half frontBand = saturate(1.0 - depth / 25.0) * step(0.0, depth);
                half3 emission = _Glow.rgb * _GlowStrength * (veins * tideW * pulse * saturate(frontBand + m.b) + front * 0.3 * pulse);

                // 法线：细节法线随泥土尺度；黑潮区平滑（焦油感）
                half3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, p * _EarthScale));
                nT.xy *= lerp(0.8, 0.15, tideW);
                float3 bit = cross(i.n, i.t.xyz) * i.t.w;
                float3 nW = normalize(nT.x * i.t.xyz + nT.y * bit + nT.z * i.n);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.wp;
                inputData.normalWS = nW;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.wp);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.wp);
                inputData.fogCoord = i.fog;
                inputData.bakedGI = SampleSH(nW);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = col;
                s.metallic = 0;
                s.smoothness = lerp(lerp(0.12, 0.22, mossW), 0.78, tideW);   // 黑潮焦油反光
                s.normalTS = nT;
                s.occlusion = 1;
                s.emission = emission;
                s.alpha = 1;
                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; };
            float4 v (A i) : SV_POSITION { return TransformObjectToHClip(i.pos.xyz); }
            half f () : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            V v (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 f (V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
