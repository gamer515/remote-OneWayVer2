Shader "OneWay/B2 Golden Slash"
{
 Properties
 {
  [HDR] _CoreColor ("Bright Core", Color) = (10,8,3,1)
  [HDR] _EdgeColor ("Golden Edge", Color) = (6,2.5,0.15,1)
 }
 SubShader
 {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float4 _CoreColor;
    float4 _EdgeColor;
   CBUFFER_END
   struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
   struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
   Varyings Vert(Attributes v)
   {
    Varyings o;
    o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);
    return o;
   }
   half4 Frag(Varyings i) : SV_Target
   {
    float facing=abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)));
    float core=smoothstep(0.15,0.85,facing);
    return half4(lerp(_EdgeColor.rgb,_CoreColor.rgb,core),1);
   }
   ENDHLSL
  }
 }
}
