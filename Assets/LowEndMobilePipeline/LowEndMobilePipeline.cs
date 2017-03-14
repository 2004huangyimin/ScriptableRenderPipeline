﻿using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

using UnityEngine.VR;
using UnityEditor;

namespace UnityEngine.Experimental.Rendering.LowendMobile
{
    public class LowEndMobilePipeline : RenderPipeline
    {
        private readonly LowEndMobilePipelineAsset m_Asset;

        private static readonly int kMaxCascades = 4;
        private static readonly int kMaxLights = 8;
        private int m_ShadowMapProperty;
        private RenderTargetIdentifier m_ShadowMapRTID;
        private int m_DepthBufferBits = 24;
        private Vector4[] m_DirectionalShadowSplitDistances = new Vector4[kMaxCascades];

        private static readonly ShaderPassName m_ForwardBasePassName = new ShaderPassName("LowEndMobileForward");

        private Vector4[] m_LightPositions = new Vector4[kMaxLights];
        private Vector4[] m_LightColors = new Vector4[kMaxLights];
        private Vector4[] m_LightAttenuations = new Vector4[kMaxLights];
        private Vector4[] m_LightSpotDirections = new Vector4[kMaxLights];

        private ShadowSettings m_ShadowSettings = ShadowSettings.Default;
        private ShadowSliceData[] m_ShadowSlices = new ShadowSliceData[kMaxCascades];

        public LowEndMobilePipeline(LowEndMobilePipelineAsset asset)
        {
            m_Asset = asset;

            BuildShadowSettings();
            m_ShadowMapProperty = Shader.PropertyToID("_ShadowMap");
            m_ShadowMapRTID = new RenderTargetIdentifier(m_ShadowMapProperty);
        }

        public override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            var prevPipe = Shader.globalRenderPipeline;
            Shader.globalRenderPipeline = "LowEndMobilePipeline";
            base.Render(context, cameras);

