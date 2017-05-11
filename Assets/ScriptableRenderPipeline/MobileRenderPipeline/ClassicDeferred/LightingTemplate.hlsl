#ifndef __DEFERREDLIGHTINGTEMPLATE_H__
#define __DEFERREDLIGHTINGTEMPLATE_H__


#include "UnityCG.cginc"
#include "UnityStandardBRDF.cginc"
#include "UnityStandardUtils.cginc"
#include "UnityPBSLighting.cginc"

//uniform uint g_nNumDirLights;

//---------------------------------------------------------------------------------------------------------------------------------------------------------
// TODO:  clean up.. -va
#define MAX_SHADOW_LIGHTS 10
#define MAX_SHADOWMAP_PER_LIGHT 6
#define MAX_DIRECTIONAL_SPLIT  4

#define CUBEMAPFACE_POSITIVE_X 0
#define CUBEMAPFACE_NEGATIVE_X 1
#define CUBEMAPFACE_POSITIVE_Y 2
#define CUBEMAPFACE_NEGATIVE_Y 3
#define CUBEMAPFACE_POSITIVE_Z 4
#define CUBEMAPFACE_NEGATIVE_Z 5

UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF(0);
UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF(1);
UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF(2);
UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(3);

// from LightDefinitions.cs.hlsl
#define SPOT_LIGHT (0)
#define SPHERE_LIGHT (1)
#define BOX_LIGHT (2)
#define DIRECTIONAL_LIGHT (3)

CBUFFER_START(ShadowLightData)

float4 g_vShadow3x3PCFTerms0;
float4 g_vShadow3x3PCFTerms1;
float4 g_vShadow3x3PCFTerms2;
float4 g_vShadow3x3PCFTerms3;

float4 g_vDirShadowSplitSpheres[MAX_DIRECTIONAL_SPLIT];
float4x4 g_matWorldToShadow[MAX_SHADOW_LIGHTS * MAX_SHADOWMAP_PER_LIGHT];

CBUFFER_END
//---------------------------------------------------------------------------------------------------------------------------------------------------------

UNITY_DECLARE_DEPTH_TEXTURE(_CameraGBufferZ);

// UNITY_DECLARE_TEX2D(_LightTextureB0);
// sampler2D _LightTextureB0;
// UNITY_DECLARE_TEX2DARRAY(_spotCookieTextures);
// UNITY_DECLARE_ABSTRACT_CUBE_ARRAY(_pointCookieTextures);

// StructuredBuffer<DirectionalLight> g_dirLightData;

#define REVERSE_ZBUF
#define DECLARE_SHADOWMAP( tex ) Texture2D tex; SamplerComparisonState sampler##tex
#ifdef REVERSE_ZBUF
	#define SAMPLE_SHADOW( tex, coord ) tex.SampleCmpLevelZero( sampler##tex, (coord).xy, (coord).z )
#else
	#define SAMPLE_SHADOW( tex, coord ) tex.SampleCmpLevelZero( sampler##tex, (coord).xy, 1.0-(coord).z )
#endif

DECLARE_SHADOWMAP(g_tShadowBuffer);

