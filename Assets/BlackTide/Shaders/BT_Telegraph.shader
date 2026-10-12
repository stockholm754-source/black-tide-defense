// 地面攻击预警：uv.y 为半径/长度方向（0→1），_Progress 从里向外填充，外沿与两侧描边
Shader "BlackTide/Telegraph"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.3,0.15,0.9)
        _Progress ("Progress", Range(0,1)) = 0
        _Flash ("Flash", Float) = 0
        _Fade ("Fade", Float) = 1
        _Sides ("Side Edges", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Progress; half _Flash; half _Fade; half _Sides;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag (V i) : SV_Target
            {
                half r = i.uv.y; half x = i.uv.x;
                half rim = smoothstep(0.95, 0.985, r);
                half side = _Sides * saturate((1 - smoothstep(0.0, 0.025, x)) + smoothstep(0.975, 1.0, x));
                half filled = step(r, _Progress);
                half front = smoothstep(_Progress - 0.07, _Progress, r) * filled;
                half pulse = 0.5 + 0.5 * sin(_Time.y * 16.0);
                half a = 0.08 + 0.22 * filled + 0.7 * front + 0.85 * max(rim, side) * (0.7 + 0.3 * pulse);
                a += _Flash * 0.6;
                half3 col = lerp(_Color.rgb, half3(1.0, 0.75, 0.6), saturate(_Flash * 2.0));
                col *= 1.0 + front * 1.2;
                return half4(col, saturate(a * _Color.a) * saturate(_Fade));
            }
            ENDHLSL
        }
    }
}
