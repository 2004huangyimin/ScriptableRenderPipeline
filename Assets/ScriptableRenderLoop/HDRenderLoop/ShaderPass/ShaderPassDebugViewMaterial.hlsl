#if SHADERPASS != SHADERPASS_DEBUG_VIEW_MATERIAL
#error SHADERPASS_is_not_correctly_define
#endif

#include "Color.hlsl"
int _DebugViewMaterial;
			
float4 Frag(PackedVaryings packedInput) : SV_Target
{
    FragInput input = UnpackVaryings(packedInput);

	SurfaceData surfaceData;
	BuiltinData builtinData;
	GetSurfaceAndBuiltinData(input, surfaceData, builtinData);

	BSDFData bsdfData = ConvertSurfaceDataToBSDFData(surfaceData);

	float3 result = float3(1.0, 0.0, 1.0);
	bool needLinearToSRGB = false;

	GetVaryingsDataDebug(_DebugViewMaterial, input, result, needLinearToSRGB);
	GetBuiltinDataDebug(_DebugViewMaterial, builtinData, result, needLinearToSRGB);
	GetSurfaceDataDebug(_DebugViewMaterial, surfaceData, result, needLinearToSRGB);
	GetBSDFDataDebug(_DebugViewMaterial, bsdfData, result, needLinearToSRGB); // TODO: This required to initialize all field from BSDFData...

	// TEMP!
	// For now, the final blit in the backbuffer performs an sRGB write
	// So in the meantime we apply the inverse transform to linear data to compensate.
	if (!needLinearToSRGB)
		result = SRGBToLinear(max(0, result));

	return float4(result, 0.0);
}

