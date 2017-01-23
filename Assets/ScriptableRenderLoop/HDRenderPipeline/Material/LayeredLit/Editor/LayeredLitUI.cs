using System;
using UnityEngine;
using UnityEngine.Rendering;

using System.Linq;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    internal class LayeredLitGUI : LitGUI
    {
        public enum LayerUVBaseMapping
        {
            UV0,
            UV1,
            UV2,
            UV3,
            Planar,
            Triplanar,
        }

        private class StylesLayer
        {
            public readonly GUIContent[] layerLabels =
            {
                new GUIContent("Base Layer"),
                new GUIContent("Layer 1"),
                new GUIContent("Layer 2"),
                new GUIContent("Layer 3"),
            };


            public readonly GUIStyle[] layerLabelColors = 
            {
                new GUIStyle(EditorStyles.label),
                new GUIStyle(EditorStyles.label),
                new GUIStyle(EditorStyles.label),
                new GUIStyle(EditorStyles.label)
            };

            public readonly GUIContent materialLayerText = new GUIContent("Material");
            public readonly GUIContent syncButtonText = new GUIContent("Re-Synchronize Layers", "Re-synchronize all layers's properties with the referenced Material");
            public readonly GUIContent layersText = new GUIContent("Layers");
            public readonly GUIContent emissiveText = new GUIContent("Emissive");
            public readonly GUIContent layerMapMaskText = new GUIContent("Layer Mask", "Layer mask (multiplied by vertex color if enabled)");
            public readonly GUIContent layerMapVertexColorText = new GUIContent("Use Vertex Color", "If no layer mask is set, vertex color values between 0 and 1.0 are used as final mask.\nIf a layer mask is set, vertex color values are remapped between -1 and 1 and added to the mask (neutral at 0.5 vertex color).");
            public readonly GUIContent vertexColorHeightMultiplierText = new GUIContent("Vertex Height Scale", "Scale applied to the vertex color height.");
            public readonly GUIContent layerCountText = new GUIContent("Layer Count", "Number of layers.");
            public readonly GUIContent layerTexWorldScaleText = new GUIContent("Tiling", "Tiling factor applied to Planar/Trilinear mapping");
            public readonly GUIContent UVBaseText = new GUIContent("Base UV Mapping", "Base UV Mapping mode of the layer.");
            public readonly GUIContent UVDetailText = new GUIContent("Detail UV Mapping", "Detail UV Mapping mode of the layer.");
            public readonly GUIContent useHeightBasedBlendText = new GUIContent("Use Height Based Blend", "Layer will be blended with the underlying layer based on the height.");
            public readonly GUIContent heightOffsetText = new GUIContent("Height Offset", "Height offset from the previous layer.");
            public readonly GUIContent inheritBaseLayerText = new GUIContent("Inherit Base Layer Normal", "Inherit the normal from the base layer.");
            public readonly GUIContent heightFactorText = new GUIContent("Height Multiplier", "Scale applied to the height of the layer.");
            public readonly GUIContent blendSizeText = new GUIContent("Blend Size", "Thickness over which the layer will be blended with the previous one.");

            public readonly GUIContent heightCenterOffsetText = new GUIContent("Height Center Offset", "Offset applied to the center of the height of the layer.");
            public readonly GUIContent useHeightBasedBlendV2Text = new GUIContent("Use Height Based Blend V2", "Layer will be blended with the underlying layer based on the height.");
            public readonly GUIContent blendUsingHeight = new GUIContent("Blend Using Height", "Blend Layers using height.");
            public readonly GUIContent inheritBaseNormalText = new GUIContent("Inherit Base Layer Normal", "Inherit the normal from the base layer.");
            public readonly GUIContent inheritBaseHeightText = new GUIContent("Inherit Base Layer Height", "Inherit the height from the base layer.");
            public readonly GUIContent inheritBaseColorText = new GUIContent("Inherit Base Layer Color", "Inherit the base color from the base layer.");
            public readonly GUIContent inheritBaseColorThresholdText = new GUIContent("Threshold", "Inherit the base color from the base layer.");
            public readonly GUIContent minimumOpacityText = new GUIContent("Minimum Opacity", "Minimum Opacity.");
            public readonly GUIContent opacityAsDensityText = new GUIContent("Use Opacity as Density", "Use Opacity as Density.");
            public StylesLayer()
            {
                layerLabelColors[1].normal.textColor = Color.red;
                layerLabelColors[2].normal.textColor = Color.green;
                layerLabelColors[3].normal.textColor = Color.blue;
            }

        }

        static StylesLayer s_Styles = null;
        private static StylesLayer styles { get { if (s_Styles == null) s_Styles = new StylesLayer(); return s_Styles; } }

        // Needed for json serialization to work
        [Serializable]
        internal struct SerializeableGUIDs
        {
            public string[] GUIDArray;
        }

        const int kMaxLayerCount = 4;
        const int kSyncButtonWidth = 58;

        Material[] m_MaterialLayers = new Material[kMaxLayerCount];

        MaterialProperty layerMaskMap = null;
        const string kLayerMaskMap = "_LayerMaskMap";
        MaterialProperty layerMaskVertexColor = null;
        const string kLayerMaskVertexColor = "_LayerMaskVertexColor";
        MaterialProperty layerCount = null;
        const string kLayerCount = "_LayerCount";
        MaterialProperty[] layerTexWorldScale = new MaterialProperty[kMaxLayerCount];
        MaterialProperty[] layerUVBase = new MaterialProperty[kMaxLayerCount];
        MaterialProperty[] layerUVMappingMask = new MaterialProperty[kMaxLayerCount];
        MaterialProperty[] layerUVMappingPlanar = new MaterialProperty[kMaxLayerCount];
        MaterialProperty[] layerUVDetail = new MaterialProperty[kMaxLayerCount];
        MaterialProperty[] layerUVDetailsMappingMask = new MaterialProperty[kMaxLayerCount];

        const string kUseHeightBasedBlend = "_UseHeightBasedBlend";
        MaterialProperty useHeightBasedBlend = null;
        const string kUseHeightBasedBlendV2 = "_UseHeightBasedBlendV2";
        MaterialProperty useHeightBasedBlendV2 = null;
        const string kBlendUsingHeight = "_BlendUsingHeight";
        MaterialProperty[] blendUsingHeight = new MaterialProperty[kMaxLayerCount - 1];
        const string kHeightOffset = "_HeightOffset";
        MaterialProperty[] heightOffset = new MaterialProperty[kMaxLayerCount-1];
        const string kInheritBaseLayer = "_InheritBaseLayer";
        MaterialProperty[] inheritBaseLayer = new MaterialProperty[kMaxLayerCount - 1];
        const string kHeightFactor = "_HeightFactor";
        MaterialProperty[] heightFactor = new MaterialProperty[kMaxLayerCount-1];
        const string kHeightCenterOffset = "_HeightCenterOffset";
        MaterialProperty[] heightCenterOffset = new MaterialProperty[kMaxLayerCount - 1];
        const string kBlendSize = "_BlendSize";
        MaterialProperty[] blendSize = new MaterialProperty[kMaxLayerCount-1];
        const string kVertexColorHeightFactor = "_VertexColorHeightFactor";
        MaterialProperty vertexColorHeightFactor = null;

        // Height blend V2
        const string kInheritBaseNormal = "_InheritBaseNormal";
        MaterialProperty[] inheritBaseNormal = new MaterialProperty[kMaxLayerCount - 1];
        const string kInheritBaseHeight = "_InheritBaseHeight";
        MaterialProperty[] inheritBaseHeight = new MaterialProperty[kMaxLayerCount - 1];
        const string kInheritBaseColor = "_InheritBaseColor";
        MaterialProperty[] inheritBaseColor = new MaterialProperty[kMaxLayerCount - 1];
        const string kInheritBaseColorThreshold = "_InheritBaseColorThreshold";
        MaterialProperty[] inheritBaseColorThreshold = new MaterialProperty[kMaxLayerCount - 1];
        const string kOpacityAsDensity = "_OpacityAsDensity";
        MaterialProperty[] opacityAsDensity = new MaterialProperty[kMaxLayerCount - 1];
        const string kMinimumOpacity = "_MinimumOpacity";
        MaterialProperty[] minimumOpacity = new MaterialProperty[kMaxLayerCount - 1];

        MaterialProperty layerEmissiveColor = null;
        MaterialProperty layerEmissiveColorMap = null;
        MaterialProperty layerEmissiveIntensity = null;

        override protected void FindMaterialProperties(MaterialProperty[] props)
        {
            FindMaterialOptionProperties(props);

            layerMaskMap = FindProperty(kLayerMaskMap, props);
            layerMaskVertexColor = FindProperty(kLayerMaskVertexColor, props);
            vertexColorHeightFactor = FindProperty(kVertexColorHeightFactor, props);
            layerCount = FindProperty(kLayerCount, props);
            useHeightBasedBlend = FindProperty(kUseHeightBasedBlend, props);
            useHeightBasedBlendV2 = FindProperty(kUseHeightBasedBlendV2, props);
            
            for (int i = 0; i < kMaxLayerCount; ++i)
            {
                layerTexWorldScale[i] = FindProperty(string.Format("{0}{1}", kTexWorldScale, i), props);
                layerUVBase[i] = FindProperty(string.Format("{0}{1}", kUVBase, i), props);
                layerUVMappingMask[i] = FindProperty(string.Format("{0}{1}", kUVMappingMask, i), props);
                layerUVMappingPlanar[i] = FindProperty(string.Format("{0}{1}", kUVMappingPlanar, i), props);
                layerUVDetail[i] = FindProperty(string.Format("{0}{1}", kUVDetail, i), props);
                layerUVDetailsMappingMask[i] = FindProperty(string.Format("{0}{1}", kUVDetailsMappingMask, i), props);

                if(i != 0)
                {
                    inheritBaseLayer[i - 1] = FindProperty(string.Format("{0}{1}", kInheritBaseLayer, i), props);
                    heightOffset[i-1] = FindProperty(string.Format("{0}{1}", kHeightOffset, i), props);
                    heightFactor[i-1] = FindProperty(string.Format("{0}{1}", kHeightFactor, i), props);
                    blendSize[i-1] = FindProperty(string.Format("{0}{1}", kBlendSize, i), props);

                    heightCenterOffset[i - 1] = FindProperty(string.Format("{0}{1}", kHeightCenterOffset, i), props);
                    blendUsingHeight[i - 1] = FindProperty(string.Format("{0}{1}", kBlendUsingHeight, i), props);
                    inheritBaseNormal[i - 1] = FindProperty(string.Format("{0}{1}", kInheritBaseNormal, i), props);
                    inheritBaseHeight[i - 1] = FindProperty(string.Format("{0}{1}", kInheritBaseHeight, i), props);
                    inheritBaseColor[i - 1] = FindProperty(string.Format("{0}{1}", kInheritBaseColor, i), props);
                    inheritBaseColorThreshold[i - 1] = FindProperty(string.Format("{0}{1}", kInheritBaseColorThreshold, i), props);
                    minimumOpacity[i - 1] = FindProperty(string.Format("{0}{1}", kMinimumOpacity, i), props);
                    opacityAsDensity[i - 1] = FindProperty(string.Format("{0}{1}", kOpacityAsDensity, i), props);
                }
            }

            layerEmissiveColor = FindProperty(kEmissiveColor, props);
            layerEmissiveColorMap = FindProperty(kEmissiveColorMap, props);
            layerEmissiveIntensity = FindProperty(kEmissiveIntensity, props);
        }

        int numLayer
        {
            set { layerCount.floatValue = (float)value; }
            get { return (int)layerCount.floatValue; }
        }

        public static void SynchronizeAllLayers(Material material)
        {
            int layerCount = (int)material.GetFloat("_LayerCount");
            AssetImporter materialImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetInstanceID()));

            Material[] layers = null;
            InitializeMaterialLayers(materialImporter, ref layers);
            for (int i = 0 ; i < layerCount ; ++i)
            {
                SynchronizeLayerProperties(material, layers, i);
            }
        }

        void SynchronizeAllLayersProperties()
        {
            for (int i = 0; i < numLayer; ++i)
            {
                SynchronizeLayerProperties(m_MaterialEditor.target as Material, m_MaterialLayers, i);
            }
        }

        static void SynchronizeLayerProperties(Material material, Material[] layers, int layerIndex)
        {
            string[] exclusionList = { kTexWorldScale, kUVBase, kUVMappingMask, kUVDetail, kUVMappingPlanar, kUVDetailsMappingMask };

            Material layerMaterial = layers[layerIndex];

            if (layerMaterial != null)
            {
                Shader layerShader = layerMaterial.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(layerShader);
                for (int i = 0; i < propertyCount; ++i)
                {
                    string propertyName = ShaderUtil.GetPropertyName(layerShader, i);
                    string layerPropertyName = propertyName + layerIndex;

                    if(!exclusionList.Contains(propertyName))
                    {
                        if (material.HasProperty(layerPropertyName))
                        {
                            ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(layerShader, i);
                            switch (type)
                            {
                                case ShaderUtil.ShaderPropertyType.Color:
                                {
                                    material.SetColor(layerPropertyName, layerMaterial.GetColor(propertyName));
                                    break;
                                }
                                case ShaderUtil.ShaderPropertyType.Float:
                                case ShaderUtil.ShaderPropertyType.Range:
                                {
                                    material.SetFloat(layerPropertyName, layerMaterial.GetFloat(propertyName));
                                    break;
                                }
                                case ShaderUtil.ShaderPropertyType.Vector:
                                {
                                    material.SetVector(layerPropertyName, layerMaterial.GetVector(propertyName));
                                    break;
                                }
                                case ShaderUtil.ShaderPropertyType.TexEnv:
                                {
                                    material.SetTexture(layerPropertyName, layerMaterial.GetTexture(propertyName));
                                    material.SetTextureOffset(layerPropertyName, layerMaterial.GetTextureOffset(propertyName));
                                    material.SetTextureScale(layerPropertyName, layerMaterial.GetTextureScale(propertyName));
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        static void InitializeMaterialLayers(AssetImporter materialImporter, ref Material[] layers)
        {
            if (materialImporter.userData != string.Empty)
            {
                SerializeableGUIDs layersGUID = JsonUtility.FromJson<SerializeableGUIDs>(materialImporter.userData);
                if (layersGUID.GUIDArray.Length > 0)
                {
                    layers = new Material[layersGUID.GUIDArray.Length];
                    for (int i = 0; i < layersGUID.GUIDArray.Length; ++i)
                    {
                        layers[i] = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(layersGUID.GUIDArray[i]), typeof(Material)) as Material;
                    }
                }
            }
        }

        void SaveMaterialLayers(AssetImporter materialImporter)
        {
            SerializeableGUIDs layersGUID;
            layersGUID.GUIDArray = new string[m_MaterialLayers.Length];
            for (int i = 0; i < m_MaterialLayers.Length; ++i)
            {
                if (m_MaterialLayers[i] != null)
                    layersGUID.GUIDArray[i] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_MaterialLayers[i].GetInstanceID()));
            }

            materialImporter.userData = JsonUtility.ToJson(layersGUID);
        }

        bool CheckInputOptionConsistency(string optionName, string[] shortNames, ref string outValueNames)
        {
            bool result = true;
            outValueNames = "";
            for (int i = 0; i < numLayer; ++i)
            {
                Material layer = m_MaterialLayers[i];
                if (layer != null)
                {
                    int currentValue = (int)layer.GetFloat(optionName); // All options are in fact enums
                    Debug.Assert(currentValue < shortNames.Length);
                    outValueNames += shortNames[currentValue] + "    ";

                    for (int j = i + 1; j < numLayer; ++j)
                    {
                        Material otherLayer = m_MaterialLayers[j];
                        if (otherLayer != null)
                        {
                            if (currentValue != (int)otherLayer.GetFloat(optionName))
                            {
                                result = false;
                            }
                        }
                    }
                }
                else
                {
                    outValueNames += "X    ";
                }
            }

            return result;
        }

        bool CheckInputFloatOptionConsistency(string optionName, ref string outValueNames)
        {
            bool result = true;
            outValueNames = "";
            for (int i = 0; i < numLayer; ++i)
            {
                Material layer = m_MaterialLayers[i];
                if (layer != null)
                {
                    float currentValue = layer.GetFloat(optionName);

                    for (int j = i + 1; j < numLayer; ++j)
                    {
                        Material otherLayer = m_MaterialLayers[j];
                        if (otherLayer != null)
                        {
                            if (currentValue != otherLayer.GetFloat(optionName))
                            {
                                result = false;
                            }
                        }
                    }
                }
                else
                {
                    outValueNames += "X    ";
                }
            }

            return result;
        }

        bool CheckInputMapConsistency(string mapName, ref string outValueNames)
        {
            bool result = true;
            outValueNames = "";
            for (int i = 0; i < numLayer; ++i)
            {
                Material layer = m_MaterialLayers[i];
                if (layer != null)
                {
                    bool currentValue = layer.GetTexture(mapName) != null;
                    outValueNames += (currentValue ? "Y" : "N") + "    ";

                    for (int j = i + 1; j < numLayer; ++j)
                    {
                        Material otherLayer = m_MaterialLayers[j];
                        if (otherLayer != null)
                        {
                            bool otherValue = otherLayer.GetTexture(mapName) != null;
                            if (currentValue != otherValue)
                            {
                                result = false;
                            }
                        }
                    }
                }
                else
                {
                    outValueNames += "N    ";
                }
            }

            return result;
        }

        void CheckLayerConsistency()
        {
            string optionValueNames = "";
            // We need to check consistency between all layers.
            // Each input options and each input maps can result in different #defines in the shader so all of them need to be consistent
            // otherwise the result will be undetermined

            // Input options consistency
            string[] smoothnessSourceShortNames = { "Mask", "Albedo" };
            string[] normalMapShortNames = { "Tan", "Obj" };
            string[] detailModeShortNames = { "DNormal", "DAOHeight" };

            string warningInputOptions = "";
            if (!CheckInputOptionConsistency(kSmoothnessTextureChannel, smoothnessSourceShortNames, ref optionValueNames))
            {
                warningInputOptions += "Smoothness Source:    " + optionValueNames + "\n";
            }
            if (!CheckInputOptionConsistency(kNormalMapSpace, normalMapShortNames, ref optionValueNames))
            {
                warningInputOptions += "Normal Map Space:    " + optionValueNames + "\n";
            }
            if (!CheckInputFloatOptionConsistency(kEnablePerPixelDisplacement, ref optionValueNames))
            {
                warningInputOptions += "Per pixel displacement:    " + optionValueNames + "\n";
            }
            if (!CheckInputOptionConsistency(kDetailMapMode, detailModeShortNames, ref optionValueNames))
            {
                warningInputOptions += "Detail Map Mode:    " + optionValueNames + "\n";
            }

            if (warningInputOptions != string.Empty)
            {
                warningInputOptions = "Input Option Consistency Error:\n" + warningInputOptions;
            }

            // Check input maps consistency
            string warningInputMaps = "";

            if (!CheckInputMapConsistency(kNormalMap, ref optionValueNames))
            {
                warningInputMaps += "Normal Map:    " + optionValueNames + "\n";
            }
            if (!CheckInputMapConsistency(kDetailMap, ref optionValueNames))
            {
                warningInputMaps += "Detail Map:    " + optionValueNames + "\n";
            }
            if (!CheckInputMapConsistency(kMaskMap, ref optionValueNames))
            {
                warningInputMaps += "Mask Map:    " + optionValueNames + "\n";
            }
            if (!CheckInputMapConsistency(kSpecularOcclusionMap, ref optionValueNames))
            {
                warningInputMaps += "Specular Occlusion Map:    " + optionValueNames + "\n";
            }
            if (!CheckInputMapConsistency(kHeightMap, ref optionValueNames))
            {
                warningInputMaps += "Height Map:    " + optionValueNames + "\n";
            }

            if (warningInputMaps != string.Empty)
            {
                warningInputMaps = "Input Maps Consistency Error:\n" + warningInputMaps;
                if (warningInputOptions != string.Empty)
                    warningInputMaps = "\n" + warningInputMaps;
            }

            string warning = warningInputOptions + warningInputMaps;
            if (warning != string.Empty)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Error);
            }
        }

        void SynchronizeInputOptions()
        {
            Material material = m_MaterialEditor.target as Material;

            // We synchronize input options with the firsts non null Layer (all layers should have consistent options)
            Material firstLayer = null;
            int i = 0;
            while (i < numLayer && !(firstLayer = m_MaterialLayers[i])) ++i;

            if (firstLayer != null)
            {
                material.SetFloat(kSmoothnessTextureChannel, firstLayer.GetFloat(kSmoothnessTextureChannel));
                material.SetFloat(kNormalMapSpace, firstLayer.GetFloat(kNormalMapSpace));
                material.SetFloat(kEnablePerPixelDisplacement, firstLayer.GetFloat(kEnablePerPixelDisplacement));
                // Force emissive to be emissive color
                material.SetFloat(kEmissiveColorMode, (float)EmissiveColorMode.UseEmissiveColor);
            }
        }

        bool DoLayerGUI(AssetImporter materialImporter, int layerIndex)
        {
            bool result = false;

            Material material = m_MaterialEditor.target as Material;

            EditorGUILayout.LabelField(styles.layerLabels[layerIndex], styles.layerLabelColors[layerIndex]);

            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            m_MaterialLayers[layerIndex] = EditorGUILayout.ObjectField(styles.materialLayerText, m_MaterialLayers[layerIndex], typeof(Material), true) as Material;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(materialImporter, "Change layer material");
                SynchronizeLayerProperties(material, m_MaterialLayers, layerIndex);
                result = true;
            }

            EditorGUI.BeginChangeCheck();
            m_MaterialEditor.ShaderProperty(layerUVBase[layerIndex], styles.UVBaseText);
            if (EditorGUI.EndChangeCheck())
            {
                SynchronizeLayerProperties(material, m_MaterialLayers, layerIndex);
                result = true;
            }
            if (((LayerUVBaseMapping)layerUVBase[layerIndex].floatValue == LayerUVBaseMapping.Planar) ||
                ((LayerUVBaseMapping)layerUVBase[layerIndex].floatValue == LayerUVBaseMapping.Triplanar))
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.ShaderProperty(layerTexWorldScale[layerIndex], styles.layerTexWorldScaleText);
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                m_MaterialEditor.ShaderProperty(layerUVDetail[layerIndex], styles.UVDetailText);
                if (EditorGUI.EndChangeCheck())
                {
                    SynchronizeLayerProperties(material, m_MaterialLayers, layerIndex);
                    result = true;
                }
            }

            if(layerIndex > 0)
            {
                if (!useHeightBasedBlend.hasMixedValue && useHeightBasedBlend.floatValue != 0.0f)
                {
                    int heightParamIndex = layerIndex - 1;

                    if(useHeightBasedBlendV2.floatValue != 1.0f)
                    {
                        //m_MaterialEditor.ShaderProperty(inheritBaseLayer[heightParamIndex], styles.inheritBaseLayerText);
                        m_MaterialEditor.ShaderProperty(heightOffset[heightParamIndex], styles.heightOffsetText);
                        m_MaterialEditor.ShaderProperty(heightFactor[heightParamIndex], styles.heightFactorText);
                        m_MaterialEditor.ShaderProperty(blendSize[heightParamIndex], styles.blendSizeText);
                        m_MaterialEditor.ShaderProperty(inheritBaseColor[heightParamIndex], styles.inheritBaseColorText);
                        EditorGUI.indentLevel++;
                        m_MaterialEditor.ShaderProperty(inheritBaseColorThreshold[heightParamIndex], styles.inheritBaseColorThresholdText);
                        EditorGUI.indentLevel--;
                        m_MaterialEditor.ShaderProperty(inheritBaseNormal[heightParamIndex], styles.inheritBaseNormalText);
                    }
                    else
                    {
                        m_MaterialEditor.ShaderProperty(heightFactor[heightParamIndex], styles.heightFactorText);
                        m_MaterialEditor.ShaderProperty(heightCenterOffset[heightParamIndex], styles.heightCenterOffsetText);
                        m_MaterialEditor.ShaderProperty(blendUsingHeight[heightParamIndex], styles.blendUsingHeight);
                        m_MaterialEditor.ShaderProperty(inheritBaseColor[heightParamIndex], styles.inheritBaseColorText);
                        EditorGUI.indentLevel++;
                        m_MaterialEditor.ShaderProperty(inheritBaseColorThreshold[heightParamIndex], styles.inheritBaseColorThresholdText);
                        EditorGUI.indentLevel--;
                        m_MaterialEditor.ShaderProperty(inheritBaseNormal[heightParamIndex], styles.inheritBaseNormalText);
                        m_MaterialEditor.ShaderProperty(inheritBaseHeight[heightParamIndex], styles.inheritBaseHeightText);
                        m_MaterialEditor.ShaderProperty(opacityAsDensity[heightParamIndex], styles.opacityAsDensityText);
                        m_MaterialEditor.ShaderProperty(minimumOpacity[heightParamIndex], styles.minimumOpacityText);
                    }
                }
            }

            EditorGUI.indentLevel--;

            if(layerIndex == 0)
                EditorGUILayout.Space();

            return result;
        }

        bool DoLayersGUI(AssetImporter materialImporter)
        {
            Material material = m_MaterialEditor.target as Material;

            bool layerChanged = false;

            GUI.changed = false;

            EditorGUI.indentLevel++;
            GUILayout.Label(styles.layersText, EditorStyles.boldLabel);

            EditorGUI.showMixedValue = layerCount.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int newLayerCount = EditorGUILayout.IntSlider(styles.layerCountText, (int)layerCount.floatValue, 2, 4);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Change layer count");
                layerCount.floatValue = (float)newLayerCount;
                SynchronizeAllLayersProperties();
                layerChanged = true;
            }

            m_MaterialEditor.TexturePropertySingleLine(styles.layerMapMaskText, layerMaskMap);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = useHeightBasedBlend.hasMixedValue;
            bool enabled = EditorGUILayout.Toggle(styles.useHeightBasedBlendText, useHeightBasedBlend.floatValue > 0.0f);
            if (EditorGUI.EndChangeCheck())
            {
                useHeightBasedBlend.floatValue = enabled ? 1.0f : 0.0f;
            }
            if (enabled)
            {
                EditorGUI.BeginChangeCheck();
                bool enabledV2 = EditorGUILayout.Toggle(styles.useHeightBasedBlendV2Text, useHeightBasedBlendV2.floatValue > 0.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    useHeightBasedBlendV2.floatValue = enabledV2 ? 1.0f : 0.0f;
                }
                if(!enabledV2)
                    m_MaterialEditor.ShaderProperty(vertexColorHeightFactor, styles.vertexColorHeightMultiplierText);
            }
            else
            {
                m_MaterialEditor.ShaderProperty(layerMaskVertexColor, styles.layerMapVertexColorText);
            }


            EditorGUILayout.Space();

            for (int i = 0; i < numLayer; i++)
            {
                layerChanged |= DoLayerGUI(materialImporter, i);
            }

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(styles.syncButtonText))
                {
                    SynchronizeAllLayersProperties();
                    layerChanged = true;
                }
            }
            GUILayout.EndHorizontal();

            EditorGUI.indentLevel--;

            layerChanged |= GUI.changed;
            GUI.changed = false;

            return layerChanged;
        }

        protected override void SetupMaterialKeywords(Material material)
        {
            SetupCommonOptionsKeywords(material);
            SetupLayersKeywords(material);

            // Find first non null layer
            int i = 0;
            while (i < numLayer && (m_MaterialLayers[i] == null)) ++i;

            if (i < numLayer)
            {
                SetKeyword(material, "_NORMALMAP", material.GetTexture(kNormalMap + i));
                SetKeyword(material, "_MASKMAP", material.GetTexture(kMaskMap + i));
                SetKeyword(material, "_SPECULAROCCLUSIONMAP", material.GetTexture(kSpecularOcclusionMap + i));
                SetKeyword(material, "_HEIGHTMAP", material.GetTexture(kHeightMap + i));
                SetKeyword(material, "_DETAIL_MAP", material.GetTexture(kDetailMap + i));

                SetKeyword(material, "_DETAIL_MAP_WITH_NORMAL", ((DetailMapMode)material.GetFloat(kDetailMapMode)) == DetailMapMode.DetailWithNormal);
                bool perPixelDisplacement = material.GetFloat(kEnablePerPixelDisplacement) == 1.0;
                SetKeyword(material, "_PER_PIXEL_DISPLACEMENT", perPixelDisplacement);
                SetKeyword(material, "_NORMALMAP_TANGENT_SPACE", ((NormalMapSpace)material.GetFloat(kNormalMapSpace)) == NormalMapSpace.TangentSpace);
                SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", ((SmoothnessMapChannel)material.GetFloat(kSmoothnessTextureChannel)) == SmoothnessMapChannel.AlbedoAlpha);
            }

            SetKeyword(material, "_EMISSIVE_COLOR_MAP", material.GetTexture(kEmissiveColorMap));
            bool useHeightBasedBlend = material.GetFloat(kUseHeightBasedBlend) != 0.0f;
            if(useHeightBasedBlend)
            {
                bool useHeightBasedBlendV2 = material.GetFloat(kUseHeightBasedBlendV2) != 0.0f;

                SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_ADD", false);
                SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_MUL", false);
                SetKeyword(material, "_HEIGHT_BASED_BLEND", true);
                SetKeyword(material, "_HEIGHT_BASED_BLEND_V2", useHeightBasedBlendV2);
            }
            else
            {
                if (material.GetTexture(kLayerMaskMap) != null)
                {
                    SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_ADD", material.GetFloat(kLayerMaskVertexColor) != 0.0f);
                    SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_MUL", false);
                }
                else
                {
                    SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_MUL", material.GetFloat(kLayerMaskVertexColor) != 0.0f);
                    SetKeyword(material, "_LAYER_MASK_VERTEX_COLOR_ADD", false);
                }
                SetKeyword(material, "_HEIGHT_BASED_BLEND", false);
            }

            // We have to check for each layer if the UV2 or UV3 is needed.
            bool needUV3 = false;
            bool needUV2 = false;
            for (int layer = 0; layer < numLayer; ++layer)
            {
                string uvBase = string.Format("{0}{1}", kUVBase, layer);
                string uvDetail = string.Format("{0}{1}", kUVDetail, layer);

                if (    ((UVDetailMapping)material.GetFloat(uvDetail) == UVDetailMapping.UV2) ||
                        ((LayerUVBaseMapping)material.GetFloat(uvBase) == LayerUVBaseMapping.UV2) )
                {
                    needUV2 = true;
                }

                if (    ((UVDetailMapping)material.GetFloat(uvDetail) == UVDetailMapping.UV3) ||
                        ((LayerUVBaseMapping)material.GetFloat(uvBase) == LayerUVBaseMapping.UV3) )
                {
                    needUV3 = true;
                    break; // If we find it UV3 let's early out
                }
            }

            if (needUV3)
            {
                material.DisableKeyword("_REQUIRE_UV2");
                material.EnableKeyword("_REQUIRE_UV3");
            }
            else if (needUV2)
            {
                material.EnableKeyword("_REQUIRE_UV2");
                material.DisableKeyword("_REQUIRE_UV3");
            }
            else
            {
                material.DisableKeyword("_REQUIRE_UV2");
                material.DisableKeyword("_REQUIRE_UV3");
            }
        }

        void SetupLayersKeywords(Material material)
        {
            if (numLayer == 4)
            {
                SetKeyword(material, "_LAYEREDLIT_4_LAYERS", true);
                SetKeyword(material, "_LAYEREDLIT_3_LAYERS", false);
            }
            else if (numLayer == 3)
            {
                SetKeyword(material, "_LAYEREDLIT_4_LAYERS", false);
                SetKeyword(material, "_LAYEREDLIT_3_LAYERS", true);
            }
            else
            {
                SetKeyword(material, "_LAYEREDLIT_4_LAYERS", false);
                SetKeyword(material, "_LAYEREDLIT_3_LAYERS", false);
            }

            const string kLayerMappingTriplanar = "_LAYER_MAPPING_TRIPLANAR_";

            for (int i = 0 ; i < numLayer; ++i)
            {
                // We setup the masking map based on the enum for each layer.
                // using mapping mask allow to reduce the number of generated combination for a very small increase in ALU
                string layerUVBaseParam = string.Format("{0}{1}", kUVBase, i);
                LayerUVBaseMapping layerUVBaseMapping = (LayerUVBaseMapping)material.GetFloat(layerUVBaseParam);
                string layerUVDetailParam = string.Format("{0}{1}", kUVDetail, i);
                UVDetailMapping layerUVDetailMapping = (UVDetailMapping)material.GetFloat(layerUVDetailParam);
                string currentLayerMappingTriplanar = string.Format("{0}{1}", kLayerMappingTriplanar, i);

                float X, Y, Z, W;
                X = (layerUVBaseMapping == LayerUVBaseMapping.UV0) ? 1.0f : 0.0f;
                Y = (layerUVBaseMapping == LayerUVBaseMapping.UV1) ? 1.0f : 0.0f;
                Z = (layerUVBaseMapping == LayerUVBaseMapping.UV2) ? 1.0f : 0.0f;
                W = (layerUVBaseMapping == LayerUVBaseMapping.UV3) ? 1.0f : 0.0f;
                layerUVMappingMask[i].colorValue = new Color(X, Y, Z, W);
                layerUVMappingPlanar[i].floatValue = (layerUVBaseMapping == LayerUVBaseMapping.Planar) ? 1.0f : 0.0f;

                SetKeyword(material, currentLayerMappingTriplanar, layerUVBaseMapping == LayerUVBaseMapping.Triplanar);

                X = (layerUVDetailMapping == UVDetailMapping.UV0) ? 1.0f : 0.0f;
                Y = (layerUVDetailMapping == UVDetailMapping.UV1) ? 1.0f : 0.0f;
                Z = (layerUVDetailMapping == UVDetailMapping.UV2) ? 1.0f : 0.0f;
                W = (layerUVDetailMapping == UVDetailMapping.UV3) ? 1.0f : 0.0f;
                layerUVDetailsMappingMask[i].colorValue = new Color(X, Y, Z, W);
            }
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            FindCommonOptionProperties(props);
            FindMaterialProperties(props);

            m_MaterialEditor = materialEditor;

            m_MaterialEditor.serializedObject.Update();

            Material material = m_MaterialEditor.target as Material;
            AssetImporter materialImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetInstanceID()));

            InitializeMaterialLayers(materialImporter, ref m_MaterialLayers);

            bool optionsChanged = false;
            EditorGUI.BeginChangeCheck();
            {
                ShaderOptionsGUI();
                EditorGUILayout.Space();
            }
            if (EditorGUI.EndChangeCheck())
            {
                optionsChanged = true;
            }

            bool layerChanged = DoLayersGUI(materialImporter);

            EditorGUILayout.Space();
            GUILayout.Label(Styles.emissiveText, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            m_MaterialEditor.TexturePropertySingleLine(Styles.emissiveText, layerEmissiveColorMap, layerEmissiveColor);
            m_MaterialEditor.ShaderProperty(layerEmissiveIntensity, Styles.emissiveIntensityText);
            m_MaterialEditor.LightmapEmissionProperty(1);
            EditorGUI.indentLevel--;

            CheckLayerConsistency();

            if (layerChanged || optionsChanged)
            {
                SynchronizeInputOptions();

                foreach (var obj in m_MaterialEditor.targets)
                {
                    SetupMaterialKeywords((Material)obj);
                }

                SaveMaterialLayers(materialImporter);
            }

            m_MaterialEditor.serializedObject.ApplyModifiedProperties();
        }
    }
} // namespace UnityEditor
