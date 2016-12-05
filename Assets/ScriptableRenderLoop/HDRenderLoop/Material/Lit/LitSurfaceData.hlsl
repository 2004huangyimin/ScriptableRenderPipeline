void ADD_IDX(ComputeLayerTexCoord)(FragInput input, bool isTriplanar, inout LayerTexCoord layerTexCoord)
{
    // TODO: Do we want to manage local or world triplanar/planar
    //float3 position = localTriplanar ? TransformWorldToObject(input.positionWS) : input.positionWS;
    float3 position = input.positionWS;
    position *= ADD_IDX(_TexWorldScale);

    // Handle uv0, uv1 and plnar XZ coordinate based on _CoordWeight weight (exclusif 0..1)
    ADD_IDX(layerTexCoord.base).uv =    ADD_IDX(_UVMappingMask).x * input.texCoord0 +
                                        ADD_IDX(_UVMappingMask).y * input.texCoord1 + 
                                        ADD_IDX(_UVMappingMask).z * input.texCoord3 +
                                        ADD_IDX(_UVMappingMask).w * position.xz;

    float2 uvDetails =  ADD_IDX(_UVDetailsMappingMask).x * input.texCoord0 +
                        ADD_IDX(_UVDetailsMappingMask).y * input.texCoord1 +
                        ADD_IDX(_UVDetailsMappingMask).z * input.texCoord3 +
                        // Note that if base is planar, detail map is planar
                        ADD_IDX(_UVMappingMask).w * position.xz;

    ADD_IDX(layerTexCoord.details).uv = TRANSFORM_TEX(uvDetails, ADD_IDX(_DetailMap));

    // triplanar
    ADD_IDX(layerTexCoord.base).isTriplanar = isTriplanar;

    ADD_IDX(layerTexCoord.base).uvYZ = position.zy;
    ADD_IDX(layerTexCoord.base).uvZX = position.xz;
    ADD_IDX(layerTexCoord.base).uvXY = float2(-position.x, position.y);

    ADD_IDX(layerTexCoord.details).isTriplanar = isTriplanar;

    ADD_IDX(layerTexCoord.details).uvYZ = TRANSFORM_TEX(position.zy, ADD_IDX(_DetailMap));
    ADD_IDX(layerTexCoord.details).uvZX = TRANSFORM_TEX(position.xz, ADD_IDX(_DetailMap));
    ADD_IDX(layerTexCoord.details).uvXY = TRANSFORM_TEX(float2(-position.x, position.y), ADD_IDX(_DetailMap));
}

void ADD_IDX(ApplyDisplacement)(inout FragInput input, inout LayerTexCoord layerTexCoord)
{
#ifdef _HEIGHTMAP
   #ifndef _HEIGHTMAP_AS_DISPLACEMENT
    // Transform view vector in tangent space
    // Hope the compiler can optimize this in case of multiple layer
    float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float3 viewDirTS = TransformWorldToTangent(V, input.tangentToWorld);
    float height = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_HeightMap), ADD_ZERO_IDX(sampler_HeightMap), ADD_IDX(layerTexCoord.base)).r * ADD_IDX(_HeightScale) + ADD_IDX(_HeightBias);
    float2 offset = ParallaxOffset(viewDirTS, height);

    ADD_IDX(layerTexCoord.base).uv += offset;
    ADD_IDX(layerTexCoord.base).uvYZ += offset;
    ADD_IDX(layerTexCoord.base).uvZX += offset;
    ADD_IDX(layerTexCoord.base).uvXY += offset;

    ADD_IDX(layerTexCoord.details).uv += offset;
    ADD_IDX(layerTexCoord.details).uvYZ += offset;
    ADD_IDX(layerTexCoord.details).uvZX += offset;
    ADD_IDX(layerTexCoord.details).uvXY += offset;

    // Only modify tex coord for first layer, this will be use by for builtin data (like lightmap)
    if (LAYER_INDEX == 0)
    {
        input.texCoord0 += offset;
        input.texCoord1 += offset;
        input.texCoord2 += offset;
        input.texCoord3 += offset;
    }
    #endif
#endif
}

