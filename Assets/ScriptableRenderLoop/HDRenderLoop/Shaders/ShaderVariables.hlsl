// UNITY_SHADER_NO_UPGRADE

#ifndef UNITY_SHADER_VARIABLES_INCLUDED
#define UNITY_SHADER_VARIABLES_INCLUDED

// CAUTION:
// Currently the shaders compiler always include regualr Unity shaderVariables, so I get a conflict here were UNITY_SHADER_VARIABLES_INCLUDED is already define, this need to be fixed.
// As I haven't change the variables name yet, I simply don't define anything, and I put the transform function at the end of the file outside the guard header.
// This need to be fixed.

float4x4 glstate_matrix_inv_projection;
#define UNITY_MATRIX_M unity_ObjectToWorld

// These are updated per eye in VR
#define UNITY_MATRIX_V unity_MatrixV
#define UNITY_MATRIX_P glstate_matrix_projection
#define UNITY_MATRIX_VP unity_MatrixVP

#ifdef UNITY_SINGLE_PASS_STEREO
    #define UNITY_MATRIX_MVP mul(unity_MatrixVP, unity_ObjectToWorld)
#else
    #define UNITY_MATRIX_MVP glstate_matrix_mvp
#endif

// These use the camera center position in VR
#define UNITY_MATRIX_MV glstate_matrix_modelview0
#define UNITY_MATRIX_T_MV glstate_matrix_transpose_modelview0
#define UNITY_MATRIX_IT_MV glstate_matrix_invtrans_modelview0


// ----------------------------------------------------------------------------


CBUFFER_START(UnityPerCamera)
    // Time (t = time since current level load) values from Unity
    float4 _Time; // (t/20, t, t*2, t*3)
    float4 _SinTime; // sin(t/8), sin(t/4), sin(t/2), sin(t)
    float4 _CosTime; // cos(t/8), cos(t/4), cos(t/2), cos(t)
    float4 unity_DeltaTime; // dt, 1/dt, smoothdt, 1/smoothdt

#ifndef UNITY_SINGLE_PASS_STEREO
    float3 _WorldSpaceCameraPos;
#endif

    // x = 1 or -1 (-1 if projection is flipped)
    // y = near plane
    // z = far plane
    // w = 1/far plane
    float4 _ProjectionParams;

    // x = width
    // y = height
    // z = 1 + 1.0/width
    // w = 1 + 1.0/height
    float4 _ScreenParams;

    // Values used to linearize the Z buffer (http://www.humus.name/temp/Linearize%20depth.txt)
    // x = 1-far/near
    // y = far/near
    // z = x/far
    // w = y/far
    float4 _ZBufferParams;

    // x = orthographic camera's width
    // y = orthographic camera's height
    // z = unused
    // w = 1.0 if camera is ortho, 0.0 if perspective
    float4 unity_OrthoParams;
CBUFFER_END


CBUFFER_START(UnityPerCameraRare)
    float4 unity_CameraWorldClipPlanes[6];

    // Projection matrices of the camera. Note that this might be different from projection matrix
    // that is set right now, e.g. while rendering shadows the matrices below are still the projection
    // of original camera.
    float4x4 unity_CameraProjection;
    float4x4 unity_CameraInvProjection;

#ifndef UNITY_SINGLE_PASS_STEREO
    float4x4 unity_WorldToCamera;
    float4x4 unity_CameraToWorld;
#endif
CBUFFER_END

// ----------------------------------------------------------------------------

CBUFFER_START(UnityPerDraw)
#ifndef UNITY_SINGLE_PASS_STEREO
    float4x4 glstate_matrix_mvp;
#endif

    // Use center position for stereo rendering
    float4x4 glstate_matrix_modelview0;
    float4x4 glstate_matrix_invtrans_modelview0;

    float4x4 unity_ObjectToWorld;
    float4x4 unity_WorldToObject;
    float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
    float4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms
CBUFFER_END

#ifdef UNITY_SINGLE_PASS_STEREO
CBUFFER_START(UnityPerEye)
    float3 _WorldSpaceCameraPos;
    float4x4 glstate_matrix_projection;

    float4x4 unity_MatrixV;
    float4x4 unity_MatrixVP;

    float4x4 unity_WorldToCamera;
    float4x4 unity_CameraToWorld;
CBUFFER_END
#endif

CBUFFER_START(UnityPerDrawRare)
    float4x4 glstate_matrix_transpose_modelview0;
#ifdef UNITY_SINGLE_PASS_STEREO
    float4x4 glstate_matrix_mvp;
#endif
CBUFFER_END


// ----------------------------------------------------------------------------

CBUFFER_START(UnityPerFrame)

#ifndef UNITY_SINGLE_PASS_STEREO
    float4x4 glstate_matrix_projection;
    float4x4 unity_MatrixV;
    float4x4 unity_MatrixVP;