            foreach (Camera camera in cameras)
            {

                CullingParameters cullingParameters;
                if (!CullResults.GetCullingParameters(camera, out cullingParameters))
                    continue;

                cullingParameters.shadowDistance = m_ShadowSettings.maxShadowDistance;
                CullResults cull = CullResults.Cull(ref cullingParameters, context);

                // Render Shadow Map
                bool shadowsRendered = RenderShadows(cull, context);

                // Draw Opaques with support to one directional shadow cascade

                // Setup camera matrices
                //context.SetupCameraProperties(camera);
                if (VRSettings.isDeviceActive)
                {
                    //camera.stereoTargetEye = StereoTargetEyeMask.Left;
                    context.StereoSetupCameraProperties(camera);
                    context.StartMultiEye(camera);
                }
                else
                {
                    context.SetupCameraProperties(camera);
                }

                // set up a temporary RT to render to
                var intermediateRT = Shader.PropertyToID("_IntermediateTarget");
                var intermediateRTID = new RenderTargetIdentifier(intermediateRT);
                var intermediateDepthRT = Shader.PropertyToID("_IntermediateDepthTarget");
                var intermediateDepthRTID = new RenderTargetIdentifier(intermediateDepthRT);

                if (VRSettings.isDeviceActive)
                {
                    int w = VRSettings.eyeTextureWidth;
                    int h = VRSettings.eyeTextureHeight;

                    var aa = QualitySettings.antiAliasing;
                    if (aa < 1)
                        aa = 1;

                    var bindTempRTCmd = new CommandBuffer() { name = "Bind intermediate RT" };

                    // TODO: this won't work in...the Player
                    var stereoPath = PlayerSettings.stereoRenderingPath;
                    if (StereoRenderingPath.Instancing == stereoPath) // can't actually check for GetGraphicsCaps().hasRenderTargetArrayIndexFromAnyShader...yet
                    {
                        bindTempRTCmd.GetTemporaryRTArray(intermediateRT, w, h, 2, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, aa, true);
                        bindTempRTCmd.GetTemporaryRTArray(intermediateDepthRT, w, h, 2, 24, FilterMode.Point, RenderTextureFormat.Depth);
                    }
                    else
                    {
                        bindTempRTCmd.GetTemporaryRT(intermediateRT, w, h, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, aa, true);
                        bindTempRTCmd.GetTemporaryRT(intermediateDepthRT, w, h, 24, FilterMode.Point, RenderTextureFormat.Depth);
                    }

                    bindTempRTCmd.SetRenderTarget(intermediateRTID, intermediateDepthRTID);
                    context.ExecuteCommandBuffer(bindTempRTCmd);
                    bindTempRTCmd.Dispose();
                }
                else
                {
                    int w = camera.pixelWidth;
                    int h = camera.pixelHeight;

                    var aa = QualitySettings.antiAliasing;
                    if (aa < 1)
                        aa = 1;
                    
                    var bindTempRTCmd = new CommandBuffer() { name = "Bind intermediate RT" };
                    
                    // this does the combined color/depth RT
                    bindTempRTCmd.GetTemporaryRT(intermediateRT, w, h, 24, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, aa, true);
                    bindTempRTCmd.SetRenderTarget(intermediateRTID);
                    //bindTempRTCmd.GetTemporaryRT(intermediateDepthRT, w, h, 24, FilterMode.Point, RenderTextureFormat.Depth);
                    //bindTempRTCmd.SetRenderTarget(intermediateRTID, intermediateDepthRTID);
                    context.ExecuteCommandBuffer(bindTempRTCmd);
                    bindTempRTCmd.Dispose();
                }

                var cmd = new CommandBuffer() { name = "Clear" };
                cmd.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(cmd);
                cmd.Dispose();

                // Setup light and shadow shader constants
                SetupLightShaderVariables(cull.visibleLights, context);
                if (shadowsRendered)
                    SetupShadowShaderVariables(context, camera.nearClipPlane, cullingParameters.shadowDistance,
                        m_ShadowSettings.directionalLightCascadeCount);

                // Render Opaques
                var settings = new DrawRendererSettings(cull, camera, m_ForwardBasePassName);
                settings.sorting.flags = SortFlags.CommonOpaque;
                settings.inputFilter.SetQueuesOpaque();

                if (m_Asset.EnableLightmap)
                    settings.rendererConfiguration |= RendererConfiguration.PerObjectLightmaps;

                if (m_Asset.EnableAmbientProbe)
                    settings.rendererConfiguration |= RendererConfiguration.PerObjectLightProbe;

                context.DrawRenderers(ref settings);

                var discardRT = new CommandBuffer();
                discardRT.ReleaseTemporaryRT(m_ShadowMapProperty);
                context.ExecuteCommandBuffer(discardRT);
                discardRT.Dispose();

                // TODO: Check skybox shader
                context.DrawSkybox(camera);

                // Render Alpha blended
                settings.sorting.flags = SortFlags.CommonTransparent;
                settings.inputFilter.SetQueuesTransparent();
                context.DrawRenderers(ref settings);

                // ok, copy from temporary RT into the real RT
                if (VRSettings.isDeviceActive)
                {
                    //context.StereoSetupCameraProperties(camera);

                    var copyIntermediateRTToDefault = new CommandBuffer() { name = "Copy intermediate RT to default RT" };
                    //copyIntermediateRTToDefault.Blit(intermediateRTID, BuiltinRenderTextureType.CurrentActive);
                    copyIntermediateRTToDefault.Blit(intermediateRTID, BuiltinRenderTextureType.CameraTarget);
                    context.ExecuteCommandBuffer(copyIntermediateRTToDefault);
                    copyIntermediateRTToDefault.Dispose();
                }
                else
                {
                    //context.SetupCameraProperties(camera);

                    var copyIntermediateRTToDefault = new CommandBuffer() { name = "Copy intermediate RT to default RT" };
                    copyIntermediateRTToDefault.Blit(intermediateRTID, BuiltinRenderTextureType.CameraTarget); // this works, but barely
                    //copyIntermediateRTToDefault.Blit(intermediateRTID, camera.targetTexture); // this won't work, target texture won't be right until SetupCameraProperties ACTUALLY executes
                    //copyIntermediateRTToDefault.Blit(intermediateRTID, BuiltinRenderTextureType.CurrentActive);
                    context.ExecuteCommandBuffer(copyIntermediateRTToDefault);
                    copyIntermediateRTToDefault.Dispose();
                }

                if (VRSettings.isDeviceActive)
                {
                    context.StopMultiEye(camera);
                    context.StereoEndRender(camera);
                }
            }

            context.Submit();
            Shader.globalRenderPipeline = prevPipe;
        }

