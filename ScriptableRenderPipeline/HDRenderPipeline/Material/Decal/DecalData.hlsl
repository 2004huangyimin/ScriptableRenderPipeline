﻿//-------------------------------------------------------------------------------------
// Fill SurfaceData/Builtin data function
//-------------------------------------------------------------------------------------
#include "ShaderLibrary/Packing.hlsl"
#include "ShaderLibrary/Sampling/SampleUVMapping.hlsl"

void GetSurfaceData(float2 texCoordDS, out DecalSurfaceData surfaceData)
{
	surfaceData.baseColor = float4(0,0,0,0);
	surfaceData.normalWS = float4(0,0,0,0);
	surfaceData.mask = float4(0,0,0,0);
	surfaceData.height = 0;
	float totalBlend = _DecalBlend;
#if _COLORMAP
	surfaceData.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, texCoordDS.xy);
	surfaceData.baseColor.w *= totalBlend;
#endif
	UVMapping texCoord;
	ZERO_INITIALIZE(UVMapping, texCoord);
	texCoord.uv = texCoordDS.xy;
#if _NORMALMAP
	surfaceData.normalWS.xyz = mul((float3x3)_DecalToWorldR, SAMPLE_UVMAPPING_NORMALMAP(_NormalMap, sampler_NormalMap, texCoord, 1)) * 0.5f + 0.5f;
	surfaceData.normalWS.w = totalBlend;
#endif
#if _MASKMAP
	surfaceData.mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, texCoordDS.xy); 
	surfaceData.normalWS.w *= surfaceData.mask.z;
	surfaceData.mask.z *= totalBlend;
#endif
#if _HEIGHTMAP
	surfaceData.height.x = (SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, texCoordDS.xy).x - _HeightCenter) * _HeightAmplitude; 
	surfaceData.height.y = _DecalBlend;	
#endif
}