float ComputeShadow_PCF_3x3_Gaussian(float3 vPositionWs, float4x4 matWorldToShadow)
{
    float4 vPositionTextureSpace = mul(float4(vPositionWs.xyz, 1.0), matWorldToShadow);
    vPositionTextureSpace.xyz /= vPositionTextureSpace.w;

    float2 shadowMapCenter = vPositionTextureSpace.xy;

    if ((shadowMapCenter.x < 0.0f) || (shadowMapCenter.x > 1.0f) || (shadowMapCenter.y < 0.0f) || (shadowMapCenter.y > 1.0f))
        return 1.0f;

    float objDepth = saturate(257.0 / 256.0 - vPositionTextureSpace.z);

    float4 v20Taps;
    v20Taps.x = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms1.xy, objDepth)).x; //  1  1
    v20Taps.y = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms1.zy, objDepth)).x; // -1  1
    v20Taps.z = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms1.xw, objDepth)).x; //  1 -1
    v20Taps.w = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms1.zw, objDepth)).x; // -1 -1
    float flSum = dot(v20Taps.xyzw, float4(0.25, 0.25, 0.25, 0.25));
    if ((flSum == 0.0) || (flSum == 1.0))
        return flSum;
    flSum *= g_vShadow3x3PCFTerms0.x * 4.0;

    float4 v33Taps;
    v33Taps.x = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms2.xz, objDepth)).x; //  1  0
    v33Taps.y = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms3.xz, objDepth)).x; // -1  0
    v33Taps.z = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms3.zy, objDepth)).x; //  0 -1
    v33Taps.w = SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy + g_vShadow3x3PCFTerms2.zy, objDepth)).x; //  0  1
    flSum += dot(v33Taps.xyzw, g_vShadow3x3PCFTerms0.yyyy);

    flSum += SAMPLE_SHADOW(g_tShadowBuffer, float3(shadowMapCenter.xy, objDepth)).x * g_vShadow3x3PCFTerms0.z;

    return flSum;
}

//---------------------------------------------------------------------------------------------------------------------------------------------------------
/**
* Gets the cascade weights based on the world position of the fragment and the positions of the split spheres for each cascade.
* Returns an invalid split index if past shadowDistance (ie 4 is invalid for cascade)
*/
float GetSplitSphereIndexForDirshadows(float3 wpos)
{
    float3 fromCenter0 = wpos.xyz - g_vDirShadowSplitSpheres[0].xyz;
    float3 fromCenter1 = wpos.xyz - g_vDirShadowSplitSpheres[1].xyz;
    float3 fromCenter2 = wpos.xyz - g_vDirShadowSplitSpheres[2].xyz;
    float3 fromCenter3 = wpos.xyz - g_vDirShadowSplitSpheres[3].xyz;
    float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));

    float4 vDirShadowSplitSphereSqRadii;
    vDirShadowSplitSphereSqRadii.x = g_vDirShadowSplitSpheres[0].w;
    vDirShadowSplitSphereSqRadii.y = g_vDirShadowSplitSpheres[1].w;
    vDirShadowSplitSphereSqRadii.z = g_vDirShadowSplitSpheres[2].w;
    vDirShadowSplitSphereSqRadii.w = g_vDirShadowSplitSpheres[3].w;
    fixed4 weights = float4(distances2 < vDirShadowSplitSphereSqRadii);
    weights.yzw = saturate(weights.yzw - weights.xyz);
    return 4 - dot(weights, float4(4, 3, 2, 1));
}

float SampleShadow(uint type, float3 vPositionWs, float3 vPositionToLightDirWs, uint lightIndex)
{
    float flShadowScalar = 1.0;
    int shadowSplitIndex = 0;

    if (type == DIRECTIONAL_LIGHT)
    {
        shadowSplitIndex = GetSplitSphereIndexForDirshadows(vPositionWs);
    }

    else if (type == SPHERE_LIGHT)
    {
        float3 absPos = abs(vPositionToLightDirWs);
        shadowSplitIndex = (vPositionToLightDirWs.z > 0) ? CUBEMAPFACE_NEGATIVE_Z : CUBEMAPFACE_POSITIVE_Z;
        if (absPos.x > absPos.y)
        {
            if (absPos.x > absPos.z)
            {
                shadowSplitIndex = (vPositionToLightDirWs.x > 0) ? CUBEMAPFACE_NEGATIVE_X : CUBEMAPFACE_POSITIVE_X;
            }
        }
        else
        {
            if (absPos.y > absPos.z)
            {
                shadowSplitIndex = (vPositionToLightDirWs.y > 0) ? CUBEMAPFACE_NEGATIVE_Y : CUBEMAPFACE_POSITIVE_Y;
            }
        }
    }

    flShadowScalar = ComputeShadow_PCF_3x3_Gaussian(vPositionWs.xyz, g_matWorldToShadow[lightIndex * MAX_SHADOWMAP_PER_LIGHT + shadowSplitIndex]);
    return flShadowScalar;
}

