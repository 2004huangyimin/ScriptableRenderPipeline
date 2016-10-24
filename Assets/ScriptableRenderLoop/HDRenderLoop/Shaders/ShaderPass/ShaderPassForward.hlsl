#if SHADERPASS != SHADERPASS_FORWARD
#error SHADERPASS_is_not_correctly_define
#endif

float4 Frag(PackedVaryings packedInput) : SV_Target
{
    FragInput input = UnpackVaryings(packedInput);
	float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
	float3 positionWS = input.positionWS;

	SurfaceData surfaceData;
	BuiltinData builtinData;
	GetSurfaceAndBuiltinData(input, surfaceData, builtinData);

	BSDFData bsdfData = ConvertSurfaceDataToBSDFData(surfaceData);
	Coordinate coord = GetCoordinate(input.positionHS.xy, _ScreenSize.zw);
	PreLightData preLightData = GetPreLightData(V, positionWS, coord, bsdfData);

	float4 diffuseLighting;
	float4 specularLighting;
	ForwardLighting(V, positionWS, preLightData, bsdfData, diffuseLighting, specularLighting);

	diffuseLighting.rgb += GetBakedDiffuseLigthing(preLightData, surfaceData, builtinData, bsdfData);

	return float4(diffuseLighting.rgb + specularLighting.rgb, builtinData.opacity);
}

