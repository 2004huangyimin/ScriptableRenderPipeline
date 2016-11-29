using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Experimental.ScriptableRenderLoop
{
    class LitGUI : BaseLitGUI
    {
        public enum SmoothnessMapChannel
        {
            MaskAlpha,
            AlbedoAlpha,
        }

        public enum UVBaseMapping
        {
            UV0,
            Planar, 
            Triplanar
        }

        public enum NormalMapSpace
        {
            TangentSpace,
            ObjectSpace,
        }

        public enum HeightmapMode
        {
            Parallax,
            Displacement,
        }

        public enum DetailMapMode
        {
            DetailWithNormal,
            DetailWithAOHeight,
        }

        public enum UVDetailMapping
        {
            UV0,
            UV1,
            UV3
        }

        public enum EmissiveColorMode
        {
            UseEmissiveColor,
            UseEmissiveMask,
        }

        protected MaterialProperty smoothnessMapChannel = null;
        protected const string kSmoothnessTextureChannel = "_SmoothnessTextureChannel";
        protected MaterialProperty UVBase = null;
        protected const string kUVBase = "_UVBase";
        protected MaterialProperty TexWorldScale = null;
        protected const string kTexWorldScale = "_TexWorldScale";
        protected MaterialProperty UVMappingMask = null;
        protected const string kUVMappingMask = "_UVMappingMask";
        protected MaterialProperty normalMapSpace = null;
        protected const string kNormalMapSpace = "_NormalMapSpace";
        protected MaterialProperty heightMapMode = null;
        protected const string kHeightMapMode = "_HeightMapMode";
        protected MaterialProperty detailMapMode = null;
        protected const string kDetailMapMode = "_DetailMapMode";
        protected MaterialProperty UVDetail = null;
        protected const string kUVDetail = "_UVDetail";
        protected MaterialProperty UVDetailsMappingMask = null;
        protected const string kUVDetailsMappingMask = "_UVDetailsMappingMask";
        protected MaterialProperty emissiveColorMode = null;
        protected const string kEmissiveColorMode = "_EmissiveColorMode";

        protected MaterialProperty baseColor = null;
        protected const string kBaseColor = "_BaseColor";
        protected MaterialProperty baseColorMap = null;
        protected const string kBaseColorMap = "_BaseColorMap";
        protected MaterialProperty metallic = null;
        protected const string kMetallic = "_Metallic";
        protected MaterialProperty smoothness = null;
        protected const string kSmoothness = "_Smoothness";
        protected MaterialProperty maskMap = null;
        protected const string kMaskMap = "_MaskMap";
        protected MaterialProperty specularOcclusionMap = null;
        protected const string kSpecularOcclusionMap = "_SpecularOcclusionMap";
        protected MaterialProperty normalMap = null;
        protected const string kNormalMap = "_NormalMap";
        protected MaterialProperty heightMap = null;
        protected const string kHeightMap = "_HeightMap";
        protected MaterialProperty heightScale = null;
        protected const string kHeightScale = "_HeightScale";
        protected MaterialProperty heightBias = null;
        protected const string kHeightBias= "_HeightBias";
        protected MaterialProperty tangentMap = null;
        protected const string kTangentMap = "_TangentMap";
        protected MaterialProperty anisotropy = null;
        protected const string kAnisotropy = "_Anisotropy";
        protected MaterialProperty anisotropyMap = null;
        protected const string kAnisotropyMap = "_AnisotropyMap";

        protected MaterialProperty detailMap = null;
        protected const string kDetailMap = "_DetailMap";
        protected MaterialProperty detailMask = null;
        protected const string kDetailMask = "_DetailMask";
        protected MaterialProperty detailAlbedoScale = null;
        protected const string kDetailAlbedoScale = "_DetailAlbedoScale";
        protected MaterialProperty detailNormalScale = null;
        protected const string kDetailNormalScale = "_DetailNormalScale";
        protected MaterialProperty detailSmoothnessScale = null;
        protected const string kDetailSmoothnessScale = "_DetailSmoothnessScale";
        protected MaterialProperty detailHeightScale = null;
        protected const string kDetailHeightScale = "_DetailHeightScale";
        protected MaterialProperty detailAOScale = null;
        protected const string kDetailAOScale = "_DetailAOScale";

        //	MaterialProperty subSurfaceRadius = null;
        //	MaterialProperty subSurfaceRadiusMap = null;

        protected MaterialProperty emissiveColor = null;
        protected const string kEmissiveColor = "_EmissiveColor";
        protected MaterialProperty emissiveColorMap = null;
        protected const string kEmissiveColorMap = "_EmissiveColorMap";
        protected MaterialProperty emissiveIntensity = null;
        protected const string kEmissiveIntensity = "_EmissiveIntensity";

        override protected void FindInputOptionProperties(MaterialProperty[] props)
        {   
            smoothnessMapChannel = FindProperty(kSmoothnessTextureChannel, props);
            UVBase = FindProperty(kUVBase, props);
            TexWorldScale = FindProperty(kTexWorldScale, props);
            UVMappingMask = FindProperty(kUVMappingMask, props);
            normalMapSpace = FindProperty(kNormalMapSpace, props);
            heightMapMode = FindProperty(kHeightMapMode, props);
            detailMapMode = FindProperty(kDetailMapMode, props);
            UVDetail = FindProperty(kUVDetail, props);
            UVDetailsMappingMask = FindProperty(kUVDetailsMappingMask, props);    
            emissiveColorMode = FindProperty(kEmissiveColorMode, props);
        }

        override protected void FindInputProperties(MaterialProperty[] props)
        {
            baseColor = FindProperty(kBaseColor, props);
            baseColorMap = FindProperty(kBaseColorMap, props);
            metallic = FindProperty(kMetallic, props);
            smoothness = FindProperty(kSmoothness, props);
            maskMap = FindProperty(kMaskMap, props);
            specularOcclusionMap = FindProperty(kSpecularOcclusionMap, props);
            normalMap = FindProperty(kNormalMap, props);
            heightMap = FindProperty(kHeightMap, props);
            heightScale = FindProperty(kHeightScale, props);
            heightBias = FindProperty(kHeightBias, props);
            tangentMap = FindProperty(kTangentMap, props);
            anisotropy = FindProperty(kAnisotropy, props);
            anisotropyMap = FindProperty(kAnisotropyMap, props);

            detailMap = FindProperty(kDetailMap, props);
            detailMask = FindProperty(kDetailMask, props);
            detailAlbedoScale = FindProperty(kDetailAlbedoScale, props);
            detailNormalScale = FindProperty(kDetailNormalScale, props);
            detailSmoothnessScale = FindProperty(kDetailSmoothnessScale, props);
            detailHeightScale = FindProperty(kDetailHeightScale, props);
            detailAOScale = FindProperty(kDetailAOScale, props);

            emissiveColor = FindProperty(kEmissiveColor, props);
            emissiveColorMap = FindProperty(kEmissiveColorMap, props);
            emissiveIntensity = FindProperty(kEmissiveIntensity, props);
        }

        override protected void ShaderInputOptionsGUI()
        {
            // When Planar or Triplanar is enable the UVDetail use the same mode, so we disable the choice on UVDetail
            bool enableUVDetail = (UVBaseMapping)UVBase.floatValue == UVBaseMapping.UV0;

            EditorGUI.indentLevel++;
            GUILayout.Label(Styles.InputsOptionsText, EditorStyles.boldLabel);
            m_MaterialEditor.ShaderProperty(smoothnessMapChannel, Styles.smoothnessMapChannelText.text);
            m_MaterialEditor.ShaderProperty(UVBase, enableUVDetail ? Styles.UVBaseDetailMappingText.text : Styles.UVBaseMappingText.text);

            float X, Y, Z, W;
            X = ((UVBaseMapping)UVBase.floatValue == UVBaseMapping.UV0) ? 1.0f : 0.0f;
            W = ((UVBaseMapping)UVBase.floatValue == UVBaseMapping.Planar) ? 1.0f : 0.0f;
            UVMappingMask.colorValue = new Color(X, 0.0f, 0.0f, W);
            if (((UVBaseMapping)UVBase.floatValue == UVBaseMapping.Planar) || ((UVBaseMapping)UVBase.floatValue == UVBaseMapping.Triplanar))
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.ShaderProperty(TexWorldScale, Styles.texWorldScaleText.text);
                EditorGUI.indentLevel--;
            }
            if (enableUVDetail)
            {
                m_MaterialEditor.ShaderProperty(UVDetail, Styles.UVDetailMappingText.text);
            }

            // If base is planar mode, detail is planar too
            if (W > 0.0f)
            {
                X = Y = Z = 0.0f;
            }
            else
            {
                X = ((UVDetailMapping)UVDetail.floatValue == UVDetailMapping.UV0) ? 1.0f : 0.0f;
                Y = ((UVDetailMapping)UVDetail.floatValue == UVDetailMapping.UV1) ? 1.0f : 0.0f;
                Z = ((UVDetailMapping)UVDetail.floatValue == UVDetailMapping.UV3) ? 1.0f : 0.0f;
            }
            UVDetailsMappingMask.colorValue = new Color(X, Y, Z, 0.0f); // W Reuse planar mode from base

            m_MaterialEditor.ShaderProperty(detailMapMode, Styles.detailMapModeText.text);
            m_MaterialEditor.ShaderProperty(normalMapSpace, Styles.normalMapSpaceText.text);
            m_MaterialEditor.ShaderProperty(heightMapMode, Styles.heightMapModeText.text);
            m_MaterialEditor.ShaderProperty(emissiveColorMode, Styles.emissiveColorModeText.text);
            EditorGUI.indentLevel--;
        }

        override protected void ShaderInputGUI()
        {
            EditorGUI.indentLevel++;
            bool smoothnessInAlbedoAlpha = (SmoothnessMapChannel)smoothnessMapChannel.floatValue == SmoothnessMapChannel.AlbedoAlpha;            
            bool useDetailMapWithNormal = (DetailMapMode)detailMapMode.floatValue == DetailMapMode.DetailWithNormal;
            bool useEmissiveMask = (EmissiveColorMode)emissiveColorMode.floatValue == EmissiveColorMode.UseEmissiveMask;

            GUILayout.Label(Styles.InputsText, EditorStyles.boldLabel);
            m_MaterialEditor.TexturePropertySingleLine(smoothnessInAlbedoAlpha ? Styles.baseColorSmoothnessText : Styles.baseColorText, baseColorMap, baseColor);
            m_MaterialEditor.ShaderProperty(metallic, Styles.metallicText);
            m_MaterialEditor.ShaderProperty(smoothness, Styles.smoothnessText);

            if (smoothnessInAlbedoAlpha && useEmissiveMask)
                m_MaterialEditor.TexturePropertySingleLine(Styles.maskMapEText, maskMap);
            else if (smoothnessInAlbedoAlpha && !useEmissiveMask)
                m_MaterialEditor.TexturePropertySingleLine(Styles.maskMapText, maskMap);
            else if (!smoothnessInAlbedoAlpha && useEmissiveMask)
                m_MaterialEditor.TexturePropertySingleLine(Styles.maskMapESText, maskMap);
            else if (!smoothnessInAlbedoAlpha && !useEmissiveMask)
                m_MaterialEditor.TexturePropertySingleLine(Styles.maskMapSText, maskMap);

            m_MaterialEditor.TexturePropertySingleLine(Styles.specularOcclusionMapText, specularOcclusionMap);

            m_MaterialEditor.TexturePropertySingleLine(Styles.normalMapText, normalMap);

            m_MaterialEditor.TexturePropertySingleLine(Styles.heightMapText, heightMap, heightScale, heightBias);

            m_MaterialEditor.TexturePropertySingleLine(Styles.tangentMapText, tangentMap);

            m_MaterialEditor.ShaderProperty(anisotropy, Styles.anisotropyText);
            
            m_MaterialEditor.TexturePropertySingleLine(Styles.anisotropyMapText, anisotropyMap);

            EditorGUILayout.Space();
            GUILayout.Label(Styles.detailText, EditorStyles.boldLabel);

            m_MaterialEditor.TexturePropertySingleLine(Styles.detailMaskText, detailMask);               

            if (useDetailMapWithNormal)
            {
                m_MaterialEditor.TexturePropertySingleLine(Styles.detailMapNormalText, detailMap);
            }
            else
            {
                m_MaterialEditor.TexturePropertySingleLine(Styles.detailMapAOHeightText, detailMap);
            }
            m_MaterialEditor.TextureScaleOffsetProperty(detailMap);
            EditorGUI.indentLevel++;
            m_MaterialEditor.ShaderProperty(detailAlbedoScale, Styles.detailAlbedoScaleText);
            m_MaterialEditor.ShaderProperty(detailNormalScale, Styles.detailNormalScaleText);
            m_MaterialEditor.ShaderProperty(detailSmoothnessScale, Styles.detailSmoothnessScaleText);
            m_MaterialEditor.ShaderProperty(detailHeightScale, Styles.detailHeightScaleText);
            m_MaterialEditor.ShaderProperty(detailAOScale, Styles.detailAOScaleText);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            GUILayout.Label(Styles.lightingText, EditorStyles.boldLabel);

            if (!useEmissiveMask)
            {
                m_MaterialEditor.TexturePropertySingleLine(Styles.emissiveText, emissiveColorMap, emissiveColor);
            }
            m_MaterialEditor.ShaderProperty(emissiveIntensity, Styles.emissiveIntensityText);
            m_MaterialEditor.LightmapEmissionProperty(MaterialEditor.kMiniTextureFieldLabelIndentLevel + 1);

            EditorGUI.indentLevel--;
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
        }

        protected virtual void SetupKeywordsForInputMaps(Material material)
        {
            SetKeyword(material, "_NORMALMAP", material.GetTexture(kNormalMap) || material.GetTexture(kDetailMap)); // With details map, we always use a normal map and Unity provide a default (0, 0, 1) normal map for ir
            SetKeyword(material, "_MASKMAP", material.GetTexture(kMaskMap));
            SetKeyword(material, "_SPECULAROCCLUSIONMAP", material.GetTexture(kSpecularOcclusionMap));
            SetKeyword(material, "_EMISSIVE_COLOR_MAP", material.GetTexture(kEmissiveColorMap));
            SetKeyword(material, "_HEIGHTMAP", material.GetTexture(kHeightMap));
            SetKeyword(material, "_TANGENTMAP", material.GetTexture(kTangentMap));
            SetKeyword(material, "_ANISOTROPYMAP", material.GetTexture(kAnisotropyMap));
            SetKeyword(material, "_DETAIL_MAP", material.GetTexture(kDetailMap));
        }

        protected override bool ShouldEmissionBeEnabled(Material mat)
        {
            float emissiveIntensity = mat.GetFloat(kEmissiveIntensity);
            var realtimeEmission = (mat.globalIlluminationFlags & MaterialGlobalIlluminationFlags.RealtimeEmissive) > 0;
            return emissiveIntensity > 0.0f || realtimeEmission;
        }

        override protected void SetupInputMaterial(Material material)
        {
            // Note: keywords must be based on Material value not on MaterialProperty due to multi-edit & material animation
            // (MaterialProperty value might come from renderer material property block)
            SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", ((SmoothnessMapChannel)material.GetFloat(kSmoothnessTextureChannel)) == SmoothnessMapChannel.AlbedoAlpha);
            SetKeyword(material, "_MAPPING_TRIPLANAR", ((UVBaseMapping)material.GetFloat(kUVBase)) == UVBaseMapping.Triplanar);
            SetKeyword(material, "_NORMALMAP_TANGENT_SPACE", ((NormalMapSpace)material.GetFloat(kNormalMapSpace)) == NormalMapSpace.TangentSpace);
            SetKeyword(material, "_HEIGHTMAP_AS_DISPLACEMENT", ((HeightmapMode)material.GetFloat(kHeightMapMode)) == HeightmapMode.Displacement);
            SetKeyword(material, "_DETAIL_MAP_WITH_NORMAL", ((DetailMapMode)material.GetFloat(kDetailMapMode)) == DetailMapMode.DetailWithNormal);
            SetKeyword(material, "_REQUIRE_UV3", ((UVDetailMapping)material.GetFloat(kUVDetail)) == UVDetailMapping.UV3 && (UVBaseMapping)material.GetFloat(kUVBase) == UVBaseMapping.UV0);
            SetKeyword(material, "_EMISSIVE_COLOR", ((EmissiveColorMode)material.GetFloat(kEmissiveColorMode)) == EmissiveColorMode.UseEmissiveColor);
 
            SetupKeywordsForInputMaps(material);
        }
    }
} // namespace UnityEditor
