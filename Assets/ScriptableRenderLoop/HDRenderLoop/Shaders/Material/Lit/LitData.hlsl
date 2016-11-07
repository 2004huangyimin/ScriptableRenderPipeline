//-------------------------------------------------------------------------------------
// FragInput
// This structure gather all possible varying/interpolator for this shader.
//-------------------------------------------------------------------------------------

struct FragInput
{
    float4 unPositionSS; // This is the position return by VPOS, only xy is use
    float3 positionWS;
    float2 texCoord0;
    float2 texCoord1;
    float2 texCoord2;
    float3 tangentToWorld[3];
    bool isFrontFace;
};

//-------------------------------------------------------------------------------------
// Fill SurfaceData/Builtin data function
//-------------------------------------------------------------------------------------

// In unity we can have a mix of fully baked lightmap (static lightmap) + enlighten realtime lightmap (dynamic lightmap)
// for each case we can have directional lightmap or not.
// Else we have lightprobe for dynamic/moving entity. Either SH9 per object lightprobe or SH4 per pixel per object volume probe
float3 SampleBakedGI(float3 positionWS, float3 normalWS, float2 uvStaticLightmap, float2 uvDynamicLightmap)
{
    // If there is no lightmap, it assume lightprobe
#if !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON)

// TODO: Confirm with Ionut but it seems that UNITY_LIGHT_PROBE_PROXY_VOLUME is always define for high end and 
// unity_ProbeVolumeParams always bind.
    if (unity_ProbeVolumeParams.x == 0.0)
    {
        // TODO: pass a tab of coefficient instead!
        float4 SHCoefficients[7];
        SHCoefficients[0] = unity_SHAr;
        SHCoefficients[1] = unity_SHAg;
        SHCoefficients[2] = unity_SHAb;
        SHCoefficients[3] = unity_SHBr;
        SHCoefficients[4] = unity_SHBg;
        SHCoefficients[5] = unity_SHBb;
        SHCoefficients[6] = unity_SHC;

        return SampleSH9(SHCoefficients, normalWS);
    }
    else
    {
        // TODO: Move all this to C++!
        float4x4 identity = 0;
        identity._m00_m11_m22_m33 = 1.0;
        float4x4 WorldToTexture = (unity_ProbeVolumeParams.y == 1.0f) ? unity_ProbeVolumeWorldToObject : identity;

        float4x4 translation = identity;
        translation._m30_m31_m32 = -unity_ProbeVolumeMin.xyz;

        float4x4 scale = 0;
        scale._m00_m11_m22_m33 = float4(unity_ProbeVolumeSizeInv.xyz, 1.0);

        WorldToTexture = mul(mul(scale, translation), WorldToTexture);
    
        return SampleProbeVolumeSH4(TEXTURE3D_PASS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH), positionWS, normalWS, WorldToTexture, unity_ProbeVolumeParams.z);
    }

#else

    float3 bakeDiffuseLighting = float3(0.0, 0.0, 0.0);

    #ifdef LIGHTMAP_ON
        #ifdef DIRLIGHTMAP_COMBINED
        bakeDiffuseLighting += SampleDirectionalLightmap(TEXTURE2D_PASS(unity_Lightmap, samplerunity_Lightmap),
                                                        TEXTURE2D_PASS(unity_LightmapInd, samplerunity_Lightmap),
                                                        uvStaticLightmap, unity_LightmapST);
        #else
        bakeDiffuseLighting += SampleSingleLightmap(TEXTURE2D_PASS(unity_Lightmap, samplerunity_Lightmap), uvStaticLightmap, unity_LightmapST);
        #endif
    #endif

    #ifdef DYNAMICLIGHTMAP_ON
        #ifdef DIRLIGHTMAP_COMBINED
        bakeDiffuseLighting += SampleDirectionalLightmap(TEXTURE2D_PASS(unity_DynamicLightmap, samplerunity_DynamicLightmap),
                                                        TEXTURE2D_PASS(unity_DynamicDirectionality, samplerunity_DynamicLightmap),
                                                        uvDynamicLightmap, unity_DynamicLightmapST);
        #else
        bakeDiffuseLighting += SampleSingleLightmap(TEXTURE2D_PASS(unity_DynamicLightmap, samplerunity_DynamicLightmap), uvDynamicLightmap, unity_DynamicLightmapST);
        #endif
    #endif

    return bakeDiffuseLighting;

#endif
}

void GetSurfaceAndBuiltinData(FragInput input, out SurfaceData surfaceData, out BuiltinData builtinData)
{    
#ifdef _HEIGHTMAP
    // TODO: in case of shader graph, a node like parallax must be nullify if use to generate code for Meta pass
    #ifndef _HEIGHTMAP_AS_DISPLACEMENT
    float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS); // This should be remove by the compiler as we usually cal it before.
    float height = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, input.texCoord0).r * _HeightScale + _HeightBias;
    // Transform view vector in tangent space
    TransformWorldToTangent(V, input.tangentToWorld);
    float2 offset = ParallaxOffset(viewDirTS, height);
    input.texCoord0 += offset;
    input.texCoord1 += offset;
    #endif
#endif

    surfaceData.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.texCoord0).rgb * _BaseColor.rgb;
#ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
    float alpha = _BaseColor.a;
#else
    float alpha = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.texCoord0).a * _BaseColor.a;
#endif

#ifdef _ALPHATEST_ON
    clip(alpha - _AlphaCutoff);
#endif

#ifdef _SPECULAROCCLUSIONMAP
    // TODO: Do something. For now just take alpha channel
    surfaceData.specularOcclusion = SAMPLE_TEXTURE2D(_SpecularOcclusionMap, sampler_SpecularOcclusionMap, input.texCoord0).a;
