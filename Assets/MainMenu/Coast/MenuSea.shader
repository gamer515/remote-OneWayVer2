Shader "MainMenu/Coastal Sea"
{
    Properties
    {
        _DeepColor("Deep water", Color) = (.015,.3,.85,1)
        _ShallowColor("Shallow water", Color) = (.015,.8,.66,1)
        _WaveHeight("Wave height", Range(0,1)) = .16
        _WaveSpeed("Wave speed", Range(0,3)) = .8
        _WaveLength("Wave length", Range(2,16)) = 6
        _FoamStrength("Foam strength", Range(0,1)) = .95
        _ShoreReach("Shore wash distance", Range(0,6)) = 2.4
        _ShoreCurve("Shore curve size", Range(0,3)) = 1.1
        _SparkleStrength("Sun sparkle", Range(0,2)) = .8
        _FoamWidth("Shore foam width", Range(.1,3)) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _DeepColor, _ShallowColor;
            float _WaveHeight, _WaveSpeed, _WaveLength, _FoamStrength, _ShoreReach, _ShoreCurve;
            float _SparkleStrength, _FoamWidth;
            CBUFFER_END
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            float front(float z,float t)
            {
                return -_ShoreReach*(.5+.5*sin(t*.85)) + _ShoreCurve*(.65*sin(z*.65+t*.35)+.35*sin(z*1.15-t*.25));
            }
            V vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                float t=_Time.y*_WaveSpeed;
                float phase=o.world.x*6.283185/max(2,_WaveLength)+t*1.8+sin(o.world.z*.32)*.4;
                float shore=smoothstep(0,4,o.world.x);
                o.world.y=lerp(.17,o.world.y,shore);
                o.world.y+=_WaveHeight*shore*(sin(phase)*.65+sin(o.world.x*1.7+o.world.z*.8+t*1.2)*.2);
                o.positionCS=TransformWorldToHClip(o.world); return o;
            }
            half4 frag(V i):SV_Target
            {
                float t=_Time.y*_WaveSpeed;
                float d=i.world.x-front(i.world.z,t);
                clip(d);
                float2 p=i.world.xz;
                float n=noise(p*.65+float2(t*.3,-t*.18));
                float fine=noise(p*3+float2(t*.65,t*.3));
                float depth=smoothstep(1,26,max(0,i.world.x));
                float3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,depth);
                color+= (n-.5)*.09+(fine-.5)*.045;
                // Shoreward-moving narrow crest lines fade before reaching open water.
                float phase=i.world.x*6.283185/max(2,_WaveLength)+t*1.8+sin(p.y*.32)*.5+(n-.5)*.6;
                float crest=pow(saturate(.5+.5*sin(phase)),35);
                float breaker=crest*(1-smoothstep(7,19,d))*smoothstep(.6,2,d);
                // Broken foam cells behind the leading edge, instead of a solid white strip.
                float cell=noise(p*float2(3,2)+float2(t*.3,t*.1));
                float ridge=1-smoothstep(.025,.095,abs(cell-.5));
                float lace=ridge*(1-smoothstep(.12,max(.2,_FoamWidth),d));
                float lip=1-smoothstep(.04,.18,d);
                float foam=saturate(max(breaker,max(lip,lace*.9))*_FoamStrength);
                color=lerp(color,float3(.94,1,.91),foam);
                // Two crossed ripple fields produce sparse moving sun glints.
                float r1=sin(p.x*12+p.y*4+t*2+6*n);
                float r2=sin(p.y*17-p.x*3-t*1.6+4*fine);
                float glint=pow(saturate(r1*r2),14)*smoothstep(.45,.75,noise(p*1.7+float2(t*.4,-t*.2)));
                float aa=1-smoothstep(.6,2,max(length(ddx(p*12)),length(ddy(p*12))));
                color+=glint*aa*_SparkleStrength*smoothstep(1,6,d)*(1-foam)*float3(.65,.85,1);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}

