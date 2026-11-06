#ifndef GERSTNER_WAVES_INCLUDED
#define GERSTNER_WAVES_INCLUDED
 

 
float3 _GerstnerWave(float2 samplePos,
                     float steepness,
                     float wavelength,
                     float speed,
                     float direction,
                     float globalOffset,
                     inout float3 tangent,
                     inout float3 binormal)
{
  
    direction = direction * 2.0 - 1.0;
    float2 d = normalize(float2(cos(3.14159 * direction),
                                 sin(3.14159 * direction)));
 

    float k = 2.0 * 3.14159 / max(wavelength, 1e-4);
    float a = steepness / k;
 
  
    float f = k * (dot(d, samplePos) - speed * _Time.y + globalOffset);
 
    float s = sin(f);
    float c = cos(f);
 
 
    tangent += float3(
        -d.x * d.x * (steepness * s),
         d.x * (steepness * c),
        -d.x * d.y * (steepness * s)
    );
    binormal += float3(
        -d.x * d.y * (steepness * s),
         d.y * (steepness * c),
        -d.y * d.y * (steepness * s)
    );
 

    return float3(d.x * a * c,
                  a * s,
                  d.y * a * c);
}
 

void GerstnerWaves_float(float3 PositionOS,
                         float2 MeshUV,
                         float UseUV,
                         float Steepness,
                         float Wavelength,
                         float Speed,
                         float4 Directions,
                         float TileSize,
                         float RiverLength,
                         float RiverWidth,
                         out float3 PositionOS_Out,
                         out float3 NormalOS_Out)
{
    float2 samplePos;
    float globalOffset = 0.0;
 
    if (UseUV > 0.5)
    {
       
        samplePos = float2(MeshUV.x * RiverLength,
                           MeshUV.y * RiverWidth);
    }
    else
    {
        
        float3 posWS = mul(unity_ObjectToWorld, float4(PositionOS, 1.0)).xyz;
 
        float gx = floor(posWS.x / TileSize) * TileSize;
        float gz = floor(posWS.z / TileSize) * TileSize;
        globalOffset = gx + gz;
 
        samplePos = float2(fmod(posWS.x, TileSize),
                               fmod(posWS.z, TileSize));
    }
 

    float3 disp = float3(0, 0, 0);
    float3 tangent = float3(1, 0, 0);
    float3 binormal = float3(0, 0, 1);
 

    disp += _GerstnerWave(samplePos, Steepness, Wavelength, Speed, Directions.x, globalOffset, tangent, binormal);
    disp += _GerstnerWave(samplePos, Steepness, Wavelength, Speed, Directions.y, globalOffset, tangent, binormal);
    disp += _GerstnerWave(samplePos, Steepness, Wavelength, Speed, Directions.z, globalOffset, tangent, binormal);
    disp += _GerstnerWave(samplePos, Steepness, Wavelength, Speed, Directions.w, globalOffset, tangent, binormal);
 
  
    float3 normalWS = normalize(cross(binormal, tangent));
 
  
    float3 dispOS = mul((float3x3) unity_WorldToObject, disp);
    PositionOS_Out = PositionOS + dispOS;
 

    NormalOS_Out = normalize(mul((float3x3) unity_WorldToObject, normalWS));
}
 
#endif 