#endif

    float4 glstate_lightmodel_ambient;
    float4 unity_AmbientSky;
    float4 unity_AmbientEquator;
    float4 unity_AmbientGround;
    float4 unity_IndirectSpecColor;

CBUFFER_END


CBUFFER_START(UnityLighting)
	// SH lighting environment
	float4 unity_SHAr;
	float4 unity_SHAg;
	float4 unity_SHAb;
	float4 unity_SHBr;
	float4 unity_SHBg;
	float4 unity_SHBb;
	float4 unity_SHC;
CBUFFER_END

// Main lightmap
TEXTURE2D(unity_Lightmap);
SAMPLER2D(samplerunity_Lightmap);
// Dual or directional lightmap (always used with unity_Lightmap, so can share sampler)
TEXTURE2D(unity_LightmapInd);

// Dynamic GI lightmap
TEXTURE2D(unity_DynamicLightmap);
SAMPLER2D(samplerunity_DynamicLightmap);

TEXTURE2D(unity_DynamicDirectionality);

CBUFFER_START(UnityLightmaps)
float4 unity_LightmapST;
float4 unity_DynamicLightmapST;
CBUFFER_END

// TODO: Change code here so probe volume use only one transform instead of all this parameters!
TEXTURE3D(unity_ProbeVolumeSH);
SAMPLER3D(samplerunity_ProbeVolumeSH)

CBUFFER_START(UnityProbeVolume)
	// x = Disabled(0)/Enabled(1)
	// y = Computation are done in global space(0) or local space(1)
	// z = Texel size on U texture coordinate
	float4 unity_ProbeVolumeParams;

	float4x4 unity_ProbeVolumeWorldToObject;
	float3 unity_ProbeVolumeSizeInv;
	float3 unity_ProbeVolumeMin;		
CBUFFER_END

// ----------------------------------------------------------------------------

// TODO: move this to constant buffer by Pass
float4		_ScreenSize;


float4x4 GetObjectToWorldMatrix()
{
    return unity_ObjectToWorld;
}

float4x4 GetWorldToObjectMatrix()
{
    return unity_WorldToObject;
}

// Transform to homogenous clip space
float4x4 GetWorldToHClipMatrix()
{
    return unity_MatrixVP;
}

// Transform from clip space to homogenous world space
float4x4 GetClipToHWorldMatrix()
{
    return glstate_matrix_inv_projection;
}

float GetOdddNegativeScale()
{
    return unity_WorldTransformParams.w;
}


float4x4 GetObjectToWorldViewMatrix()
{
    return glstate_matrix_modelview0;
}

float3 TransformObjectToWorld(float3 positionOS)
{
    return mul(GetObjectToWorldMatrix(), float4(positionOS, 1.0)).xyz;
}

float3 TransformObjectToView(float3 positionOS)
{
    return mul(GetObjectToWorldViewMatrix(), float4(positionOS, 1.0)).xyz;
}

float3 TransformObjectToWorldDir(float3 dirOS)
{
    // Normalize to support uniform scaling
    return normalize(mul((float3x3)GetObjectToWorldMatrix(), dirOS));
}

// Transforms normal from object to world space
float3 TransformObjectToWorldNormal(float3 normalOS)
{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
    return UnityObjectToWorldDir(normalOS);
#else
    // Normal need to be multiply by inverse transpose
    // mul(IT_M, norm) => mul(norm, I_M) => {dot(norm, I_M.col0), dot(norm, I_M.col1), dot(norm, I_M.col2)}
    return normalize(mul(normalOS, (float3x3)GetWorldToObjectMatrix()));
#endif
}

// Tranforms position from world space to homogenous space
float4 TransformWorldToHClip(float3 positionWS)
{
    return mul(GetWorldToHClipMatrix(), float4(positionWS, 1.0));
}

float3x3 CreateTangentToWorld(float3 normal, float3 tangent, float tangentSign)
{
    // For odd-negative scale transforms we need to flip the sign
    float sign = tangentSign * GetOdddNegativeScale();
    float3 bitangent = cross(normal, tangent) * sign;

    return float3x3(tangent, bitangent, normal);
}

// Computes world space view direction, from object space position
float3 GetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    return normalize(_WorldSpaceCameraPos.xyz - positionWS);
}

float3 TransformTangentToWorld(float3 dirTS, float3 tangentToWorld[3])
{
    // TODO check: do we need to normalize ?
    return normalize(mul(dirTS, float3x3(tangentToWorld[0].xyz, tangentToWorld[1].xyz, tangentToWorld[2].xyz)));
}

// Assume TBN is orthonormal.
float3 TransformWorldToTangent(float3 dirWS, float3 tangentToWorld[3])
{
    // TODO check: do we need to normalize ?
    return normalize(mul(float3x3(tangentToWorld[0].xyz, tangentToWorld[1].xyz, tangentToWorld[2].xyz), dirWS));
}

#endif // UNITY_SHADER_VARIABLES_INCLUDED
