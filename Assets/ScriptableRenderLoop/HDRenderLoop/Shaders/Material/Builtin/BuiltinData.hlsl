#ifndef UNITY_BUILTIN_DATA_INCLUDED
#define UNITY_BUILTIN_DATA_INCLUDED

//-----------------------------------------------------------------------------
// BuiltinData
// This structure include common data that should be present in all material
// and are independent from the BSDF parametrization.
// Note: These parameters can be store in GBuffer if the writer wants
//-----------------------------------------------------------------------------

#include "BuiltinData.cs.hlsl"

void GetBuiltinDataDebug(uint paramId, BuiltinData builtinData, inout float3 result, inout bool needLinearToSRGB)
{
    switch (paramId)
    {
    case DEBUGVIEW_BUILTIN_BUILTINDATA_OPACITY:
        result = builtinData.opacity.xxx;
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_BAKE_DIFFUSE_LIGHTING:
        // TODO: require a remap
        result = builtinData.bakeDiffuseLighting;
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_EMISSIVE_COLOR:
        result = builtinData.emissiveColor; needLinearToSRGB = true;
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_EMISSIVE_INTENSITY:
        result = builtinData.emissiveIntensity.xxx;
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_VELOCITY:
        result = float3(builtinData.velocity, 0.0);
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_DISTORTION:
        result = float3(builtinData.distortion, 0.0);
        break;
    case DEBUGVIEW_BUILTIN_BUILTINDATA_DISTORTION_BLUR:
        result = builtinData.distortionBlur.xxx;
        break;
    }
}

void GetLighTransportDataDebug(uint paramId, LighTransportData lightTransportData, inout float3 result, inout bool needLinearToSRGB)
{
    switch (paramId)
    {
    case DEBUGVIEW_BUILTIN_LIGHTRANSPORTDATA_DIFFUSE_COLOR:
        result = lightTransportData.diffuseColor; needLinearToSRGB = true;
        break;
    case DEBUGVIEW_BUILTIN_LIGHTRANSPORTDATA_EMISSIVE_COLOR:
        // TODO: Need a tonemap ?
        result = lightTransportData.emissiveColor;
        break;
    }    
}

#endif // UNITY_BUILTIN_DATA_INCLUDED