//debug
//float3 GetViewPosFromLinDepth(float2 v2ScrPos, float fLinDepth)
//{
//	float fSx = UNITY_MATRIX_P[0].x;
//	float fCx = UNITY_MATRIX_P[0].z;
//	float fSy = UNITY_MATRIX_P[1].y;
//	float fCy = UNITY_MATRIX_P[1].z;
//
//	return fLinDepth*float3( ((v2ScrPos.x-fCx)/fSx), ((v2ScrPos.y-fCy)/fSy), 1.0 );
//}

// --------------------------------------------------------
// Common lighting data calculation (direction, attenuation, ...)

void OnChipDeferredFragSetup (
	inout unity_v2f_deferred i,
	out float2 outUV,
	out float4 outVPos,
	out float3 outWPos,
	float depth
	)
{
	i.ray = i.ray * (_ProjectionParams.z / i.ray.z);
	float2 uv = i.uv.xy / i.uv.w;
	//float2 uv = i.pos.xy / float2(_ScreenParams.xy);

	depth = Linear01Depth (depth);

	//debug
	//float linDepth = depth * (_ProjectionParams.z-_ProjectionParams.y) + _ProjectionParams.y;
	//float4 vpos = float4(GetViewPosFromLinDepth((2*uv-1)*float2(1, -1), linDepth), 1);

	float4 vpos = float4(i.ray * depth,1);
	float3 wpos = mul (unity_CameraToWorld, vpos).xyz;

	outUV = uv;
	outVPos = vpos;
	outWPos = wpos;
}

static float4 debugLighting;

int _LightIndexForShadowMatrixArray;

void OnChipDeferredCalculateLightParams (
	unity_v2f_deferred i,
	out float3 outWorldPos,
	out float2 outUV,
	out half3 outLightDir,
	out float outAtten,
	out float outFadeDist,
	out float4 outCookieColor,
	float depth
	)
{
	float4 vpos;
	float3 wpos;
	float2 uv; 
	float4 colorCookie = float4(1, 1, 1, 1);

	OnChipDeferredFragSetup(i, uv, vpos, wpos, depth); 

	// needed? old shadow code is commented out, we switched to new shadow code .. aka sampleShadow()
	float fadeDist = UnityComputeShadowFadeDistance(wpos, vpos.z);

	// spot light case
	#if defined (SPOT)	
		float3 tolight = _LightPos.xyz - wpos;
		half3 lightDir = normalize (tolight);
		
		float4 uvCookie = mul (unity_WorldToLight, float4(wpos,1));
		colorCookie = tex2Dlod (_LightTexture0, float4(uvCookie.xy / uvCookie.w, 0, 0));
		float atten = colorCookie.w;
		atten *= uvCookie.w < 0;

		float att = dot(tolight, tolight) * _LightPos.w;
		atten *= tex2D (_LightTextureB0, att.rr).UNITY_ATTEN_CHANNEL;

		if (_LightIndexForShadowMatrixArray >= 0)
			atten *= SampleShadow(SPOT_LIGHT, wpos, 0, _LightIndexForShadowMatrixArray);
	
	// directional light case		
	#elif defined (DIRECTIONAL) || defined (DIRECTIONAL_COOKIE)
		half3 lightDir = -_LightDir.xyz;
		float atten = 1.0;

		if (_LightIndexForShadowMatrixArray >= 0)
			atten *= SampleShadow(DIRECTIONAL_LIGHT, wpos, 0, _LightIndexForShadowMatrixArray);

		#if defined (DIRECTIONAL_COOKIE)
		colorCookie = tex2Dlod (_LightTexture0, float4(mul(unity_WorldToLight, half4(wpos,1)).xy, 0, 0));
		atten *= colorCookie.w;
		#endif //DIRECTIONAL_COOKIE

	// point light case	
	#elif defined (POINT) || defined (POINT_COOKIE)
		float3 tolight = wpos - _LightPos.xyz;
		half3 lightDir = -normalize (tolight);
		
		float att = dot(tolight, tolight) * _LightPos.w;
		float atten = tex2D (_LightTextureB0, att.rr).UNITY_ATTEN_CHANNEL;

		if (_LightIndexForShadowMatrixArray >= 0)
			atten *= SampleShadow(SPHERE_LIGHT, wpos, lightDir, _LightIndexForShadowMatrixArray);

		#if defined (POINT_COOKIE)
			colorCookie = texCUBElod(_LightTexture0, float4(mul(unity_WorldToLight, float4(wpos,1)).xyz, 0));
			atten *= colorCookie.w;
		#endif //POINT_COOKIE	
	#else
		half3 lightDir = 0;
		float atten = 0;
	#endif

	outWorldPos = wpos;
	outUV = uv;
	outLightDir = lightDir;
	outAtten = atten;
	outFadeDist = fadeDist;
	outCookieColor = colorCookie;
}


