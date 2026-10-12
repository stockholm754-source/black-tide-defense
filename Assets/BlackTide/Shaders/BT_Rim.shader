// 敌人轮廓光（2026-10-09 打磨）：作为第二个材质叠在模型上，加色菲涅耳边缘光。
// 深色敌人站在深色地面上几乎看不见，加一圈冷青色轮廓让它从地面里跳出来；受击时由 Damageable 调亮（_Flash）
Shader "BlackTide/Rim"
{
    Properties
    {
        _Color ("Rim Color", Color) = (0.35,0.95,1,1)
        _Power ("Power", Float) = 2.6
        _Intensity ("Intensity", Float) = 0.9
        _Flash ("Flash", Float) = 0
        _Tell ("Attack Tell", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            Name "Rim"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Power; half _Intensity; half _Flash; half _Tell;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; };
            V vert (A i) { V o; o.wp = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.wp); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag (V i) : SV_Target
            {
                half3 v = normalize(GetWorldSpaceViewDir(i.wp));
                half f = pow(1.0 - saturate(dot(normalize(i.n), v)), _Power);
                // 出手提示（_Tell 0→1）：轮廓从冷青变成红橙并变亮，满值时身体也泛一层红光
                half3 tc = half3(1.0, 0.28, 0.06);
                half3 rc = lerp(_Color.rgb, tc, saturate(_Tell));
                half3 c = rc * f * (_Intensity + _Flash * 3.0 + _Tell * 1.6) + _Color.rgb * _Flash * 0.25 + tc * saturate(_Tell - 0.7) * 0.4;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
