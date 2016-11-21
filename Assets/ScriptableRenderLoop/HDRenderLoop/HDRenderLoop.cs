// Must be in sync with ShaderConfig.cs
//#define VELOCITY_IN_GBUFFER

using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.MaterialGraph;
using UnityEngine.Graphing;

namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    [Serializable]
    public abstract class AbstractHDRenderLoopMasterNode : AbstractMasterNode
    {
        public AbstractHDRenderLoopMasterNode()
        {
            name = GetName();
            UpdateNodeAfterDeserialization();
        }

        protected abstract Type GetSurfaceType();
        protected abstract string GetName();
        protected abstract int GetMatchingMaterialID();

        public sealed override void UpdateNodeAfterDeserialization()
        {
            var surfaceType = GetSurfaceType();
            if (surfaceType != null)
            {
                var fieldsBuiltIn = typeof(Builtin.BuiltinData).GetFields();
                var fieldsSurface = surfaceType.GetFields();
                var slots = fieldsSurface.Concat(fieldsBuiltIn).Select((field, index) =>
                {
                    var attributes = (SurfaceDataAttributes[])field.GetCustomAttributes(typeof(SurfaceDataAttributes), false);
                    var attribute = attributes.Length > 0 ? attributes[0] : new SurfaceDataAttributes();

                    var valueType = SlotValueType.Dynamic;
                    var fieldType = field.FieldType;
                    if (fieldType == typeof(float))
                    {
                        valueType = SlotValueType.Vector1;
                    }
                    else if (fieldType == typeof(Vector2))
                    {
                        valueType = SlotValueType.Vector2;
                    }
                    else if (fieldType == typeof(Vector3))
                    {
                        valueType = SlotValueType.Vector3;
                    }
                    else if (fieldType == typeof(Vector2))
                    {
                        valueType = SlotValueType.Vector4;
                    }

                    return new
                    {
                        index = index,
                        priority = attribute.priority,
                        displayName = attribute.displayName,
                        materialID = attribute.filter,
                        shaderOutputName = field.Name,
                        valueType = valueType
                    };
                })
                .Where(o => (o.materialID == null || o.materialID.Contains(GetMatchingMaterialID())) && o.valueType != SlotValueType.Dynamic)
                .OrderBy(o => o.priority)
                .ThenBy(o => o.displayName)
                .ToArray();

                foreach (var slot in slots)
                {
                    if (slot.displayName == "Normal") //WIP : should be a setting in attribute
                    {
                        AddSlot(new MaterialSlotDefaultInput(slot.index, slot.displayName, slot.shaderOutputName, Graphing.SlotType.Input, slot.valueType, new WorldSpaceNormalNode(), 0));
                    }
                    else
                    {
                        AddSlot(new MaterialSlot(slot.index, slot.displayName, slot.shaderOutputName, Graphing.SlotType.Input, slot.valueType, Vector4.zero));
                    }
                }
            }
        }

        private static void CollectFromNodesFromNodes(List<INode> nodeList, INode node, List<int> slotId)
        {
            // no where to start
            if (node == null)
                return;

            // allready added this node
            if (nodeList.Contains(node))
                return;

            // if we have a slot passed in but can not find it on the node abort
            if (slotId != null && node.GetInputSlots<ISlot>().All(x => !slotId.Contains(x.id)))
                return;

            var validSlots = ListPool<int>.Get();
            if (slotId != null)
                slotId.ForEach(x => validSlots.Add(x));
            else
                validSlots.AddRange(node.GetInputSlots<ISlot>().Select(x => x.id));

            foreach (var slot in validSlots)
            {
                foreach (var edge in node.owner.GetEdges(node.GetSlotReference(slot)))
                {
                    var outputNode = node.owner.GetNodeFromGuid(edge.outputSlot.nodeGuid);
                    CollectFromNodesFromNodes(nodeList, outputNode, null);
                }
            }
            nodeList.Add(node);
            ListPool<int>.Release(validSlots);
        }

        private struct Vayring
        {
            public string attributeName;
            public string semantic;
            public string vayringName;
            public SlotValueType type;
            public string vertexCode;
            public string pixelCode;
        };

        private string GenerateLitDataTemplate(GenerationMode mode, string useDataInput, string needFragInput, PropertyGenerator propertyGenerator, ShaderGenerator propertyUsagesVisitor, ShaderGenerator shaderFunctionVisitor)
        {
            var activeNodeList = new List<INode>();

            var useDataInputRegex = new Regex(useDataInput);
            var needFragInputRegex = new Regex(needFragInput);
            var slotIDList = GetInputSlots<MaterialSlot>().Where(s => useDataInputRegex.IsMatch(s.shaderOutputName)).Select(s => s.id).ToList();

            CollectFromNodesFromNodes(activeNodeList, this, slotIDList);

            var vayrings = new List<Vayring>();
            if (needFragInputRegex.IsMatch("meshUV0") || activeNodeList.OfType<IMayRequireMeshUV>().Any(x => x.RequiresMeshUV()))
            {
                vayrings.Add(new Vayring()
                {
                    attributeName = "meshUV0",
                    semantic = "TEXCOORD0",
                    vayringName = "meshUV0",
                    type = SlotValueType.Vector2,
                    vertexCode = "output.meshUV0 = input.meshUV0;",
                    pixelCode = string.Format("float4 {0} = float4(fragInput.meshUV0, 0, 0);", ShaderGeneratorNames.UV0)
                });
            }

            if (needFragInputRegex.IsMatch("normalWS") || activeNodeList.OfType<IMayRequireNormal>().Any(x => x.RequiresNormal()))
            {
                vayrings.Add(new Vayring()
                {
                    attributeName = "normalOS",
                    semantic = "NORMAL",
                    vayringName = "normalWS",
                    type = SlotValueType.Vector3,
                    vertexCode = "output.normalWS = TransformObjectToWorldNormal(input.normalOS);",
                    pixelCode = string.Format("float3 {0} = normalize(fragInput.normalWS);", ShaderGeneratorNames.WorldSpaceNormal)
                });
            }

            if (needFragInputRegex.IsMatch("positionWS") || activeNodeList.OfType<IMayRequireWorldPosition>().Any(x => x.RequiresWorldPosition()))
            {
                vayrings.Add(new Vayring()
                {
                    vayringName = "positionWS",
                    type = SlotValueType.Vector3,
                    vertexCode = "output.positionWS = TransformObjectToWorld(input.positionOS);",
                    pixelCode = string.Format("float3 {0} = fragInput.positionWS;", ShaderGeneratorNames.WorldSpacePosition)
                });
            }

            if (needFragInputRegex.IsMatch("viewDirectionWS") || activeNodeList.OfType<IMayRequireViewDirection>().Any(x => x.RequiresViewDirection()))
            {
                vayrings.Add(new Vayring()
                {
                    vayringName = "viewDirectionWS",
                    type = SlotValueType.Vector3,
                    vertexCode = "output.viewDirectionWS = GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(input.positionOS));",
                    pixelCode = string.Format("float3 {0} = normalize(fragInput.viewDirectionWS);", ShaderGeneratorNames.WorldSpaceViewDirection)
                });
            }

            Func<SlotValueType, int> _fnTypeToSize = o =>
            {
                switch (o)
                {
                    case SlotValueType.Vector1: return 1;
                    case SlotValueType.Vector2: return 2;
                    case SlotValueType.Vector3: return 3;
                    case SlotValueType.Vector4: return 4;
                }
                return 0;
            };

            var packedVarying = new ShaderGenerator();
            int totalSize = vayrings.Sum(x => _fnTypeToSize(x.type));

            if (totalSize > 0)
            {
                var interpolatorCount = Mathf.Ceil((float)totalSize / 4.0f);
                packedVarying.AddShaderChunk(string.Format("float4 interpolators[{0}] : TEXCOORD0;", (int)interpolatorCount), false);
            }

            var vayringVisitor = new ShaderGenerator();
            var pixelShaderInitVisitor = new ShaderGenerator();
            var vertexAttributeVisitor = new ShaderGenerator();
            var vertexShaderBodyVisitor = new ShaderGenerator();
            var packInterpolatorVisitor = new ShaderGenerator();
            var unpackInterpolatorVisitor = new ShaderGenerator();
            int currentIndex = 0;
            int currentChannel = 0;
            foreach (var vayring in vayrings)
            {
                var typeSize = _fnTypeToSize(vayring.type);
                if (!string.IsNullOrEmpty(vayring.attributeName))
                {
                    vertexAttributeVisitor.AddShaderChunk(string.Format("float{0} {1} : {2};", typeSize, vayring.attributeName, vayring.semantic), true);
                }

                vayringVisitor.AddShaderChunk(string.Format("float{0} {1};", typeSize, vayring.vayringName), false);
                vertexShaderBodyVisitor.AddShaderChunk(vayring.vertexCode, false);
                pixelShaderInitVisitor.AddShaderChunk(vayring.pixelCode, false);

                for (int channel = 0; channel < typeSize; ++channel)
                {
                    var packed = string.Format("interpolators[{0}][{1}]", currentIndex, currentChannel);
                    var source = string.Format("{0}[{1}]", vayring.vayringName, channel);
                    packInterpolatorVisitor.AddShaderChunk(string.Format("output.{0} = input.{1};", packed, source), false);
                    unpackInterpolatorVisitor.AddShaderChunk(string.Format("output.{0} = input.{1};", source, packed), false);

                    if (currentChannel == 3)
                    {
                        currentChannel = 0;
                        currentIndex++;
                    }
                    else
                    {
                        currentChannel++;
                    }
                }
            }

            foreach (var node in activeNodeList.OfType<AbstractMaterialNode>())
            {
                if (node is IGeneratesFunction) (node as IGeneratesFunction).GenerateNodeFunction(shaderFunctionVisitor, mode);
                if (node is IGenerateProperties)
                {
                    (node as IGenerateProperties).GeneratePropertyBlock(propertyGenerator, mode);
                    (node as IGenerateProperties).GeneratePropertyUsages(propertyUsagesVisitor, mode);
                }
            }

            var pixelShaderBodyVisitor = new ShaderGenerator();
            foreach (var node in activeNodeList)
            {
                if (node is IGeneratesBodyCode)
                    (node as IGeneratesBodyCode).GenerateNodeCode(pixelShaderBodyVisitor, mode);
            }

            foreach (var slot in GetInputSlots<MaterialSlot>())
            {
                if (!slotIDList.Contains(slot.id))
                    continue;

                foreach (var edge in owner.GetEdges(slot.slotReference))
                {
                    var outputRef = edge.outputSlot;
                    var fromNode = owner.GetNodeFromGuid<AbstractMaterialNode>(outputRef.nodeGuid);
                    if (fromNode == null)
                        continue;

                    var slotOutputName = slot.shaderOutputName;

                    var inputStruct = typeof(Lit.SurfaceData).GetFields().Any(o => o.Name == slotOutputName) ? "surfaceData" : "builtinData";
                    pixelShaderBodyVisitor.AddShaderChunk(inputStruct + "." + slot.shaderOutputName + " = " + fromNode.GetVariableNameForSlot(outputRef.slotId) + ";", true);
                }
            }

            var template =
@"struct FragInput
{
    float4 unPositionSS;
${VaryingAttributes}
};

void GetSurfaceAndBuiltinData(FragInput fragInput, out SurfaceData surfaceData, out BuiltinData builtinData)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    ZERO_INITIALIZE(BuiltinData, builtinData);
${PixelShaderInitialize}
${PixelShaderBody}
}

struct Attributes
{
    float3 positionOS : POSITION;
${VertexAttributes}
};

struct Varyings
{
    float4 positionHS;
${VaryingAttributes}
};

struct PackedVaryings
{
    float4 positionHS : SV_Position;
${PackedVaryingAttributes}
};

PackedVaryings PackVaryings(Varyings input)
{
    PackedVaryings output;
    output.positionHS = input.positionHS;
${PackingVaryingCode}
    return output;
}

FragInput UnpackVaryings(PackedVaryings input)
{
    FragInput output;
    ZERO_INITIALIZE(FragInput, output);

    output.unPositionSS = input.positionHS;
${UnpackVaryingCode}
    return output;
}

PackedVaryings VertDefault(Attributes input)
{
    Varyings output;
    output.positionHS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
${VertexShaderBody}
    return PackVaryings(output);
}";

            var resultShader = template.Replace("${VaryingAttributes}", vayringVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${PixelShaderInitialize}", pixelShaderInitVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${PixelShaderBody}", pixelShaderBodyVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${VertexAttributes}", vertexAttributeVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${PackedVaryingAttributes}", packedVarying.GetShaderString(1));
            resultShader = resultShader.Replace("${PackingVaryingCode}", packInterpolatorVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${UnpackVaryingCode}", unpackInterpolatorVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${VertexShaderBody}", vertexShaderBodyVisitor.GetShaderString(1));
            return resultShader;
        }

        public override string GetShader(MaterialOptions options, GenerationMode mode, out List<PropertyGenerator.TextureInfo> configuredTextures)
        {
            configuredTextures = new List<PropertyGenerator.TextureInfo>();

            var path = "Assets/ScriptableRenderLoop/HDRenderLoop/Material/Lit/Lit.template";
            if (!System.IO.File.Exists(path))
                return "";

            var templateText = System.IO.File.ReadAllText(path);

            var shaderPropertiesVisitor = new PropertyGenerator();
            var propertyUsagesVisitor = new ShaderGenerator();
            var shaderFunctionVisitor = new ShaderGenerator();
            var templateToShader = new Dictionary<string, string>();

            var findLitShareTemplate = new System.Text.RegularExpressions.Regex("#{LitTemplate.*}");
            var findUseDataInput = new System.Text.RegularExpressions.Regex("useDataInput:{(.*?)}");
            var findNeedFragInput = new System.Text.RegularExpressions.Regex("needFragInput:{(.*?)}");
            foreach (System.Text.RegularExpressions.Match match in findLitShareTemplate.Matches(templateText))
            {
                if (match.Captures.Count > 0)
                {
                    var capture = match.Captures[0].Value;

                    if (!templateToShader.ContainsKey(capture))
                    {
                        var useUseDataInputRegex = "";
                        if (findUseDataInput.IsMatch(capture))
                        {
                            var useInputMatch = findUseDataInput.Match(capture);
                            useUseDataInputRegex = useInputMatch.Groups.Count > 1 ? useInputMatch.Groups[1].Value : "";
                        }

                        var needFragInputRegex = "";
                        if (findNeedFragInput.IsMatch(capture))
                        {
                            var useInputMatch = findNeedFragInput.Match(capture);
                            needFragInputRegex = useInputMatch.Groups.Count > 1 ? useInputMatch.Groups[1].Value : "";
                        }

                        var generatedShader = GenerateLitDataTemplate(mode, useUseDataInputRegex, needFragInputRegex, shaderPropertiesVisitor, propertyUsagesVisitor, shaderFunctionVisitor);
                        templateToShader.Add(capture, generatedShader);
                    }
                }
            }

            var resultShader = templateText.Replace("${ShaderName}", GetType() + guid.ToString());
            resultShader = resultShader.Replace("${ShaderPropertiesHeader}", shaderPropertiesVisitor.GetShaderString(2));
            resultShader = resultShader.Replace("${ShaderPropertyUsages}", propertyUsagesVisitor.GetShaderString(1));
            resultShader = resultShader.Replace("${ShaderFunctions}", shaderFunctionVisitor.GetShaderString(1));
            foreach (var entry in templateToShader)
            {
                resultShader = resultShader.Replace(entry.Key, entry.Value);
            }

           configuredTextures = shaderPropertiesVisitor.GetConfiguredTexutres();
           resultShader = Regex.Replace(resultShader, @"\t", "    ");

            return Regex.Replace(resultShader, @"\r\n|\n\r|\n|\r", Environment.NewLine);
        }
    }

    [Serializable]
    [Title("HDRenderLoop/StandardLit")]
    public class StandardtLit : AbstractHDRenderLoopMasterNode
    {
        protected override string GetName()
        {
            return "MasterNodeStandardLit";
        }

        protected override Type GetSurfaceType()
        {
            return typeof(Lit.SurfaceData);
        }

        protected override int GetMatchingMaterialID()
        {
            return (int)Lit.MaterialId.LitStandard;
        }
    }

    [Serializable]
    [Title("HDRenderLoop/SubsurfaceScatteringLit")]
    public class SubsurfaceScatteringLit : AbstractHDRenderLoopMasterNode
    {
        protected override string GetName()
        {
            return "MasterNodeSubsurfaceScatteringLit";
        }

        protected override Type GetSurfaceType()
        {
            return typeof(Lit.SurfaceData);
        }

        protected override int GetMatchingMaterialID()
        {
            return (int)Lit.MaterialId.LitSSS;
        }
    }

    [Serializable]
    [Title("HDRenderLoop/SubsurfaceClearCoatLit")]
    public class SubsurfaceClearCoatLit : AbstractHDRenderLoopMasterNode
    {
        protected override string GetName()
        {
            return "MasterNodeSubsurfaceClearCoatLit";
        }

        protected override Type GetSurfaceType()
        {
            return typeof(Lit.SurfaceData);
        }

        protected override int GetMatchingMaterialID()
        {
            return (int)Lit.MaterialId.LitClearCoat;
        }
    }

    [Serializable]
    [Title("HDRenderLoop/SpecularColorLit")]
    public class SpecularColorLit : AbstractHDRenderLoopMasterNode
    {
        protected override string GetName()
        {
            return "MasterNodeSpecularColorLit";
        }

        protected override Type GetSurfaceType()
        {
            return typeof(Lit.SurfaceData);
        }

        protected override int GetMatchingMaterialID()
        {
            return (int)Lit.MaterialId.LitSpecular;
        }
    }
}
namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    [ExecuteInEditMode]
    // This HDRenderLoop assume linear lighting. Don't work with gamma.
    public partial class HDRenderLoop : ScriptableRenderLoop
    {
        const string k_HDRenderLoopPath = "Assets/ScriptableRenderLoop/HDRenderLoop/HDRenderLoop.asset";

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Renderloop/CreateHDRenderLoop")]
        static void CreateHDRenderLoop()
        {
            var instance = ScriptableObject.CreateInstance<HDRenderLoop>();
            UnityEditor.AssetDatabase.CreateAsset(instance, k_HDRenderLoopPath);
        }

        [UnityEditor.MenuItem("HDRenderLoop/Add \"Additional Light Data\" (if not present)")]
        static void AddAdditionalLightData()
        {
            Light[] lights = FindObjectsOfType(typeof(Light)) as Light[];

            foreach (Light light in lights)
            {
                // Do not add a component if there already is one.
                if (light.GetComponent<AdditionalLightData>() == null)
                {
                    light.gameObject.AddComponent<AdditionalLightData>();
                }
            }
        }
#endif

        public class SkyParameters
        {
            public Cubemap skyHDRI;
            public float rotation;
            public float exposure;
            public float multiplier;
        }

        [SerializeField]
        SkyParameters m_SkyParameters = new SkyParameters();
 
        public SkyParameters skyParameters
        {
            get { return m_SkyParameters; }
        }

        public class DebugParameters
        {
            // Material Debugging
            public int debugViewMaterial = 0;

            // Rendering debugging
            public bool displayOpaqueObjects = true;
            public bool displayTransparentObjects = true;

            public bool useForwardRenderingOnly = false; // TODO: Currently there is no way to strip the extra forward shaders generated by the shaders compiler, so we can switch dynamically.
            public bool useDepthPrepass = false;

            public bool enableTonemap = true;
            public float exposure = 0;
        }

        DebugParameters m_DebugParameters = new DebugParameters();
        public DebugParameters debugParameters
        {
            get { return m_DebugParameters; }
        }


        public class GBufferManager
        {
            public const int MaxGbuffer = 8;

            public void SetBufferDescription(int index, string stringId, RenderTextureFormat inFormat, RenderTextureReadWrite inSRGBWrite)
            {
                IDs[index] = Shader.PropertyToID(stringId);
                RTIDs[index] = new RenderTargetIdentifier(IDs[index]);
                formats[index] = inFormat;
                sRGBWrites[index] = inSRGBWrite;
            }

            public void InitGBuffers(int width, int height, CommandBuffer cmd)
            {
                for (int index = 0; index < gbufferCount; index++)
                {
                    /* RTs[index] = */ cmd.GetTemporaryRT(IDs[index], width, height, 0, FilterMode.Point, formats[index], sRGBWrites[index]);
                }
            }

            public RenderTargetIdentifier[] GetGBuffers(CommandBuffer cmd)
            {
                var colorMRTs = new RenderTargetIdentifier[gbufferCount];
                for (int index = 0; index < gbufferCount; index++)
                {
                    colorMRTs[index] = RTIDs[index];
                }


                return colorMRTs;
            }
        
            /*
            public void BindBuffers(Material mat)
            {
                for (int index = 0; index < gbufferCount; index++)
                {
                    mat.SetTexture(IDs[index], RTs[index]);
                }
            }
            */

            public int gbufferCount { get; set; }
            int[] IDs = new int[MaxGbuffer];
            RenderTargetIdentifier[] RTIDs = new RenderTargetIdentifier[MaxGbuffer];
            RenderTextureFormat[] formats = new RenderTextureFormat[MaxGbuffer];
            RenderTextureReadWrite[] sRGBWrites = new RenderTextureReadWrite[MaxGbuffer];
        }

        GBufferManager m_gbufferManager = new GBufferManager();

        [SerializeField]
        ShadowSettings m_ShadowSettings = ShadowSettings.Default;
        ShadowRenderPass m_ShadowPass;


        public const int k_MaxDirectionalLightsOnSCreen = 2;
        public const int k_MaxPunctualLightsOnSCreen = 512;
        public const int k_MaxAreaLightsOnSCreen = 128;
        public const int k_MaxEnvLightsOnSCreen = 64;
        public const int k_MaxShadowOnScreen = 16;
        public const int k_MaxCascadeCount = 4; //Should be not less than m_Settings.directionalLightCascadeCount;

        [SerializeField]
        TextureSettings m_TextureSettings = TextureSettings.Default;

        // Various set of material use in render loop
        Material m_SkyboxMaterial;
        Material m_SkyHDRIMaterial;
        Material m_DeferredMaterial;
        Material m_FinalPassMaterial;
        Material m_DebugViewMaterialGBuffer;

        // Various buffer
        int m_CameraColorBuffer;
        int m_CameraDepthBuffer;
        int m_VelocityBuffer;
        int m_DistortionBuffer;

        public class LightList
        {
            public List<DirectionalLightData> directionalLights;
            public List<DirectionalShadowData> directionalShadows;
            public List<LightData> punctualLights;
            public List<PunctualShadowData> punctualShadows;
            public List<LightData> areaLights;
            public List<EnvLightData> envLights;
            public Vector4[] directionalShadowSplitSphereSqr;

            // Index mapping list to go from GPU lights (above) to CPU light (in cullResult)
            public List<int> directionalCullIndices;
            public List<int> punctualCullIndices;
            public List<int> areaCullIndices;
            public List<int> envCullIndices;

            public void Clear()
            {
                directionalLights.Clear();
                directionalShadows.Clear();
                punctualLights.Clear();
                punctualShadows.Clear();
                areaLights.Clear();
                envLights.Clear();

                directionalCullIndices.Clear();
                punctualCullIndices.Clear();
                areaCullIndices.Clear();
                envCullIndices.Clear();
            }

            public void Allocate()
            {
                directionalLights = new List<DirectionalLightData>();
                punctualLights = new List<LightData>();
                areaLights = new List<LightData>();
                envLights = new List<EnvLightData>();
                punctualShadows = new List<PunctualShadowData>();
                directionalShadows = new List<DirectionalShadowData>();
                directionalShadowSplitSphereSqr = new Vector4[k_MaxCascadeCount];

                directionalCullIndices = new List<int>();
                punctualCullIndices = new List<int>();
                areaCullIndices = new List<int>();
                envCullIndices = new List<int>();
            }
        }

        LightList m_lightList;

        // Detect when windows size is changing
        int m_WidthOnRecord;
        int m_HeightOnRecord;

        // TODO: Find a way to automatically create/iterate through lightloop
        SinglePass.LightLoop m_SinglePassLightLoop;
        TilePass.LightLoop m_TilePassLightLoop;

        // TODO: Find a way to automatically create/iterate through deferred material
        Lit.RenderLoop m_LitRenderLoop;

        TextureCacheCubemap m_CubeReflTexArray;
        TextureCache2D m_CookieTexArray;
        TextureCacheCubemap m_CubeCookieTexArray;

        void OnEnable()
        {
            Rebuild();
        }

        void OnValidate()
        {
            Rebuild();
        }

        Material CreateEngineMaterial(string shaderPath)
        {
            var mat = new Material(Shader.Find(shaderPath) as Shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return mat;
        }

        public override void Rebuild()
        {
            m_CameraColorBuffer  = Shader.PropertyToID("_CameraColorTexture");
            m_CameraDepthBuffer  = Shader.PropertyToID("_CameraDepthTexture");

            // TODO: We need to have an API to send our sky information to Enlighten. For now use a workaround through skybox/cubemap material...
            m_SkyboxMaterial = CreateEngineMaterial("Skybox/Cubemap");
            RenderSettings.skybox = m_SkyboxMaterial; // Setup this material as the default to be use in RenderSettings
            RenderSettings.ambientIntensity = 1.0f; // fix this to 1, this parameter should not exist!
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox; // Force skybox for our HDRI
            RenderSettings.reflectionIntensity = 1.0f;
        
            m_SkyHDRIMaterial = CreateEngineMaterial("Hidden/HDRenderLoop/SkyHDRI");
            m_DeferredMaterial   = CreateEngineMaterial("Hidden/HDRenderLoop/Deferred");
            m_FinalPassMaterial  = CreateEngineMaterial("Hidden/HDRenderLoop/FinalPass");
            m_DebugViewMaterialGBuffer = CreateEngineMaterial("Hidden/HDRenderLoop/DebugViewMaterialGBuffer");

            m_ShadowPass = new ShadowRenderPass(m_ShadowSettings);

            // Init Gbuffer description
            m_LitRenderLoop = new Lit.RenderLoop(); // Our object can be garbage collected, so need to be allocate here

            m_gbufferManager.gbufferCount = m_LitRenderLoop.GetMaterialGBufferCount();
            RenderTextureFormat[] RTFormat; RenderTextureReadWrite[] RTReadWrite;
            m_LitRenderLoop.GetMaterialGBufferDescription(out RTFormat, out RTReadWrite);

            for (int gbufferIndex = 0; gbufferIndex < m_gbufferManager.gbufferCount; ++gbufferIndex)
            {
                m_gbufferManager.SetBufferDescription(gbufferIndex, "_GBufferTexture" + gbufferIndex, RTFormat[gbufferIndex], RTReadWrite[gbufferIndex]);
            }

#pragma warning disable 162 // warning CS0162: Unreachable code detected
            m_VelocityBuffer = Shader.PropertyToID("_VelocityTexture");
            if (ShaderConfig.VelocityInGbuffer == 1)
            {
                // If velocity is in GBuffer then it is in the last RT. Assign a different name to it.
                m_gbufferManager.SetBufferDescription(m_gbufferManager.gbufferCount, "_VelocityTexture", Builtin.RenderLoop.GetVelocityBufferFormat(), Builtin.RenderLoop.GetVelocityBufferReadWrite());
                m_gbufferManager.gbufferCount++;
            }
#pragma warning restore 162

            m_DistortionBuffer = Shader.PropertyToID("_DistortionTexture");

            m_LitRenderLoop.Rebuild();

            m_CookieTexArray = new TextureCache2D();
            m_CookieTexArray.AllocTextureArray(8, (int)m_TextureSettings.spotCookieSize, (int)m_TextureSettings.spotCookieSize, TextureFormat.RGBA32, true);
            m_CubeCookieTexArray = new TextureCacheCubemap();
            m_CubeCookieTexArray.AllocTextureArray(4, (int)m_TextureSettings.pointCookieSize, TextureFormat.RGBA32, true);
            m_CubeReflTexArray = new TextureCacheCubemap();
            m_CubeReflTexArray.AllocTextureArray(32, (int)m_TextureSettings.reflectionCubemapSize, TextureFormat.BC6H, true);

            // Init various light loop
            m_SinglePassLightLoop = new SinglePass.LightLoop();
            m_SinglePassLightLoop.Rebuild();
            m_TilePassLightLoop = new TilePass.LightLoop();
            m_TilePassLightLoop.Rebuild();

            m_lightList = new LightList();
            m_lightList.Allocate();
        }

        void OnDisable()
        {
            m_LitRenderLoop.OnDisable();
            m_SinglePassLightLoop.OnDisable();
            m_TilePassLightLoop.OnDisable();

            if (m_SkyboxMaterial) DestroyImmediate(m_SkyboxMaterial);
            if (m_SkyHDRIMaterial) DestroyImmediate(m_SkyHDRIMaterial);
            if (m_DeferredMaterial)  DestroyImmediate(m_DeferredMaterial);
            if (m_FinalPassMaterial) DestroyImmediate(m_FinalPassMaterial);
            if (m_DebugViewMaterialGBuffer) DestroyImmediate(m_DebugViewMaterialGBuffer);

            m_CubeReflTexArray.Release();
            m_CookieTexArray.Release();
            m_CubeCookieTexArray.Release();
        }

        void NewFrame()
        {
            m_CookieTexArray.NewFrame();
            m_CubeCookieTexArray.NewFrame();
            m_CubeReflTexArray.NewFrame();
        }

        void InitAndClearBuffer(Camera camera, RenderLoop renderLoop)
        {
            // We clear only the depth buffer, no need to clear the various color buffer as we overwrite them.
            // Clear depth/stencil and init buffers
            {
                var cmd = new CommandBuffer();
                cmd.name = "InitGBuffers and clear Depth/Stencil";

                // Init buffer
                // With scriptable render loop we must allocate ourself depth and color buffer (We must be independent of backbuffer for now, hope to fix that later).
                // Also we manage ourself the HDR format, here allocating fp16 directly.
                // With scriptable render loop we can allocate temporary RT in a command buffer, they will not be release with ExecuteCommandBuffer
                // These temporary surface are release automatically at the end of the scriptable renderloop if not release explicitly
                int w = camera.pixelWidth;
                int h = camera.pixelHeight;

                cmd.GetTemporaryRT(m_CameraColorBuffer, w, h, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                cmd.GetTemporaryRT(m_CameraDepthBuffer, w, h, 24, FilterMode.Point, RenderTextureFormat.Depth);
                if (!debugParameters.useForwardRenderingOnly)
                {
                    m_gbufferManager.InitGBuffers(w, h, cmd);
                }

                cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraColorBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
                cmd.ClearRenderTarget(true, false, new Color(0, 0, 0, 0));
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }


            // TEMP: As we are in development and have not all the setup pass we still clear the color in emissive buffer and gbuffer, but this will be removed later.

            // Clear HDR target
            {
                var cmd = new CommandBuffer();
                cmd.name = "Clear HDR target";
                cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraColorBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
                cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }


            // Clear GBuffers
            {
                var cmd = new CommandBuffer();
                cmd.name = "Clear GBuffer";
                // Write into the Camera Depth buffer
                cmd.SetRenderTarget(m_gbufferManager.GetGBuffers(cmd), new RenderTargetIdentifier(m_CameraDepthBuffer));
                // Clear everything
                // TODO: Clear is not required for color as we rewrite everything, will save performance.
                cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }

            // END TEMP
        }

        void RenderOpaqueNoLightingRenderList(CullResults cull, Camera camera, RenderLoop renderLoop, string passName)
        {
            if (!debugParameters.displayOpaqueObjects)
                return;

            var settings = new DrawRendererSettings(cull, camera, new ShaderPassName(passName))
            {
                rendererConfiguration = 0,
                sorting = { sortOptions = SortOptions.SortByMaterialThenMesh }
            };        
            settings.inputFilter.SetQueuesOpaque();
            renderLoop.DrawRenderers(ref settings);
        }

        void RenderOpaqueRenderList(CullResults cull, Camera camera, RenderLoop renderLoop, string passName)
        {
            if (!debugParameters.displayOpaqueObjects)
                return;

            var settings = new DrawRendererSettings(cull, camera, new ShaderPassName(passName))
            {
                rendererConfiguration = RendererConfiguration.PerObjectLightProbe | RendererConfiguration.PerObjectReflectionProbes | RendererConfiguration.PerObjectLightmaps | RendererConfiguration.PerObjectLightProbeProxyVolume,
                sorting = { sortOptions = SortOptions.SortByMaterialThenMesh }
            };
            settings.inputFilter.SetQueuesOpaque();
            renderLoop.DrawRenderers(ref settings);
        }

        void RenderTransparentNoLightingRenderList(CullResults cull, Camera camera, RenderLoop renderLoop, string passName)
        {
            if (!debugParameters.displayTransparentObjects)
                return;

            var settings = new DrawRendererSettings(cull, camera, new ShaderPassName(passName))
            {
                rendererConfiguration = 0,
                sorting = { sortOptions = SortOptions.BackToFront }
            };
            settings.inputFilter.SetQueuesTransparent();
            renderLoop.DrawRenderers(ref settings);
        }

        void RenderTransparentRenderList(CullResults cull, Camera camera, RenderLoop renderLoop, string passName)        
        {
            if (!debugParameters.displayTransparentObjects)
                return;

            var settings = new DrawRendererSettings(cull, camera, new ShaderPassName(passName))
            {
                rendererConfiguration = RendererConfiguration.PerObjectLightProbe | RendererConfiguration.PerObjectReflectionProbes | RendererConfiguration.PerObjectLightmaps | RendererConfiguration.PerObjectLightProbeProxyVolume,
                sorting = { sortOptions = SortOptions.BackToFront }
            };
            settings.inputFilter.SetQueuesTransparent();
            renderLoop.DrawRenderers(ref settings);
        }

        void RenderDepthPrepass(CullResults cull, Camera camera, RenderLoop renderLoop)
        {
            // If we are forward only we will do a depth prepass
            // TODO: Depth prepass should be enabled based on light loop settings. LightLoop define if they need a depth prepass + forward only...
            if (!debugParameters.useDepthPrepass)
                return;

              // TODO: Must do opaque then alpha masked for performance! 
            // TODO: front to back for opaque and by materal for opaque tested when we split in two
            var cmd = new CommandBuffer { name = "Depth Prepass" };
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            RenderOpaqueNoLightingRenderList(cull, camera, renderLoop, "DepthOnly");
        }

        void RenderGBuffer(CullResults cull, Camera camera, RenderLoop renderLoop)
        {
            if (debugParameters.useForwardRenderingOnly)
            {
                return ;
            }

            // setup GBuffer for rendering
            var cmd = new CommandBuffer { name = "GBuffer Pass" };
            cmd.SetRenderTarget(m_gbufferManager.GetGBuffers(cmd), new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            // render opaque objects into GBuffer
            RenderOpaqueRenderList(cull, camera, renderLoop, "GBuffer");
        }

        // This pass is use in case of forward opaque and deferred rendering. We need to render forward objects before tile lighting pass
        void RenderForwardOpaqueDepth(CullResults cull, Camera camera, RenderLoop renderLoop)
        {
            // If we have render a depth prepass, no need for this pass
            if (debugParameters.useDepthPrepass)
                return;

            // TODO: Use the render queue index to only send the forward opaque!
            var cmd = new CommandBuffer { name = "Depth Prepass" };
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            RenderOpaqueNoLightingRenderList(cull, camera, renderLoop, "DepthOnly");
        }

        void RenderDebugViewMaterial(CullResults cull, Camera camera, RenderLoop renderLoop)
        {
            // Render Opaque forward
            {
                var cmd = new CommandBuffer { name = "DebugView Material Mode Pass" };
                cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraColorBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();

                Shader.SetGlobalInt("_DebugViewMaterial", (int)debugParameters.debugViewMaterial);

                RenderOpaqueRenderList(cull, camera, renderLoop, "DebugViewMaterial");
            }

            // Render GBuffer opaque
            if (!debugParameters.useForwardRenderingOnly)
            {
                Vector4 screenSize = ComputeScreenSize(camera);
                m_DebugViewMaterialGBuffer.SetVector("_ScreenSize", screenSize);
                m_DebugViewMaterialGBuffer.SetFloat("_DebugViewMaterial", (float)debugParameters.debugViewMaterial);

                // m_gbufferManager.BindBuffers(m_DeferredMaterial);
                // TODO: Bind depth textures
                var cmd = new CommandBuffer { name = "GBuffer Debug Pass" };
                cmd.Blit(null, new RenderTargetIdentifier(m_CameraColorBuffer), m_DebugViewMaterialGBuffer, 0);
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }

            // Render forward transparent
            {
                RenderTransparentRenderList(cull, camera, renderLoop, "DebugViewMaterial");
            }

            // Last blit
            {
                var cmd = new CommandBuffer { name = "Blit DebugView Material Debug" };
                cmd.Blit(new RenderTargetIdentifier(m_CameraColorBuffer), BuiltinRenderTextureType.CameraTarget);
                renderLoop.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }
        }

        Matrix4x4 GetViewProjectionMatrix(Camera camera)
        {
            // The actual projection matrix used in shaders is actually massaged a bit to work across all platforms
            // (different Z value ranges etc.)
            var gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            var gpuVP = gpuProj * camera.worldToCameraMatrix;

            return gpuVP;
        }

        Vector4 ComputeScreenSize(Camera camera)
        {
            return new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);
        }

        void RenderDeferredLighting(Camera camera, RenderLoop renderLoop)
        {
            if (debugParameters.useForwardRenderingOnly)
            {
                return ;
            }

            // Bind material data
            m_LitRenderLoop.Bind();

            var invViewProj = GetViewProjectionMatrix(camera).inverse;
            m_DeferredMaterial.SetMatrix("_InvViewProjMatrix", invViewProj);

            var screenSize = ComputeScreenSize(camera);
            m_DeferredMaterial.SetVector("_ScreenSize", screenSize);

            // m_gbufferManager.BindBuffers(m_DeferredMaterial);
            // TODO: Bind depth textures
            var cmd = new CommandBuffer { name = "Deferred Ligthing Pass" };
            cmd.Blit(null, new RenderTargetIdentifier(m_CameraColorBuffer), m_DeferredMaterial, 0);
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        void RenderSky(Camera camera, RenderLoop renderLoop)
        {
            /*
            // Render sky into a cubemap - doesn't happen every frame, can be control

            // TODO: do a render to texture here

            // Downsample the cubemap and provide it to Enlighten

            // TODO: currently workaround is to set the cubemap in a Skybox/cubemap material
            //m_SkyboxMaterial.SetTexture(cubemap);

            // Render the sky itself

            Vector3[] vertData = new Vector3[4];
            vertData[0] = new Vector3(-1.0f, -1.0f, 0.0f);
            vertData[1] = new Vector3(1.0f, -1.0f, 0.0f);
            vertData[2] = new Vector3(1.0f, 1.0f, 0.0f);
            vertData[3] = new Vector3(-1.0f, 1.0f, 0.0f);            

            Vector3[] eyeVectorData = new Vector3[4];
            // camera.worldToCameraMatrix, camera.projectionMatrix
            // Get view vector vased on the frustrum, i.e (invert transform frustrum get position etc...)
            eyeVectorData[0] = 
            eyeVectorData[1] = 
            eyeVectorData[2] = 
            eyeVectorData[3] = 

            // Write out the mesh
            var triangles = new int[4];
            for (int i = 0; i < 4; i++)
            {
                triangles[i] = i;
            }

            Mesh mesh = new Mesh
            {
                vertices = vertData,
                normals = eyeVectorData,
                triangles = triangles
            };

            m_SkyHDRIMaterial.SetTexture("_Cubemap", skyParameters.skyHDRI);
            m_SkyHDRIMaterial.SetVector("_SkyParam", new Vector4(skyParameters.exposure, skyParameters.multiplier, skyParameters.rotation, 0.0f));

            var cmd = new CommandBuffer { name = "Skybox" };
            cmd.DrawMesh(mesh, Matrix4x4.identity, m_SkyHDRIMaterial);
            renderloop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
            */
        }

        void RenderForward(CullResults cullResults, Camera camera, RenderLoop renderLoop)
        {
            // Bind material data
            m_LitRenderLoop.Bind();

            var cmd = new CommandBuffer { name = "Forward Pass" };
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraColorBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            if (debugParameters.useForwardRenderingOnly)
            {
                RenderOpaqueRenderList(cullResults, camera, renderLoop, "Forward");
            }

            RenderTransparentRenderList(cullResults, camera, renderLoop, "Forward");
        }

        void RenderForwardUnlit(CullResults cullResults, Camera camera, RenderLoop renderLoop)
        {
            // Bind material data
            m_LitRenderLoop.Bind();

            var cmd = new CommandBuffer { name = "Forward Unlit Pass" };
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_CameraColorBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            RenderOpaqueNoLightingRenderList(cullResults, camera, renderLoop, "ForwardUnlit");
            RenderTransparentNoLightingRenderList(cullResults, camera, renderLoop, "ForwardUnlit");
        }

        void RenderVelocity(CullResults cullResults, Camera camera, RenderLoop renderLoop)
        {
            // warning CS0162: Unreachable code detected // warning CS0429: Unreachable expression code detected
#pragma warning disable 162, 429
            // If opaque velocity have been render during GBuffer no need to render it here
            if ((ShaderConfig.VelocityInGbuffer == 0) || debugParameters.useForwardRenderingOnly)
                return ;

            int w = camera.pixelWidth;
            int h = camera.pixelHeight;

            var cmd = new CommandBuffer { name = "Velocity Pass" };
            cmd.GetTemporaryRT(m_VelocityBuffer, w, h, 0, FilterMode.Point, Builtin.RenderLoop.GetVelocityBufferFormat(), Builtin.RenderLoop.GetVelocityBufferReadWrite());
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_VelocityBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            RenderOpaqueNoLightingRenderList(cullResults, camera, renderLoop, "MotionVectors");
#pragma warning restore 162, 429
        }

        void RenderDistortion(CullResults cullResults, Camera camera, RenderLoop renderLoop)
        {
            int w = camera.pixelWidth;
            int h = camera.pixelHeight;

            var cmd = new CommandBuffer { name = "Distortion Pass" };
            cmd.GetTemporaryRT(m_DistortionBuffer, w, h, 0, FilterMode.Point, Builtin.RenderLoop.GetDistortionBufferFormat(), Builtin.RenderLoop.GetDistortionBufferReadWrite());
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_DistortionBuffer), new RenderTargetIdentifier(m_CameraDepthBuffer));
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            // Only transparent object can render distortion vectors
            RenderTransparentNoLightingRenderList(cullResults, camera, renderLoop, "DistortionVectors");
        }


        void FinalPass(RenderLoop renderLoop)
        {
            // Those could be tweakable for the neutral tonemapper, but in the case of the LookDev we don't need that
            const float blackIn = 0.02f;
            const float whiteIn = 10.0f;
            const float blackOut = 0.0f;
            const float whiteOut = 10.0f;
            const float whiteLevel = 5.3f;
            const float whiteClip = 10.0f;
            const float dialUnits = 20.0f;
            const float halfDialUnits = dialUnits * 0.5f;

            // converting from artist dial units to easy shader-lerps (0-1)
            var tonemapCoeff1 = new Vector4((blackIn * dialUnits) + 1.0f, (blackOut * halfDialUnits) + 1.0f, (whiteIn / dialUnits), (1.0f - (whiteOut / dialUnits)));
            var tonemapCoeff2 = new Vector4(0.0f, 0.0f, whiteLevel, whiteClip / halfDialUnits);

            m_FinalPassMaterial.SetVector("_ToneMapCoeffs1", tonemapCoeff1);
            m_FinalPassMaterial.SetVector("_ToneMapCoeffs2", tonemapCoeff2);

            m_FinalPassMaterial.SetFloat("_EnableToneMap", debugParameters.enableTonemap ? 1.0f : 0.0f);
            m_FinalPassMaterial.SetFloat("_Exposure", debugParameters.exposure);

            var cmd = new CommandBuffer { name = "FinalPass" };

            // Resolve our HDR texture to CameraTarget.
            cmd.Blit(new RenderTargetIdentifier(m_CameraColorBuffer), BuiltinRenderTextureType.CameraTarget, m_FinalPassMaterial, 0);
            renderLoop.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        // Function to prepare light structure for GPU lighting
        void PrepareLightsForGPU(CullResults cullResults, Camera camera, ref ShadowOutput shadowOutput, ref LightList lightList)
        {
            lightList.Clear();

            for (int lightIndex = 0, numLights = cullResults.visibleLights.Length; lightIndex < numLights; ++lightIndex)
            {
                var light = cullResults.visibleLights[lightIndex];

                // We only process light with additional data
                var additionalData = light.light.GetComponent<AdditionalLightData>();

                if (additionalData == null)
                {
                    Debug.LogWarning("Light entity detected without additional data, will not be taken into account " + light.light.name);
                    continue;
                }

                // Linear intensity calculation (different Unity 5.5)
                var lightColorR = light.light.intensity * Mathf.GammaToLinearSpace(light.light.color.r);
                var lightColorG = light.light.intensity * Mathf.GammaToLinearSpace(light.light.color.g);
                var lightColorB = light.light.intensity * Mathf.GammaToLinearSpace(light.light.color.b);

                if (light.lightType == LightType.Directional)
                {
                    if (lightList.directionalLights.Count >= k_MaxDirectionalLightsOnSCreen)
                        continue;

                    var directionalLightData = new DirectionalLightData();
                    // Light direction for directional and is opposite to the forward direction
                    directionalLightData.direction = -light.light.transform.forward;
                    directionalLightData.color = new Vector3(lightColorR, lightColorG, lightColorB);
                    directionalLightData.diffuseScale = additionalData.affectDiffuse ? 1.0f : 0.0f;
                    directionalLightData.specularScale = additionalData.affectSpecular ? 1.0f : 0.0f;
                    directionalLightData.cosAngle = 0.0f;
                    directionalLightData.sinAngle = 0.0f;
                    directionalLightData.shadowIndex = -1;

                    bool hasDirectionalShadows = light.light.shadows != LightShadows.None && shadowOutput.GetShadowSliceCountLightIndex(lightIndex) != 0;
                    bool hasDirectionalNotReachMaxLimit = lightList.directionalShadows.Count == 0; // Only one cascade shadow allowed

                    if (hasDirectionalShadows && hasDirectionalNotReachMaxLimit) // Note  < MaxShadows should be check at shadowOutput creation
                    {
                        // When we have a point light, we assumed that there is 6 consecutive PunctualShadowData
                        directionalLightData.shadowIndex = 0;

                        for (int sliceIndex = 0; sliceIndex < shadowOutput.GetShadowSliceCountLightIndex(lightIndex); ++sliceIndex)
                        {
                            DirectionalShadowData directionalShadowData = new DirectionalShadowData();

                            int shadowSliceIndex = shadowOutput.GetShadowSliceIndex(lightIndex, sliceIndex);
                            directionalShadowData.worldToShadow = shadowOutput.shadowSlices[shadowSliceIndex].shadowTransform.transpose; // Transpose for hlsl reading ?

                            directionalShadowData.bias = light.light.shadowBias;

                            lightList.directionalShadows.Add(directionalShadowData);
                        }

                        // Fill split information for shaders
                        for (int s = 0; s < k_MaxCascadeCount; ++s)
                        {
                            lightList.directionalShadowSplitSphereSqr[s] = shadowOutput.directionalShadowSplitSphereSqr[s];
                        }
                    }

                    lightList.directionalLights.Add(directionalLightData);
                    lightList.directionalCullIndices.Add(lightIndex);

                    continue;
                }

                // Note: LightType.Area is offline only, use for baking, no need to test it
                var lightData = new LightData();

                // Test whether we should treat this punctual light as an area light.
                // It's a temporary hack until the proper UI support is added.
                if (additionalData.archetype != LightArchetype.Punctual)
                {
                    // Early out if we reach the maximum
                    if (lightList.areaLights.Count >= k_MaxAreaLightsOnSCreen)
                        continue;

                    if (additionalData.archetype == LightArchetype.Rectangle)
                    {
                        lightData.lightType = GPULightType.Rectangle;
                    }
                    else
                    {
                        lightData.lightType = GPULightType.Line;
                    }
                }
                else
                {
                    if (lightList.punctualLights.Count >= k_MaxPunctualLightsOnSCreen)
                        continue;

                    switch (light.lightType)
                    {
                        case LightType.Directional: lightData.lightType = GPULightType.Directional; break;
                        case LightType.Spot: lightData.lightType = GPULightType.Spot; break;
                        case LightType.Point: lightData.lightType = GPULightType.Point; break;
                    }
                }

                lightData.positionWS = light.light.transform.position;
                lightData.invSqrAttenuationRadius = 1.0f / (light.range * light.range);

                lightData.color = new Vector3(lightColorR, lightColorG, lightColorB);
                
                lightData.forward = light.light.transform.forward; // Note: Light direction is oriented backward (-Z)
                lightData.up = light.light.transform.up;
                lightData.right = light.light.transform.right;

                if (lightData.lightType == GPULightType.Spot)
                {
                    var spotAngle = light.spotAngle;

                    var innerConePercent = additionalData.GetInnerSpotPercent01();
                    var cosSpotOuterHalfAngle = Mathf.Clamp(Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad), 0.0f, 1.0f);
                    var cosSpotInnerHalfAngle = Mathf.Clamp(Mathf.Cos(spotAngle * 0.5f * innerConePercent * Mathf.Deg2Rad), 0.0f, 1.0f); // inner cone

                    var val = Mathf.Max(0.001f, (cosSpotInnerHalfAngle - cosSpotOuterHalfAngle));
                    lightData.angleScale = 1.0f / val;
                    lightData.angleOffset = -cosSpotOuterHalfAngle * lightData.angleScale;
                }
                else
                {
                    // 1.0f, 2.0f are neutral value allowing GetAngleAnttenuation in shader code to return 1.0
                    lightData.angleScale = 1.0f;
                    lightData.angleOffset = 2.0f;
                }

                lightData.diffuseScale = additionalData.affectDiffuse ? 1.0f : 0.0f;
                lightData.specularScale = additionalData.affectSpecular ? 1.0f : 0.0f;
                lightData.shadowDimmer = additionalData.shadowDimmer;

                lightData.IESIndex = -1;
                lightData.cookieIndex = -1;
                lightData.shadowIndex = -1;

                bool hasCookie = light.light.cookie != null;
                if (hasCookie)
                {
                    if (light.lightType == LightType.Point)
                    {
                        lightData.cookieIndex = m_CubeCookieTexArray.FetchSlice(light.light.cookie);
                    }
                    else if (light.lightType == LightType.Spot)
                    {
                        lightData.cookieIndex = m_CookieTexArray.FetchSlice(light.light.cookie);
                    }
                }

                // Setup shadow data arrays
                bool hasShadows = light.light.shadows != LightShadows.None && shadowOutput.GetShadowSliceCountLightIndex(lightIndex) != 0;
                bool hasNotReachMaxLimit = lightList.punctualShadows.Count + (lightData.lightType == GPULightType.Point ? 6 : 1) <= k_MaxShadowOnScreen;

                if (hasShadows && hasNotReachMaxLimit) // Note  < MaxShadows should be check at shadowOutput creation
                {
                    // When we have a point light, we assumed that there is 6 consecutive PunctualShadowData
                    lightData.shadowIndex = lightList.punctualShadows.Count;

                    for (int sliceIndex = 0; sliceIndex < shadowOutput.GetShadowSliceCountLightIndex(lightIndex); ++sliceIndex)
                    {
                        PunctualShadowData punctualShadowData = new PunctualShadowData();

                        int shadowSliceIndex = shadowOutput.GetShadowSliceIndex(lightIndex, sliceIndex);
                        punctualShadowData.worldToShadow = shadowOutput.shadowSlices[shadowSliceIndex].shadowTransform.transpose; // Transpose for hlsl reading ?
                        punctualShadowData.lightType = lightData.lightType;

                        punctualShadowData.bias = light.light.shadowBias;

                        lightList.punctualShadows.Add(punctualShadowData);
                    }
                }

                lightData.size = new Vector2(additionalData.areaLightLength, additionalData.areaLightWidth);
                lightData.twoSided = additionalData.isDoubleSided;

                if (additionalData.archetype == LightArchetype.Punctual)
                {
                    lightList.punctualLights.Add(lightData);
                    lightList.punctualCullIndices.Add(lightIndex);
                }
                else
                {
                    // Area and line lights are both currently stored as area lights on the GPU.
                    lightList.areaLights.Add(lightData);
                    lightList.areaCullIndices.Add(lightIndex);
                }
            }

            for (int probeIndex = 0, numProbes = cullResults.visibleReflectionProbes.Length; probeIndex < numProbes; probeIndex++)
            {
                var probe = cullResults.visibleReflectionProbes[probeIndex];

                // If probe have not been rendered discard
                if (probe.texture == null)
                    continue;

                if (lightList.envLights.Count >= k_MaxEnvLightsOnSCreen)
                    continue;

                var envLightData = new EnvLightData();

                // CAUTION: localToWorld is the transform for the widget of the reflection probe. i.e the world position of the point use to do the cubemap capture (mean it include the local offset)
                envLightData.positionWS = probe.localToWorld.GetColumn(3);

                envLightData.envShapeType = EnvShapeType.None;

                // TODO: Support sphere in the interface
                if (probe.boxProjection != 0)
                {
                    envLightData.envShapeType = EnvShapeType.Box;
                }

                // remove scale from the matrix (Scale in this matrix is use to scale the widget)
                envLightData.right = probe.localToWorld.GetColumn(0);
                envLightData.right.Normalize();
                envLightData.up = probe.localToWorld.GetColumn(1);
                envLightData.up.Normalize();
                envLightData.forward = probe.localToWorld.GetColumn(2);
                envLightData.forward.Normalize();

                // Artists prefer to have blend distance inside the volume!
                // So we let the current UI but we assume blendDistance is an inside factor instead
                // Blend distance can't be larger than the max radius
                // probe.bounds.extents is BoxSize / 2
                float maxBlendDist = Mathf.Min(probe.bounds.extents.x, Mathf.Min(probe.bounds.extents.y, probe.bounds.extents.z));
                float blendDistance = Mathf.Min(maxBlendDist, probe.blendDistance);
                envLightData.innerDistance = probe.bounds.extents - new Vector3(blendDistance, blendDistance, blendDistance);

                envLightData.envIndex = m_CubeReflTexArray.FetchSlice(probe.texture);

                envLightData.offsetLS = probe.center; // center is misnamed, it is the offset (in local space) from center of the bounding box to the cubemap capture point
                envLightData.blendDistance = blendDistance;
                lightList.envLights.Add(envLightData);
                lightList.envCullIndices.Add(probeIndex);
            }

            // build per tile light lists           
            m_SinglePassLightLoop.PrepareLightsForGPU(cullResults, camera, m_lightList);
            m_TilePassLightLoop.PrepareLightsForGPU(cullResults, camera, m_lightList);
        }

        void Resize(Camera camera)
        {
            if (camera.pixelWidth != m_WidthOnRecord || camera.pixelHeight != m_HeightOnRecord || m_TilePassLightLoop.NeedResize())
            {
                if (m_WidthOnRecord > 0 && m_HeightOnRecord > 0)
                {
                    m_TilePassLightLoop.ReleaseResolutionDependentBuffers();
                }

                m_TilePassLightLoop.AllocResolutionDependentBuffers(camera.pixelWidth, camera.pixelHeight);

                // update recorded window resolution
                m_WidthOnRecord = camera.pixelWidth;
                m_HeightOnRecord = camera.pixelHeight;
            }
        }

        public void PushGlobalParams(Camera camera, RenderLoop renderLoop, HDRenderLoop.LightList lightList)
        {
            //Shader.SetGlobalTexture("_CookieTextures", m_CookieTexArray.GetTexCache());
            //Shader.SetGlobalTexture("_CubeCookieTextures", m_CubeCookieTexArray.GetTexCache());
            Shader.SetGlobalTexture("_EnvTextures", m_CubeReflTexArray.GetTexCache());

            m_SinglePassLightLoop.PushGlobalParams(camera, renderLoop, lightList);
            m_TilePassLightLoop.PushGlobalParams(camera, renderLoop, lightList);
        }

        public override void Render(Camera[] cameras, RenderLoop renderLoop)
        {
            if (!m_LitRenderLoop.isInit)
            {
                m_LitRenderLoop.RenderInit(renderLoop);
            }

            // Do anything we need to do upon a new frame.
            NewFrame();

            // Set Frame constant buffer
            // TODO...

            foreach (var camera in cameras)
            {
                // Set camera constant buffer
                // TODO...

                CullingParameters cullingParams;
                if (!CullResults.GetCullingParameters(camera, out cullingParams))
                    continue;

                m_ShadowPass.UpdateCullingParameters(ref cullingParams);

                var cullResults = CullResults.Cull(ref cullingParams, renderLoop);

                Resize(camera);

                renderLoop.SetupCameraProperties(camera);

                InitAndClearBuffer(camera, renderLoop);

                RenderDepthPrepass(cullResults, camera, renderLoop);

                RenderGBuffer(cullResults, camera, renderLoop); 

                // For tile lighting with forward opaque
                //RenderForwardOpaqueDepth(cullResults, camera, renderLoop);

                if (debugParameters.debugViewMaterial != 0)
                {
                    RenderDebugViewMaterial(cullResults, camera, renderLoop);
                }
                else
                {
                    ShadowOutput shadows;
                    m_ShadowPass.Render(renderLoop, cullResults, out shadows);

                    renderLoop.SetupCameraProperties(camera); // Need to recall SetupCameraProperties after m_ShadowPass.Render

                    PrepareLightsForGPU(cullResults, camera, ref shadows, ref m_lightList);
                    m_TilePassLightLoop.BuildGPULightLists(camera, renderLoop, m_lightList, m_CameraDepthBuffer);

                    PushGlobalParams(camera, renderLoop, m_lightList);

                    

                    RenderDeferredLighting(camera, renderLoop);

                    RenderSky(camera, renderLoop);

                    RenderForward(cullResults, camera, renderLoop); // Note: We want to render forward opaque before RenderSky, then RenderTransparent - can only do that once we have material.SetPass feature...
                    RenderForwardUnlit(cullResults, camera, renderLoop);

                    RenderVelocity(cullResults, camera, renderLoop); // Note we may have to render velocity earlier if we do temporalAO, temporal volumetric etc... Mean we will not take into account forward opaque in case of deferred rendering ? 

                    // TODO: Check with VFX team.
                    // Rendering distortion here have off course lot of artifact.
                    // But resolving at each objects that write in distortion is not possible (need to sort transparent, render those that do not distort, then resolve, then etc...)
                    // Instead we chose to apply distortion at the end after we cumulate distortion vector and desired blurriness. This
                    // RenderDistortion(cullResults, camera, renderLoop);

                    FinalPass(renderLoop);
                }

                renderLoop.Submit();
            }

            // Post effects
        }

#if UNITY_EDITOR
        public override UnityEditor.SupportedRenderingFeatures GetSupportedRenderingFeatures()
        {
            var features = new UnityEditor.SupportedRenderingFeatures
            {
                reflectionProbe = UnityEditor.SupportedRenderingFeatures.ReflectionProbe.Rotation
            };

            return features;
        }
#endif
    }
}
