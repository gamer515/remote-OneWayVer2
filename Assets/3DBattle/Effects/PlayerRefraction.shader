Shader "OneWay/Player Refractive Slash"
{
    Properties
    {
        _Distortion("Refraction Strength", Range(0, .05)) = .012
        _Visibility("Visibility", Range(0, 1)) = 1
        _RippleSpeed("Ripple Speed", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Distortion, _Visibility, _RippleSpeed;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float edge=smoothstep(0,.16,uv.y)*smoothstep(0,.16,1-uv.y);
                float ends=smoothstep(0,.12,uv.x)*smoothstep(0,.12,1-uv.x);
                float mask=edge*ends*_Visibility;
                float t=_Time.y*_RippleSpeed;
                float wave=sin(uv.x*65+uv.y*18-t*5)+.4*sin(uv.x*133-uv.y*31+t*7);
                float2 screenUV=GetNormalizedScreenSpaceUV(i.positionCS);
                float2 offset=float2(wave,cos(uv.x*53+uv.y*23-t*4)) * _Distortion * mask;
                half3 scene=SampleSceneColor(clamp(screenUV+offset,.002,.998));
                scene+=half3(.012,.018,.02)*mask;
                return half4(scene,mask*.85);
            }
            ENDHLSL
        }
    }
}