// Return opacity
float ADD_IDX(GetSurfaceData)(FragInput input, LayerTexCoord layerTexCoord, out SurfaceData surfaceData)
{
#ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
    float alpha = ADD_IDX(_BaseColor).a;
#else
    float alpha = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_BaseColorMap), ADD_ZERO_IDX(sampler_BaseColorMap), ADD_IDX(layerTexCoord.base)).a * ADD_IDX(_BaseColor).a;
#endif

    // Perform alha test very early to save performance (a killed pixel will not sample textures)
#ifdef _ALPHATEST_ON
    clip(alpha - _AlphaCutoff);
#endif

#ifdef _DETAIL_MAP
    float detailMask = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_DetailMask), ADD_ZERO_IDX(sampler_DetailMask), ADD_IDX(layerTexCoord.base)).b;
    float2 detailAlbedoAndSmoothness = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_DetailMap), ADD_ZERO_IDX(sampler_DetailMap), ADD_IDX(layerTexCoord.details)).rb;
    float detailAlbedo = detailAlbedoAndSmoothness.r;
    float detailSmoothness = detailAlbedoAndSmoothness.g;
    #ifdef _DETAIL_MAP_WITH_NORMAL
    // Resample the detail map but this time for the normal map. This call should be optimize by the compiler
    // We split both call due to trilinear mapping
    float3 detailNormalTS = SAMPLE_LAYER_NORMALMAP_AG(ADD_IDX(_DetailMap), ADD_ZERO_IDX(sampler_DetailMap), ADD_IDX(layerTexCoord.details));
    //float detailAO = 0.0;
    #else
    // TODO: Use heightmap as a derivative with Morten Mikklesen approach, how this work with our abstraction and triplanar ?
    float3 detailNormalTS = float3(0.0, 0.0, 1.0);
    //float detailAO = detail.b;
    #endif
#endif

    surfaceData.baseColor = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_BaseColorMap), ADD_ZERO_IDX(sampler_BaseColorMap), ADD_IDX(layerTexCoord.base)).rgb * ADD_IDX(_BaseColor).rgb;
#ifdef _DETAIL_MAP
    surfaceData.baseColor *= LerpWhiteTo(2.0 * saturate(detailAlbedo * ADD_IDX(_DetailAlbedoScale)), detailMask);
#endif

#ifdef _SPECULAROCCLUSIONMAP
    // TODO: Do something. For now just take alpha channel
    surfaceData.specularOcclusion = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_SpecularOcclusionMap), ADD_ZERO_IDX(sampler_SpecularOcclusionMap), ADD_IDX(layerTexCoord.base)).a;
#else
    // Horizon Occlusion for Normal Mapped Reflections: http://marmosetco.tumblr.com/post/81245981087
    //surfaceData.specularOcclusion = saturate(1.0 + horizonFade * dot(r, input.tangentToWorld[2].xyz);
    // smooth it
    //surfaceData.specularOcclusion *= surfaceData.specularOcclusion;
    surfaceData.specularOcclusion = 1.0;
#endif

    // TODO: think about using BC5
    float3 vertexNormalWS = normalize(input.tangentToWorld[2].xyz);

#ifdef _NORMALMAP
    #ifdef _NORMALMAP_TANGENT_SPACE
        float3 normalTS = SAMPLE_LAYER_NORMALMAP(ADD_IDX(_NormalMap), ADD_ZERO_IDX(sampler_NormalMap), ADD_IDX(layerTexCoord.base));
        #ifdef _DETAIL_MAP
        normalTS = lerp(normalTS, BlendNormal(normalTS, detailNormalTS), detailMask);
        #endif
        surfaceData.normalWS = TransformTangentToWorld(normalTS, input.tangentToWorld);
    #else // Object space
        // TODO: We are suppose to do * 2 - 1 here. how to deal with triplanar...
        float3 normalOS = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_NormalMap), ADD_ZERO_IDX(sampler_NormalMap), ADD_IDX(layerTexCoord.base)).rgb;
        surfaceData.normalWS = TransformObjectToWorldDir(normalOS);
        #ifdef _DETAIL_MAP
        float3 detailNormalWS = TransformTangentToWorld(detailNormalTS, input.tangentToWorld);
        surfaceData.normalWS = lerp(surfaceData.normalWS, BlendNormal(surfaceData.normalWS, detailNormalWS), detailMask);
        #endif
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
    surfaceData.perceptualSmoothness = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_BaseColorMap), ADD_ZERO_IDX(sampler_BaseColorMap), ADD_IDX(layerTexCoord.base)).a;
