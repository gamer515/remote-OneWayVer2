Shader "Journey/BakedSurfaceURP"
{
    Properties
    {
        [MainTexture] _BakedTexture("Baked diffuse lighting (UV2)", 2D) = "white" {}
        _Gain("Baked lighting gain", Range(0,3)) = 1
        _ShadowBlend("Realtime shadow contribution", Range(0,1)) = 0.2
        _AOBlend("Realtime contact occlusion", Range(0,1)) = 0.3
        _SpecularAmount("Subtle realtime reflections", Range(0,1)) = 0.05
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _MetalTint("Reflection tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BakedTexture); SAMPLER(sampler_BakedTexture);
        CBUFFER_START(UnityPerMaterial)
            float4 _BakedTexture_ST;
            half4 _MetalTint;
            half _Gain, _ShadowBlend, _AOBlend, _SpecularAmount, _Smoothness;
            float _Cull;
        CBUFFER_END
        struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
        Varyings Vertex(Attributes input)
        {
            Varyings output=(Varyings)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position=GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS=position.positionCS;output.positionWS=position.positionWS;
            output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.uv=input.uv;return output;
        }
        half4 Fragment(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 color=SAMPLE_TEXTURE2D(_BakedTexture,sampler_BakedTexture,input.uv).rgb*_Gain;
            half3 normal=normalize(input.normalWS);half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
            Light main=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            color*=lerp(1.0h,main.shadowAttenuation,_ShadowBlend);
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                color*=lerp(1.0h,ao.indirectAmbientOcclusion,_AOBlend);
            #endif
            half3 reflected=GlossyEnvironmentReflection(reflect(-view,normal),1-_Smoothness,1);
            color+=reflected*_MetalTint.rgb*_SpecularAmount;
            half spec=pow(saturate(dot(normal,normalize(main.direction+view))),lerp(8,128,_Smoothness));
            color+=main.color*spec*_MetalTint.rgb*_SpecularAmount*main.shadowAttenuation*.3h;
            return half4(color,1);
        }
        half4 DepthFragment(Varyings input):SV_Target{return 0;}
        half4 NormalsFragment(Varyings input):SV_Target
        {
            float3 normal=normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct=PackNormalOctQuadEncode(normal);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
            #else
                return half4(normal,0);
            #endif
        }
        float3 _LightDirection;float3 _LightPosition;
        Varyings ShadowVertex(Attributes input)
        {
            Varyings output=Vertex(input);float3 direction=_LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction=normalize(_LightPosition-output.positionWS);
            #endif
            output.positionCS=TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z=min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
            #else
                output.positionCS.z=max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
            #endif
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "Forward" Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormals"} ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly" Tags {"LightMode"="DepthNormalsOnly"} ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
