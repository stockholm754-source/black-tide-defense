Shader "BlackTide/MaskAlpha"
{
    Properties
    {
        _MainTex ("Texture",2D)="white" {}
        _Color ("Color",Color)=(1,1,1,1)
        _Intensity ("Intensity",Float)=1
        _SrcBlend ("Source",Float)=5
        _DstBlend ("Destination",Float)=10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            float4 _MainTex_ST,_Color; float _Intensity,_SrcBlend,_DstBlend;
            CBUFFER_END
            struct A {float4 p:POSITION; float2 uv:TEXCOORD0; float4 c:COLOR;};
            struct V {float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR;};
            V vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.uv=TRANSFORM_TEX(i.uv,_MainTex); o.c=i.c; return o; }
            half4 frag(V i):SV_Target
            {
                float4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                float mask=max(t.r,max(t.g,t.b));
                return half4(t.rgb*_Color.rgb*i.c.rgb*_Intensity,mask*t.a*_Color.a*i.c.a);
            }
            ENDHLSL
        }
    }
}