half4 CalculateLight (unity_v2f_deferred i)
{
	float3 wpos;
	float2 uv;
	float atten, fadeDist;
	float4 colorCookie = float4(1, 1, 1, 1);

	UnityLight light;
	UNITY_INITIALIZE_OUTPUT(UnityLight, light);
	float depth = UNITY_READ_FRAMEBUFFER_INPUT(3, i.pos);

	//debugLighting = float4(0.0, 0.0, 0.0, 0.0);

	OnChipDeferredCalculateLightParams (i, wpos, uv, light.dir, atten, fadeDist, colorCookie, depth);

	#if defined (POINT_COOKIE) || defined (DIRECTIONAL_COOKIE) || defined (SPOT)
		light.color = _LightColor.rgb * colorCookie.rgb * atten;
	#else
		light.color = _LightColor.rgb * atten;
	#endif

	half4 gbuffer0 = UNITY_READ_FRAMEBUFFER_INPUT(0, i.pos);
	half4 gbuffer1 = UNITY_READ_FRAMEBUFFER_INPUT(1, i.pos);
	half4 gbuffer2 = UNITY_READ_FRAMEBUFFER_INPUT(2, i.pos);

	UnityStandardData data = UnityStandardDataFromGbuffer(gbuffer0, gbuffer1, gbuffer2);

	float3 eyeVec = normalize(wpos-_WorldSpaceCameraPos);
	half oneMinusReflectivity = 1 - SpecularStrength(data.specularColor.rgb);

	UnityIndirect ind;
	UNITY_INITIALIZE_OUTPUT(UnityIndirect, ind);
	ind.diffuse = 0;
	ind.specular = 0;

	// UNITY_BRDF_PBS1 writes out alpha 1 to our emission alpha. 
    half4 res = UNITY_BRDF_PBS (data.diffuseColor, data.specularColor, oneMinusReflectivity, data.smoothness, data.normalWorld, -eyeVec, light, ind);

    //return debugLighting;

	return res;
}

unity_v2f_deferred onchip_vert_deferred (float4 vertex : POSITION, float3 normal : NORMAL)
{
    bool lightAsQuad = _LightAsQuad!=0.0;

    unity_v2f_deferred o;

    // scaling quad by two becuase built-in unity quad.fbx ranges from -0.5 to 0.5
    o.pos = lightAsQuad ? float4(2.0*vertex.xy, 0.5, 1.0) : UnityObjectToClipPos(vertex);
    o.uv = ComputeScreenPos(o.pos);

    // normal contains a ray pointing from the camera to one of near plane's
    // corners in camera space when we are drawing a full screen quad.
    // Otherwise, when rendering 3D shapes, use the ray calculated here.
    if (lightAsQuad){
    	float2 rayXY = mul(unity_CameraInvProjection, float4(o.pos.x, -o.pos.y, -1, 1)).xy;
        o.ray = float3(rayXY, 1.0);
    }
    else
    {
    	o.ray = UnityObjectToViewPos(vertex) * float3(-1,-1,1);
    }
    return o;
}


#endif
