#if SHADERPASS != SHADERPASS_FORWARD_UNLIT
#error SHADERPASS_is_not_correctly_define
#endif

float4 Frag(PackedVaryings packedInput) : SV_Target
{
    FragInput input = UnpackVaryings(packedInput);

	SurfaceData surfaceData;
	BuiltinData builtinData;
	GetSurfaceAndBuiltinData(input, surfaceData, builtinData);
	
	// Not lit here (but emissive is allowed)

	BSDFData bsdfData = ConvertSurfaceDataToBSDFData(surfaceData);
		
	// TODO: we must not access bsdfData here, it break the genericity of the code!
    return float4(bsdfData.color + builtinData.emissiveColor * builtinData.emissiveIntensity, builtinData.opacity);
}

