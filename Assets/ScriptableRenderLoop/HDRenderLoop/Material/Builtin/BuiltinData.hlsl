#ifndef UNITY_BUILTIN_DATA_INCLUDED
#define UNITY_BUILTIN_DATA_INCLUDED

//-----------------------------------------------------------------------------
// BuiltinData
// This structure include common data that should be present in all material
// and are independent from the BSDF parametrization.
// Note: These parameters can be store in GBuffer if the writer wants
//-----------------------------------------------------------------------------

#include "BuiltinData.cs.hlsl"

//-----------------------------------------------------------------------------
// common Encode/Decode functions
//-----------------------------------------------------------------------------

// Guideline for velocity buffer.
// We support various architecture for HDRenderLoop
// - Forward only rendering
// - Hybrid forward/deferred opaque
// - Regular deferred
// The velocity buffer is potentially fill in several pass.
// - In gbuffer pass with extra RT
// - In forward opaque pass (Can happen even when deferred) with MRT
// - In dedicated velocity pass
// Also the velocity buffer is only fill in case of dynamic or deformable objects, static case can use camera reprojection to retrieve motion vector (<= TODO: this may be false with TAA due to jitter matrix)
// or just previous and current transform

// So here we decide the following rules:
// - A deferred material can't override the velocity buffer format of builtinData, must use appropriate function
// - If velocity buffer is enable in deferred material it is the last one
// - Velocity buffer can be optionally enabled (either in forward or deferred)
// - Velocity data can't be pack with other properties
// - Same velocity buffer is use for all scenario, so if deferred define a velocity buffer, the same is reuse for forward case.
// For these reasons we chose to avoid to pack velocity buffer with anything else in case of PackgbufferInFP16 (and also in case the format change)

// Encode/Decode velocity/distortion in a buffer (either forward of deferred)
// Design note: We assume that velocity/distortion fit into a single buffer (i.e not spread on several buffer)
void EncodeVelocity(float2 velocity, out float4 outBuffer)
{
    // RT - 16:16 float
    outBuffer = float4(velocity.xy, 0.0, 0.0);
}

void DecodeVelocity(float4 inBuffer, out float2 velocity)
{
    velocity = inBuffer.xy;
}

void EncodeDistortion(float2 distortion, float distortionBlur, out float4 outBuffer)
{
    // RT - 16:16 float
    outBuffer = float4(distortion, distortionBlur, 0.0);
}

void DecodeDistortion(float4 inBuffer, out float2 distortion, out float2 distortionBlur)
{
    distortion = inBuffer.xy;
    distortionBlur = inBuffer.z;
}

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
        // emissiveColor is premultiply by emissive intensity
        result = (builtinData.emissiveColor / builtinData.emissiveIntensity); needLinearToSRGB = true;
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
