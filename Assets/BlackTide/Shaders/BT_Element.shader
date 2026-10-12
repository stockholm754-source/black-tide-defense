Shader "BlackTide/Element"
{
    Properties
    {
        _Color ("Skill Color", Color) = (1,.4,.06,1)
        _Kind ("Shape", Float) = 0
        _Seed ("Seed", Float) = 0
        _Fade ("Envelope", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+13" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Kind, _Seed, _Fade;
            CBUFFER_END
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float4 c:COLOR; };
            V vert(A i)
            {
                V o;
                if(_Kind<.5) i.p.xz+=float2(sin(_Time.y*2.6+i.p.y*4+_Seed),cos(_Time.y*2.2+i.p.y*3+_Seed))*.12*i.uv.y*i.uv.y;
                o.p=TransformObjectToHClip(i.p.xyz); o.uv=i.uv; o.c=i.c; return o;
            }
            half4 frag(V i):SV_Target
            {
                float2 uv=i.uv;
                float3 col=_Color.rgb;
                float a=1;
                if(_Kind<.5)
                {
                    float flow=sin(uv.y*19- _Time.y*8+_Seed)+sin(uv.y*31-uv.x*9-_Time.y*5+_Seed)*.4;
                    float edge=abs(uv.x-.5)*2;
                    a=(1-smoothstep(.35,.98,edge+flow*.15))*pow(saturate(1-uv.y),.45);
                    // Amber base, orange tongues: no white core and no channel splitting.
                    col=lerp(float3(1,.65,.06),float3(1,.19,.015),saturate(uv.y+edge*.35))*1.4;
                }
                else if(_Kind<1.5)
                {
                    float2 q=abs(uv*2-1); float d=max(q.x,q.y+q.x*.5);
                    float border=smoothstep(.84,.98,d);
                    float circuit=step(.96,frac(uv.y*8))*step(.4,frac(uv.x*6));
                    a=.025+border*.7+circuit*.10;
                    col*=1.3;
                }
                else if(_Kind<2.5)
                {
                    a=1-smoothstep(.1,.5,abs(uv.y-.5));
                    col*=1.2;
                }
                else if(_Kind<4.5)
                {
                    float flow=.4+.6*pow(saturate(sin(uv.x*16-_Time.y*8)),2);
                    a=sin(uv.y*PI)*flow; col*=1.4;
                }
                else if(_Kind<5.5)
                { col=lerp(col*.5,col*1.1,uv.y); a=.7; }
                else
                { col*=1.5; a=pow(saturate(1-abs(uv.y-.5)*2),.65); }
                return half4(col,a*_Fade*_Color.a*i.c.a);
            }
            ENDHLSL
        }
    }
}