#elif defined(_MASKMAP)
    surfaceData.perceptualSmoothness = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_MaskMap), ADD_ZERO_IDX(sampler_MaskMap), ADD_IDX(layerTexCoord.base)).a;
#else
    surfaceData.perceptualSmoothness = 1.0;
#endif
	surfaceData.perceptualSmoothness = ADD_IDX(_Smoothness);
#ifdef _DETAIL_MAP
    surfaceData.perceptualSmoothness *= LerpWhiteTo(2.0 * saturate(detailSmoothness * ADD_IDX(_DetailSmoothnessScale)), detailMask);
#endif

    // MaskMap is Metallic, Ambient Occlusion, (Optional) - emissive Mask, Optional - Smoothness (in alpha)
#ifdef _MASKMAP
    surfaceData.metallic = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_MaskMap), ADD_ZERO_IDX(sampler_MaskMap), ADD_IDX(layerTexCoord.base)).r;
    surfaceData.ambientOcclusion = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_MaskMap), ADD_ZERO_IDX(sampler_MaskMap), ADD_IDX(layerTexCoord.base)).g;
#else
    surfaceData.metallic = 1.0;
    surfaceData.ambientOcclusion = 1.0;
#endif
    surfaceData.metallic *= ADD_IDX(_Metallic);

    // This part of the code is not used in case of layered shader but we keep the same macro system for simplicity
#if !defined(LAYERED_LIT_SHADER)

    surfaceData.materialId = 0; // TODO

    // TODO: think about using BC5
#ifdef _TANGENTMAP
#ifdef _NORMALMAP_TANGENT_SPACE // Normal and tangent use same space
    float3 tangentTS = SAMPLE_LAYER_NORMALMAP(ADD_IDX(_TangentMap), ADD_ZERO_IDX(sampler_TangentMap), ADD_IDX(layerTexCoord.base));
    surfaceData.tangentWS = TransformTangentToWorld(tangentTS, input.tangentToWorld);
#else // Object space (TODO: We need to apply the world rotation here! - Require to pass world transform)
    surfaceData.tangentWS = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_TangentMap), ADD_ZERO_IDX(sampler_TangentMap), ADD_IDX(layerTexCoord.base)).rgb;
#endif
#else
    surfaceData.tangentWS = normalize(input.tangentToWorld[0].xyz);
#endif
    // TODO: Is there anything todo regarding flip normal but for the tangent ?

#ifdef _ANISOTROPYMAP
    surfaceData.anisotropy = SAMPLE_LAYER_TEXTURE2D(ADD_IDX(_AnisotropyMap), ADD_ZERO_IDX(sampler_AnisotropyMap), ADD_IDX(layerTexCoord.base)).g;
#else
    surfaceData.anisotropy = 1.0;
#endif
    surfaceData.anisotropy *= ADD_IDX(_Anisotropy);

    surfaceData.specular = 0.04;

    surfaceData.subSurfaceRadius = 1.0;
    surfaceData.thickness = 0.0;
    surfaceData.subSurfaceProfile = 0;

    surfaceData.coatNormalWS = float3(1.0, 0.0, 0.0);
    surfaceData.coatPerceptualSmoothness = 1.0;
    surfaceData.specularColor = float3(0.0, 0.0, 0.0);

#else // #if !defined(LAYERED_LIT_SHADER)

    // Mandatory to setup value to keep compiler quiet

    // Layered shader only support materialId 0
    surfaceData.materialId = 0;

    surfaceData.tangentWS = input.tangentToWorld[0].xyz;
    surfaceData.anisotropy = 0;
    surfaceData.specular = 0.04;

    surfaceData.subSurfaceRadius = 1.0;
    surfaceData.thickness = 0.0;
    surfaceData.subSurfaceProfile = 0;

    surfaceData.coatNormalWS = float3(1.0, 0.0, 0.0);
    surfaceData.coatPerceptualSmoothness = 1.0;
    surfaceData.specularColor = float3(0.0, 0.0, 0.0);

#endif // #if !defined(LAYERED_LIT_SHADER)

    return alpha;
}

