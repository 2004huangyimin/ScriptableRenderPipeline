//
// This file was automatically generated from Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/Material/Builtin/BuiltinData.cs.  Please don't edit by hand.
//

//
// UnityEngine.Experimental.ScriptableRenderLoop.Builtin.BuiltinData:  static fields
//
#define DEBUGVIEW_BUILTIN_BUILTINDATA_OPACITY (100)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_BAKE_DIFFUSE_LIGHTING (101)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_EMISSIVE_COLOR (102)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_EMISSIVE_INTENSITY (103)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_VELOCITY (104)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_DISTORTION (105)
#define DEBUGVIEW_BUILTIN_BUILTINDATA_DISTORTION_BLUR (106)

//
// UnityEngine.Experimental.ScriptableRenderLoop.Builtin.LighTransportData:  static fields
//
#define DEBUGVIEW_BUILTIN_LIGHTRANSPORTDATA_DIFFUSE_COLOR (120)
#define DEBUGVIEW_BUILTIN_LIGHTRANSPORTDATA_EMISSIVE_COLOR (121)

// Generated from UnityEngine.Experimental.ScriptableRenderLoop.Builtin.BuiltinData
// PackingRules = Exact
struct BuiltinData
{
	float opacity;
	float3 bakeDiffuseLighting;
	float3 emissiveColor;
	float emissiveIntensity;
	float2 velocity;
	float2 distortion;
	float distortionBlur;
};

// Generated from UnityEngine.Experimental.ScriptableRenderLoop.Builtin.LighTransportData
// PackingRules = Exact
struct LighTransportData
{
	float3 diffuseColor;
	float3 emissiveColor;
};


