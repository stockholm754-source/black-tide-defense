// 黑潮海面：两层滚动噪声，暗紫底色 + 亮紫浪尖，带一点自发光让泛光吃到
Shader "BlackTide/Tide"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Deep ("Deep", Color) = (0.03,0.01,0.07,1)
        _Crest ("Crest", Color) = (0.55,0.25,1.0,1)
        _Glow ("Glow", Float) = 2.5
        _Scale ("Scale", Float) = 0.035
        _Speed ("Speed", Float) = 0.02
        _Alpha ("Alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep; half4 _Crest; half _Glow; float _Scale; float _Speed; half _Alpha;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };
            V vert (A i) { V o; o.wp = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.wp); return o; }
            half4 frag (V i) : SV_Target
            {
                float2 uv = i.wp.xz * _Scale;
                float t = _Time.y * _Speed;
                half n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv + float2(t, t * 0.6)).r;
                half n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 2.3 - float2(t * 1.3, -t * 0.4)).r;
                half n = n1 * 0.6 + n2 * 0.4;
                half crest = smoothstep(0.58, 0.8, n);
                half3 col = lerp(_Deep.rgb, _Crest.rgb * 0.35, smoothstep(0.3, 0.75, n)) + _Crest.rgb * crest * _Glow;
                return half4(col, _Alpha);
            }
            ENDHLSL
        }
    }
}
