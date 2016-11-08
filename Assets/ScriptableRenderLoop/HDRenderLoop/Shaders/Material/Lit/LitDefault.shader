Shader "HDRenderLoop/Lit"
{
    Properties
    {
        // Following set of parameters represent the parameters node inside the MaterialGraph.
        // They are use to fill a SurfaceData. With a MaterialGraph this should not exist.

        // Reminder. Color here are in linear but the UI (color picker) do the conversion sRGB to linear
        _BaseColor("BaseColor", Color) = (1,1,1,1)
        _BaseColorMap("BaseColorMap", 2D) = "white" {}

        _Metallic("_Metallic", Range(0.0, 1.0)) = 0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _MaskMap("MaskMap", 2D) = "white" {}

        _SpecularOcclusionMap("SpecularOcclusion", 2D) = "white" {}

        _NormalMap("NormalMap", 2D) = "bump" {}
        [Enum(TangentSpace, 0, ObjectSpace, 1)] _NormalMapSpace("NormalMap space", Float) = 0

        _HeightMap("HeightMap", 2D) = "black" {}
        _HeightScale("Height Scale", Float) = 1
        _HeightBias("Height Bias", Float) = 0
        [Enum(Parallax, 0, Displacement, 1)] _HeightMapMode("Heightmap usage", Float) = 0

        _TangentMap("TangentMap", 2D) = "bump" {}
        _Anisotropy("Anisotropy", Range(0.0, 1.0)) = 0
        _AnisotropyMap("AnisotropyMap", 2D) = "white" {}

        _SubSurfaceRadius("SubSurfaceRadius", Range(0.0, 1.0)) = 0
        _SubSurfaceRadiusMap("SubSurfaceRadiusMap", 2D) = "white" {}
        //_Thickness("Thickness", Range(0.0, 1.0)) = 0
        //_ThicknessMap("ThicknessMap", 2D) = "white" {}
        //_SubSurfaceProfile("SubSurfaceProfile", Float) = 0

        //_CoatCoverage("CoatCoverage", Range(0.0, 1.0)) = 0
        //_CoatCoverageMap("CoatCoverageMapMap", 2D) = "white" {}

        //_CoatRoughness("CoatRoughness", Range(0.0, 1.0)) = 0
        //_CoatRoughnessMap("CoatRoughnessMap", 2D) = "white" {}

        // _DistortionVectorMap("DistortionVectorMap", 2D) = "white" {}
        // _DistortionBlur("DistortionBlur", Range(0.0, 1.0)) = 0

        // Following options are for the GUI inspector and different from the input parameters above
        // These option below will cause different compilation flag.

        _EmissiveColor("EmissiveColor", Color) = (0, 0, 0)
        _EmissiveColorMap("EmissiveColorMap", 2D) = "white" {}
        _EmissiveIntensity("EmissiveIntensity", Float) = 0

        [ToggleOff]     _DistortionOnly("Distortion Only", Float) = 0.0
        [ToggleOff]     _DistortionDepthTest("Distortion Only", Float) = 0.0

        [ToggleOff]  _AlphaCutoffEnable("Alpha Cutoff Enable", Float) = 0.0
        _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        // Blending state
        [HideInInspector] _SurfaceType("__surfacetype", Float) = 0.0
        [HideInInspector] _BlendMode ("__blendmode", Float) = 0.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        // Material Id
        [HideInInspector] _MaterialId("_MaterialId", FLoat) = 0

        [Enum(Mask Alpha, 0, BaseColor Alpha, 1)] _SmoothnessTextureChannel("Smoothness texture channel", Float) = 1
        [Enum(Use Emissive Color, 0, Use Emissive Mask, 1)] _EmissiveColorMode("Emissive color mode", Float) = 1
        [Enum(None, 0, DoubleSided, 1, DoubleSidedLigthingFlip, 2, DoubleSidedLigthingMirror, 3)] _DoubleSidedMode("Double sided mode", Float) = 0
    }

    HLSLINCLUDE

    #pragma target 5.0
    #pragma only_renderers d3d11 // TEMP: unitl we go futher in dev

    //-------------------------------------------------------------------------------------
    // Variant
    //-------------------------------------------------------------------------------------

    #pragma shader_feature _ALPHATEST_ON
    #pragma shader_feature _ _DOUBLESIDED_LIGHTING_FLIP _DOUBLESIDED_LIGHTING_MIRROR
    #pragma shader_feature _NORMALMAP
    #pragma shader_feature _NORMALMAP_TANGENT_SPACE
    #pragma shader_feature _MASKMAP
    #pragma shader_feature _SPECULAROCCLUSIONMAP
    #pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
    #pragma shader_feature _EMISSIVE_COLOR
    #pragma shader_feature _EMISSIVE_COLOR_MAP
    #pragma shader_feature _HEIGHTMAP
    #pragma shader_feature _HEIGHTMAP_AS_DISPLACEMENT
    #pragma shader_feature _TANGENTMAP
    #pragma shader_feature _ANISOTROPYMAP

    #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
    #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED
    #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON

    //-------------------------------------------------------------------------------------
    // Define
    //-------------------------------------------------------------------------------------

    #define UNITY_MATERIAL_LIT // Need to be define before including Material.hlsl

    //-------------------------------------------------------------------------------------
    // Include
    //-------------------------------------------------------------------------------------
    
    #include "common.hlsl"
    #include "Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/ShaderConfig.cs"
    #include "Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/ShaderVariables.hlsl"
    #include "Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/ShaderPass/ShaderPass.cs.hlsl"    
    #include "Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/Debug/DebugViewMaterial.hlsl"

    //-------------------------------------------------------------------------------------
    // variable declaration
    //-------------------------------------------------------------------------------------

    // Set of users variables
    float4 _BaseColor;
    TEXTURE2D(_BaseColorMap);
    SAMPLER2D(sampler_BaseColorMap);

    float _Metallic;
    float _Smoothness;
    TEXTURE2D(_MaskMap);
    SAMPLER2D(sampler_MaskMap);
    TEXTURE2D(_SpecularOcclusionMap);
    SAMPLER2D(sampler_SpecularOcclusionMap);

    TEXTURE2D(_NormalMap);
    SAMPLER2D(sampler_NormalMap);
    TEXTURE2D(_Heightmap);
    SAMPLER2D(sampler_Heightmap);

    float _HeightScale;
    float _HeightBias;

    TEXTURE2D(_TangentMap);
    SAMPLER2D(sampler_TangentMap);

    float _Anisotropy;
    TEXTURE2D(_AnisotropyMap);
    SAMPLER2D(sampler_AnisotropyMap);

    TEXTURE2D(_DiffuseLightingMap);
    SAMPLER2D(sampler_DiffuseLightingMap);

    float4 _EmissiveColor;
    TEXTURE2D(_EmissiveColorMap);
    SAMPLER2D(sampler_EmissiveColorMap);

    float _EmissiveIntensity;

    float _SubSurfaceRadius;
    TEXTURE2D(_SubSurfaceRadiusMap);
    SAMPLER2D(sampler_SubSurfaceRadiusMap);

    // float _Thickness;
    //TEXTURE2D(_ThicknessMap);
    //SAMPLER2D(sampler_ThicknessMap);

    // float _CoatCoverage;
    //TEXTURE2D(_CoatCoverageMap);
    //SAMPLER2D(sampler_CoatCoverageMap);

    // float _CoatRoughness;
    //TEXTURE2D(_CoatRoughnessMap);
    //SAMPLER2D(sampler_CoatRoughnessMap);
 
    float _AlphaCutoff;

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 300

        // ------------------------------------------------------------------
        //  Deferred pass
        // ------------------------------------------------------------------
        Pass
        {
            Name "GBuffer"  // Name is not used
            Tags { "LightMode" = "GBuffer" } // This will be only for opaque object based on the RenderQueue index

            Cull  [_CullMode]

            HLSLPROGRAM

            #pragma vertex VertDefault
            #pragma fragment Frag

            #define SHADERPASS SHADERPASS_GBUFFER
            #include "../../Material/Material.hlsl"
            #include "LitData.hlsl"
            #include "LitSharePass.hlsl"

            #include "../../ShaderPass/ShaderPassGBuffer.hlsl"

            ENDHLSL
        }

        // ------------------------------------------------------------------
        //  Debug pass
        // ------------------------------------------------------------------
        Pass
        {
            Name "Debug"
            Tags { "LightMode" = "DebugViewMaterial" }

            Cull[_CullMode]

            HLSLPROGRAM

            #pragma vertex VertDefault
            #pragma fragment Frag

            #define SHADERPASS SHADERPASS_DEBUG_VIEW_MATERIAL
            #include "../../Material/Material.hlsl"
            #include "LitData.hlsl"
            #include "LitSharePass.hlsl"
            #include "LitDebugPass.hlsl"
            
            #include "../../ShaderPass/ShaderPassDebugViewMaterial.hlsl"

            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Extracts information for lightmapping, GI (emission, albedo, ...)
        // This pass it not used during regular rendering.
        // ------------------------------------------------------------------
        Pass
        {
            Name "META"
            Tags{ "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM

            // Lightmap memo
            // DYNAMICLIGHTMAP_ON is used when we have an "enlighten lightmap" ie a lightmap updated at runtime by enlighten.This lightmap contain indirect lighting from realtime lights and realtime emissive material.Offline baked lighting(from baked material / light, 
            // both direct and indirect lighting) will hand up in the "regular" lightmap->LIGHTMAP_ON.

            #pragma vertex Vert
            #pragma fragment Frag

            #define SHADERPASS SHADERPASS_LIGHT_TRANSPORT
            #include "../../Material/Material.hlsl"
            #include "LitData.hlsl"
            #include "LitMetaPass.hlsl"

            #include "../../ShaderPass/ShaderPassLightTransport.hlsl"

            ENDHLSL
        }

        // ------------------------------------------------------------------
        //  Depth only
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }

            Cull[_CullMode]

            ZWrite On ZTest LEqual

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "../../Material/Material.hlsl"            
            #include "LitData.hlsl"
            #include "LitDepthPass.hlsl"

            #include "../../ShaderPass/ShaderPassDepthOnly.hlsl"

            ENDHLSL
        }

        // ------------------------------------------------------------------
        //  forward pass
        // ------------------------------------------------------------------
        Pass
        {
            Name "Forward" // Name is not used
            Tags { "LightMode" = "Forward" } // This will be only for transparent object based on the RenderQueue index

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_CullMode]

            HLSLPROGRAM

            #pragma vertex VertDefault
            #pragma fragment Frag

            #define SHADERPASS SHADERPASS_FORWARD
            // TEMP until pragma work in include
            // #include "../../Lighting/Forward.hlsl"
            #pragma multi_compile LIGHTLOOP_SINGLE_PASS
            //#pragma multi_compile SHADOWFILTERING_FIXED_SIZE_PCF

            #include "../../Lighting/Lighting.hlsl"
            #include "LitData.hlsl"
            #include "LitSharePass.hlsl"

            #include "../../ShaderPass/ShaderPassForward.hlsl"

            ENDHLSL
        }
    }

    CustomEditor "LitGUI"
}