        private void BuildShadowSettings()
        {
            m_ShadowSettings = ShadowSettings.Default;
            m_ShadowSettings.directionalLightCascadeCount = m_Asset.CascadeCount;

            m_ShadowSettings.shadowAtlasWidth = m_Asset.ShadowAtlasResolution;
            m_ShadowSettings.shadowAtlasHeight = m_Asset.ShadowAtlasResolution;
            m_ShadowSettings.maxShadowDistance = m_Asset.ShadowDistance;

            switch (m_ShadowSettings.directionalLightCascadeCount)
            {
                case 1:
                    m_ShadowSettings.directionalLightCascades = new Vector3(1.0f, 0.0f, 0.0f);
                    break;

                case 2:
                    m_ShadowSettings.directionalLightCascades = new Vector3(m_Asset.Cascade2Split, 1.0f, 0.0f);
                    break;

                default:
                    m_ShadowSettings.directionalLightCascades = m_Asset.Cascade4Split;
                    break;
            }
        }

        #region HelperMethods

        private void SetupLightShaderVariables(VisibleLight[] lights, ScriptableRenderContext context)
        {
            if (lights.Length <= 0)
                return;

            int pixelLightCount = Mathf.Min(lights.Length, m_Asset.MaxSupportedPixelLights);
            int vertexLightCount = (m_Asset.SupportsVertexLight)
                ? Mathf.Min(lights.Length - pixelLightCount, kMaxLights)
                : 0;
            int totalLightCount = Mathf.Min(pixelLightCount + vertexLightCount, kMaxLights);

            for (int i = 0; i < totalLightCount; ++i)
            {
                VisibleLight currLight = lights[i];
                if (currLight.lightType == LightType.Directional)
                {
                    Vector4 dir = -currLight.localToWorld.GetColumn(2);
                    m_LightPositions[i] = new Vector4(dir.x, dir.y, dir.z, 0.0f);
                }
                else
                {
                    Vector4 pos = currLight.localToWorld.GetColumn(3);
                    m_LightPositions[i] = new Vector4(pos.x, pos.y, pos.z, 1.0f);
                }

                m_LightColors[i] = currLight.finalColor;

                float rangeSq = currLight.range*currLight.range;
                float quadAtten = (currLight.lightType == LightType.Directional) ? 0.0f : 25.0f/rangeSq;

                if (currLight.lightType == LightType.Spot)
                {
                    Vector4 dir = currLight.localToWorld.GetColumn(2);
                    m_LightSpotDirections[i] = new Vector4(-dir.x, -dir.y, -dir.z, 0.0f);

                    float spotAngle = Mathf.Deg2Rad*currLight.spotAngle;
                    float cosOuterAngle = Mathf.Cos(spotAngle*0.5f);
                    float cosInneAngle = Mathf.Cos(spotAngle*0.25f);
                    float angleRange = cosInneAngle - cosOuterAngle;
                    m_LightAttenuations[i] = new Vector4(cosOuterAngle,
                        Mathf.Approximately(angleRange, 0.0f) ? 1.0f : angleRange, quadAtten, rangeSq);
                }
                else
                {
                    m_LightSpotDirections[i] = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
                    m_LightAttenuations[i] = new Vector4(-1.0f, 1.0f, quadAtten, rangeSq);
                }
            }

            CommandBuffer cmd = new CommandBuffer() {name = "SetupShadowShaderConstants"};
            cmd.SetGlobalVectorArray("globalLightPos", m_LightPositions);
            cmd.SetGlobalVectorArray("globalLightColor", m_LightColors);
            cmd.SetGlobalVectorArray("globalLightAtten", m_LightAttenuations);
            cmd.SetGlobalVectorArray("globalLightSpotDir", m_LightSpotDirections);
            cmd.SetGlobalVector("globalLightCount", new Vector4(pixelLightCount, totalLightCount, 0.0f, 0.0f));
            SetShadowKeywords(cmd);
            context.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        private bool RenderShadows(CullResults cullResults, ScriptableRenderContext context)
        {
            int cascadeCount = m_ShadowSettings.directionalLightCascadeCount;

            VisibleLight[] lights = cullResults.visibleLights;
            int lightCount = lights.Length;

            int shadowResolution = 0;
            int lightIndex = -1;
            for (int i = 0; i < lightCount; ++i)
            {
                if (lights[i].light.shadows != LightShadows.None && lights[i].lightType == LightType.Directional)
                {
                    lightIndex = i;
                    shadowResolution = GetMaxTileResolutionInAtlas(m_ShadowSettings.shadowAtlasWidth,
                        m_ShadowSettings.shadowAtlasHeight, cascadeCount);
                    break;
                }
            }

            if (lightIndex < 0)
                return false;

            Bounds bounds;
            if (!cullResults.GetShadowCasterBounds(lightIndex, out bounds))
                return false;

            var setRenderTargetCommandBuffer = new CommandBuffer();
            setRenderTargetCommandBuffer.name = "Render packed shadows";
            setRenderTargetCommandBuffer.GetTemporaryRT(m_ShadowMapProperty, m_ShadowSettings.shadowAtlasWidth,
                m_ShadowSettings.shadowAtlasHeight, m_DepthBufferBits, FilterMode.Bilinear, RenderTextureFormat.Depth,
                RenderTextureReadWrite.Linear);
            setRenderTargetCommandBuffer.SetRenderTarget(m_ShadowMapRTID);
            setRenderTargetCommandBuffer.ClearRenderTarget(true, true, Color.black);
            context.ExecuteCommandBuffer(setRenderTargetCommandBuffer);
            setRenderTargetCommandBuffer.Dispose();

            float shadowNearPlane = m_Asset.ShadowNearOffset;
            Vector3 splitRatio = m_ShadowSettings.directionalLightCascades;
            Vector3 lightDir = lights[lightIndex].light.transform.forward;
            for (int cascadeIdx = 0; cascadeIdx < cascadeCount; ++cascadeIdx)
            {
                Matrix4x4 view, proj;
                var settings = new DrawShadowsSettings(cullResults, lightIndex);
                bool needRendering = cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(lightIndex,
                    cascadeIdx, cascadeCount, splitRatio, shadowResolution, shadowNearPlane, out view, out proj,
                    out settings.splitData);

                m_DirectionalShadowSplitDistances[cascadeIdx] = settings.splitData.cullingSphere;
                m_DirectionalShadowSplitDistances[cascadeIdx].w *= settings.splitData.cullingSphere.w;

                if (needRendering)
                {
                    SetupShadowSliceTransform(cascadeIdx, shadowResolution, proj, view);
                    RenderShadowSlice(ref context, lightDir, cascadeIdx, proj, view, settings);
                }
            }

            return true;
        }

        private void SetupShadowSliceTransform(int cascadeIndex, int shadowResolution, Matrix4x4 proj, Matrix4x4 view)
        {
            // Assumes MAX_CASCADES = 4
            m_ShadowSlices[cascadeIndex].atlasX = (cascadeIndex%2)*shadowResolution;
            m_ShadowSlices[cascadeIndex].atlasY = (cascadeIndex/2)*shadowResolution;
            m_ShadowSlices[cascadeIndex].shadowResolution = shadowResolution;
            m_ShadowSlices[cascadeIndex].shadowTransform = Matrix4x4.identity;

            var matScaleBias = Matrix4x4.identity;
            matScaleBias.m00 = 0.5f;
            matScaleBias.m11 = 0.5f;
            matScaleBias.m22 = 0.5f;
            matScaleBias.m03 = 0.5f;
            matScaleBias.m23 = 0.5f;
            matScaleBias.m13 = 0.5f;

            // Later down the pipeline the proj matrix will be scaled to reverse-z in case of DX.
            // We need account for that scale in the shadowTransform.
            if (SystemInfo.usesReversedZBuffer)
                matScaleBias.m22 = -0.5f;

            var matTile = Matrix4x4.identity;
            matTile.m00 = (float) m_ShadowSlices[cascadeIndex].shadowResolution/
                          (float) m_ShadowSettings.shadowAtlasWidth;
            matTile.m11 = (float) m_ShadowSlices[cascadeIndex].shadowResolution/
                          (float) m_ShadowSettings.shadowAtlasHeight;
            matTile.m03 = (float) m_ShadowSlices[cascadeIndex].atlasX/(float) m_ShadowSettings.shadowAtlasWidth;
            matTile.m13 = (float) m_ShadowSlices[cascadeIndex].atlasY/(float) m_ShadowSettings.shadowAtlasHeight;

            m_ShadowSlices[cascadeIndex].shadowTransform = matTile*matScaleBias*proj*view;
        }

        private void RenderShadowSlice(ref ScriptableRenderContext context, Vector3 lightDir, int cascadeIndex,
            Matrix4x4 proj, Matrix4x4 view, DrawShadowsSettings settings)
        {
            var buffer = new CommandBuffer() {name = "Prepare Shadowmap Slice"};
            buffer.SetViewport(new Rect(m_ShadowSlices[cascadeIndex].atlasX, m_ShadowSlices[cascadeIndex].atlasY,
                m_ShadowSlices[cascadeIndex].shadowResolution, m_ShadowSlices[cascadeIndex].shadowResolution));
            buffer.SetViewProjectionMatrices(view, proj);
            buffer.SetGlobalVector("_WorldLightDirAndBias",
                new Vector4(-lightDir.x, -lightDir.y, -lightDir.z, m_Asset.ShadowBias));
            context.ExecuteCommandBuffer(buffer);
            buffer.Dispose();

            context.DrawShadows(ref settings);
        }

        private int GetMaxTileResolutionInAtlas(int atlasWidth, int atlasHeight, int tileCount)
        {
            int resolution = Mathf.Min(atlasWidth, atlasHeight);
            if (tileCount > Mathf.Log(resolution))
            {
                Debug.LogError(
                    String.Format(
                        "Cannot fit {0} tiles into current shadowmap atlas of size ({1}, {2}). ShadowMap Resolution set to zero.",
                        tileCount, atlasWidth, atlasHeight));
                return 0;
            }

            int currentTileCount = atlasWidth/resolution*atlasHeight/resolution;
            while (currentTileCount < tileCount)
            {
                resolution = resolution >> 1;
                currentTileCount = atlasWidth/resolution*atlasHeight/resolution;
            }
            return resolution;
        }

        void SetupShadowShaderVariables(ScriptableRenderContext context, float shadowNear, float shadowFar,
            int cascadeCount)
        {
            float shadowResolution = m_ShadowSlices[0].shadowResolution;

            // PSSM distance settings
            float shadowFrustumDepth = shadowFar - shadowNear;
            Vector3 shadowSplitRatio = m_ShadowSettings.directionalLightCascades;

            // We set PSSMDistance to infinity for non active cascades so the comparison test always fails for unavailable cascades
            Vector4 PSSMDistances = new Vector4(
                shadowNear + shadowSplitRatio.x*shadowFrustumDepth,
                (shadowSplitRatio.y > 0.0f) ? shadowNear + shadowSplitRatio.y*shadowFrustumDepth : Mathf.Infinity,
                (shadowSplitRatio.z > 0.0f) ? shadowNear + shadowSplitRatio.z*shadowFrustumDepth : Mathf.Infinity,
                1.0f/shadowResolution);

            const int maxShadowCascades = 4;
            Matrix4x4[] shadowMatrices = new Matrix4x4[maxShadowCascades];
            for (int i = 0; i < cascadeCount; ++i)
                shadowMatrices[i] = (cascadeCount >= i) ? m_ShadowSlices[i].shadowTransform : Matrix4x4.identity;

            // TODO: shadow resolution per cascade in case cascades endup being supported.
            float invShadowResolution = 1.0f/shadowResolution;
            float[] pcfKernel =
            {
                -0.5f*invShadowResolution, 0.5f*invShadowResolution,
                0.5f*invShadowResolution, 0.5f*invShadowResolution,
                -0.5f*invShadowResolution, -0.5f*invShadowResolution,
                0.5f*invShadowResolution, -0.5f*invShadowResolution
            };

            var setupShadow = new CommandBuffer() {name = "SetupShadowShaderConstants"};
            SetShadowKeywords(setupShadow);
            setupShadow.SetGlobalMatrixArray("_WorldToShadow", shadowMatrices);
            setupShadow.SetGlobalVector("_PSSMDistancesAndShadowResolution", PSSMDistances);
            setupShadow.SetGlobalVectorArray("g_vDirShadowSplitSpheres", m_DirectionalShadowSplitDistances);
            setupShadow.SetGlobalFloatArray("_PCFKernel", pcfKernel);
            SetShadowKeywords(setupShadow);
            context.ExecuteCommandBuffer(setupShadow);
            setupShadow.Dispose();
        }

        void SetShadowKeywords(CommandBuffer cmd)
        {
            switch (m_Asset.CurrShadowType)
            {
                case ShadowType.NO_SHADOW:
                    cmd.DisableShaderKeyword("HARD_SHADOWS");
                    cmd.DisableShaderKeyword("SOFT_SHADOWS");
                    break;

                case ShadowType.HARD_SHADOWS:
                    cmd.EnableShaderKeyword("HARD_SHADOWS");
                    cmd.DisableShaderKeyword("SOFT_SHADOWS");
                    break;

                case ShadowType.SOFT_SHADOWS:
                    cmd.DisableShaderKeyword("HARD_SHADOWS");
                    cmd.EnableShaderKeyword("SOFT_SHADOWS");
                    break;
            }
        }

        #endregion
    }
}