#else
    // Horizon Occlusion for Normal Mapped Reflections: http://marmosetco.tumblr.com/post/81245981087
    //surfaceData.specularOcclusion = saturate(1.0 + horizonFade * dot(r, input.tangentToWorld[2].xyz);
    // smooth it
    //surfaceData.specularOcclusion *= surfaceData.specularOcclusion;
    surfaceData.specularOcclusion = 1.0;
#endif

    // TODO: think about using BC5
    float3 vertexNormalWS = input.tangentToWorld[2].xyz;

#ifdef _NORMALMAP
    #ifdef _NORMALMAP_TANGENT_SPACE
    float3 normalTS = UnpackNormalAG(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.texCoord0));
    surfaceData.normalWS = TransformTangentToWorld(normalTS, input.tangentToWorld);
    #else // Object space (TODO: We need to apply the world rotation here! - Require to pass world transform)
    surfaceData.normalWS = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.texCoord0).rgb;
    #endif
#else
    surfaceData.normalWS = vertexNormalWS;
#endif

#if defined(_DOUBLESIDED_LIGHTING_FLIP) || defined(_DOUBLESIDED_LIGHTING_MIRROR)
    #ifdef _DOUBLESIDED_LIGHTING_FLIP
    float3 oppositeNormalWS = -surfaceData.normalWS;
    #else
    // Mirror the normal with the plane define by vertex normal
    float3 oppositeNormalWS = reflect(surfaceData.normalWS, vertexNormalWS);
#endif
    // TODO : Test if GetOdddNegativeScale() is necessary here in case of normal map, as GetOdddNegativeScale is take into account in CreateTangentToWorld();
    surfaceData.normalWS =  input.isFrontFace ? 
                                (GetOdddNegativeScale() >= 0.0 ? surfaceData.normalWS : oppositeNormalWS) :
                                (-GetOdddNegativeScale() >= 0.0 ? surfaceData.normalWS : oppositeNormalWS);
#endif

#ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
    surfaceData.perceptualSmoothness = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.texCoord0).a;
#elif defined(_MASKMAP)
    surfaceData.perceptualSmoothness = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.texCoord0).a;
#else
    surfaceData.perceptualSmoothness = 1.0;
#endif
    surfaceData.perceptualSmoothness *= _Smoothness;

    surfaceData.materialId = 0;

    // MaskMap is Metallic, Ambient Occlusion, (Optional) - emissive Mask, Optional - Smoothness (in alpha)
#ifdef _MASKMAP
    surfaceData.metallic = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.texCoord0).r;
    surfaceData.ambientOcclusion = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.texCoord0).g;
#else
    surfaceData.metallic = 1.0;
    surfaceData.ambientOcclusion = 1.0;
#endif
    surfaceData.metallic *= _Metallic;

    // TODO: think about using BC5
#ifdef _TANGENTMAP
#ifdef _NORMALMAP_TANGENT_SPACE // Normal and tangent use same space
    float3 tangentTS = UnpackNormalAG(SAMPLE_TEXTURE2D(_TangentMap, sampler_TangentMap, input.texCoord0));
    surfaceData.tangentWS = TransformTangentToWorld(tangentTS, input.tangentToWorld);
#else // Object space (TODO: We need to apply the world rotation here! - Require to pass world transform)
    surfaceData.tangentWS = SAMPLE_TEXTURE2D(_TangentMap, sampler_TangentMap, input.texCoord0).rgb;
#endif
#else
    surfaceData.tangentWS = input.tangentToWorld[0].xyz;
#endif
    // TODO: Is there anything todo regarding flip normal but for the tangent ?

#ifdef _ANISOTROPYMAP
    surfaceData.anisotropy = SAMPLE_TEXTURE2D(_AnisotropyMap, sampler_AnisotropyMap, input.texCoord0).r;
#else
    surfaceData.anisotropy = 1.0;
#endif
    surfaceData.anisotropy *= _Anisotropy;

    surfaceData.specular = 0.04;

    surfaceData.subSurfaceRadius = 1.0;
    surfaceData.thickness = 0.0;
    surfaceData.subSurfaceProfile = 0;

    surfaceData.coatNormalWS = float3(1.0, 0.0, 0.0);
    surfaceData.coatPerceptualSmoothness = 1.0;
    surfaceData.specularColor = float3(0.0, 0.0, 0.0);


    // Builtin Data
    builtinData.opacity = alpha;

    // TODO: Sample lightmap/lightprobe/volume proxy
    // This should also handle projective lightmap
    // Note that data input above can be use to sample into lightmap (like normal)
    builtinData.bakeDiffuseLighting = SampleBakedGI(input.positionWS, surfaceData.normalWS, input.texCoord1, input.texCoord2);

    // If we chose an emissive color, we have a dedicated texture for it and don't use MaskMap
#ifdef _EMISSIVE_COLOR
    #ifdef _EMISSIVE_COLOR_MAP
    builtinData.emissiveColor = SAMPLE_TEXTURE2D(_EmissiveColorMap, sampler_EmissiveColorMap, input.texCoord0).rgb * _EmissiveColor;
    #else
    builtinData.emissiveColor = _EmissiveColor;
    #endif
#elif defined(_MASKMAP) // If we have a MaskMap, use emissive slot as a mask on baseColor
    builtinData.emissiveColor = surfaceData.baseColor * SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.texCoord0).bbb;
#else
    builtinData.emissiveColor = float3(0.0, 0.0, 0.0);
#endif

    builtinData.emissiveIntensity = _EmissiveIntensity;

    builtinData.velocity = float2(0.0, 0.0);

    builtinData.distortion = float2(0.0, 0.0);
    builtinData.distortionBlur = 0.0;
}