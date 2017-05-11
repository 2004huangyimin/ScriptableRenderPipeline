﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class ClassicDeferredPipelineInstance : RenderPipeline {

	private readonly ClassicDeferredPipeline m_Owner;

	public ClassicDeferredPipelineInstance(ClassicDeferredPipeline owner)
	{
		m_Owner = owner;

		if (m_Owner != null)
			m_Owner.Build();
	}

	public override void Dispose()
	{
		base.Dispose();
		if (m_Owner != null)
			m_Owner.Cleanup();
	}


	public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
	{
		base.Render(renderContext, cameras);
		m_Owner.Render(renderContext, cameras);
	}
}

[ExecuteInEditMode]
public class ClassicDeferredPipeline : RenderPipelineAsset {

#if UNITY_EDITOR
	[UnityEditor.MenuItem("RenderPipeline/Create ClassicDeferredPipeline")]
	static void CreateDeferredRenderPipeline()
	{
		var instance = ScriptableObject.CreateInstance<ClassicDeferredPipeline> ();
		UnityEditor.AssetDatabase.CreateAsset (instance, "Assets/ClassicDeferredPipeline.asset");
	}

	[UnityEditor.MenuItem("MobileRenderPipeline/Setup Materials")]
	static void SetupDeferredRenderPipelineMaterials()
	{
		Renderer[] _renderers = Component.FindObjectsOfType<Renderer> ();
		foreach (Renderer _renderer in _renderers) {
			Material[] _materials = _renderer.sharedMaterials;
			foreach (Material _material in _materials) {
				if (_material == null)
					continue;
				
				if (_material.shader.name.Contains ("Standard (Specular setup)")) {
					_material.shader = Shader.Find("Standard-SRP (Specular setup)");
				} else if (_material.shader.name.Contains ("Standard")) {
					_material.shader = Shader.Find("Standard-SRP");
				}

			}
		}
	}
#endif

	protected override IRenderPipeline InternalCreatePipeline()
	{
		return new ClassicDeferredPipelineInstance(this);
	}
		
	[SerializeField]
	ShadowSettings m_ShadowSettings = ShadowSettings.Default;
	ShadowRenderPass m_ShadowPass;

	const int k_MaxLights = 10;
	const int k_MaxShadowmapPerLights = 6;
	const int k_MaxDirectionalSplit = 4;

	Matrix4x4[] m_MatWorldToShadow = new Matrix4x4[k_MaxLights * k_MaxShadowmapPerLights];
	Vector4[] m_DirShadowSplitSpheres = new Vector4[k_MaxDirectionalSplit];
	Vector4[] m_Shadow3X3PCFTerms = new Vector4[4];

	[NonSerialized]
	private int m_WarnedTooManyLights = 0;

	private int m_shadowBufferID;

	public Mesh m_PointLightMesh;
	public float PointLightMeshScaleFactor = 2.0f;

	public Mesh m_SpotLightMesh;
	public float SpotLightMeshScaleFactor = 1.0f;

	public Mesh m_QuadMesh;
	public Mesh m_BoxMesh;

	public Texture m_DefaultSpotCookie;

	public Shader finalPassShader;
	public Shader deferredShader;
	public Shader deferredReflectionShader;

	private static RenderPassAttachment s_GBufferAlbedo;
	private static RenderPassAttachment s_GBufferSpecRough;
	private static RenderPassAttachment s_GBufferNormal;
	private static RenderPassAttachment s_GBufferEmission;
	private static RenderPassAttachment s_GBufferZ;

	private static RenderPassAttachment s_CameraTarget;

	//private static int s_GBufferRedF32;
	//private static int s_CameraDepthTexture;

	private Material m_DirectionalDeferredLightingMaterial;
	private Material m_FiniteDeferredLightingMaterial;
	private Material m_FiniteNearDeferredLightingMaterial;

	private Material m_ReflectionMaterial;
	private Material m_ReflectionNearClipMaterial;
	private Material m_ReflectionNearAndFarClipMaterial;

	private Material m_BlitMaterial;

	private void OnValidate()
	{
		Build();
	}

	public void Cleanup()
	{
		if (m_BlitMaterial) DestroyImmediate(m_BlitMaterial);
		if (m_DirectionalDeferredLightingMaterial) DestroyImmediate(m_DirectionalDeferredLightingMaterial);
		if (m_FiniteDeferredLightingMaterial) DestroyImmediate(m_FiniteDeferredLightingMaterial);
		if (m_FiniteNearDeferredLightingMaterial) DestroyImmediate(m_FiniteNearDeferredLightingMaterial);
		if (m_ReflectionMaterial) DestroyImmediate (m_ReflectionMaterial);
		if (m_ReflectionNearClipMaterial) DestroyImmediate (m_ReflectionNearClipMaterial);
		if (m_ReflectionNearAndFarClipMaterial) DestroyImmediate (m_ReflectionNearAndFarClipMaterial);
	}

	public void Build()
	{
		s_GBufferAlbedo = new RenderPassAttachment(RenderTextureFormat.ARGB32);
		s_GBufferSpecRough = new RenderPassAttachment(RenderTextureFormat.ARGB32);
		s_GBufferNormal = new RenderPassAttachment(RenderTextureFormat.ARGB2101010);
		s_GBufferEmission = new RenderPassAttachment(RenderTextureFormat.ARGBHalf);
		s_GBufferZ = new RenderPassAttachment(RenderTextureFormat.Depth);
		s_CameraTarget = new RenderPassAttachment(RenderTextureFormat.ARGB32);

		s_GBufferEmission.Clear(new Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f, 0);
		s_GBufferZ.Clear(new Color(), 1.0f, 0);

		s_CameraTarget.BindSurface(BuiltinRenderTextureType.CameraTarget, false, true);

		// material setup
		m_BlitMaterial = new Material (finalPassShader) { hideFlags = HideFlags.HideAndDontSave };

		m_DirectionalDeferredLightingMaterial = new Material (deferredShader) { hideFlags = HideFlags.HideAndDontSave };
		m_DirectionalDeferredLightingMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
		m_DirectionalDeferredLightingMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_DirectionalDeferredLightingMaterial.SetInt("_SrcABlend", (int)BlendMode.One);
		m_DirectionalDeferredLightingMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_DirectionalDeferredLightingMaterial.SetInt("_CullMode", (int)CullMode.Off);
		m_DirectionalDeferredLightingMaterial.SetInt("_CompareFunc", (int)CompareFunction.Always);

		m_FiniteDeferredLightingMaterial = new Material (deferredShader) { hideFlags = HideFlags.HideAndDontSave };
		m_FiniteDeferredLightingMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
		m_FiniteDeferredLightingMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_FiniteDeferredLightingMaterial.SetInt("_SrcABlend", (int)BlendMode.One);
		m_FiniteDeferredLightingMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_FiniteDeferredLightingMaterial.SetInt("_CullMode", (int)CullMode.Back);
		m_FiniteDeferredLightingMaterial.SetInt("_CompareFunc", (int)CompareFunction.LessEqual);

		m_FiniteNearDeferredLightingMaterial = new Material (deferredShader) { hideFlags = HideFlags.HideAndDontSave };
		m_FiniteNearDeferredLightingMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
		m_FiniteNearDeferredLightingMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_FiniteNearDeferredLightingMaterial.SetInt("_SrcABlend", (int)BlendMode.One);
		m_FiniteNearDeferredLightingMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_FiniteNearDeferredLightingMaterial.SetInt("_CullMode", (int)CullMode.Front);
		m_FiniteNearDeferredLightingMaterial.SetInt("_CompareFunc", (int)CompareFunction.Greater);

		m_ReflectionMaterial = new Material (deferredReflectionShader) { hideFlags = HideFlags.HideAndDontSave };
		m_ReflectionMaterial.SetInt("_SrcBlend", (int)BlendMode.DstAlpha);
		m_ReflectionMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_ReflectionMaterial.SetInt("_SrcABlend", (int)BlendMode.DstAlpha);
		m_ReflectionMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_ReflectionMaterial.SetInt("_CullMode", (int)CullMode.Back);
		m_ReflectionMaterial.SetInt("_CompareFunc", (int)CompareFunction.LessEqual);

		m_ReflectionNearClipMaterial = new Material (deferredReflectionShader) { hideFlags = HideFlags.HideAndDontSave };
		m_ReflectionNearClipMaterial.SetInt("_SrcBlend", (int)BlendMode.DstAlpha);
		m_ReflectionNearClipMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_ReflectionNearClipMaterial.SetInt("_SrcABlend", (int)BlendMode.DstAlpha);
		m_ReflectionNearClipMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_ReflectionNearClipMaterial.SetInt("_CullMode", (int)CullMode.Front);
		m_ReflectionNearClipMaterial.SetInt("_CompareFunc", (int)CompareFunction.GreaterEqual);

		m_ReflectionNearAndFarClipMaterial = new Material (deferredReflectionShader) { hideFlags = HideFlags.HideAndDontSave };
		m_ReflectionNearAndFarClipMaterial.SetInt("_SrcBlend", (int)BlendMode.DstAlpha);
		m_ReflectionNearAndFarClipMaterial.SetInt("_DstBlend", (int)BlendMode.One);
		m_ReflectionNearAndFarClipMaterial.SetInt("_SrcABlend", (int)BlendMode.DstAlpha);
		m_ReflectionNearAndFarClipMaterial.SetInt("_DstABlend", (int)BlendMode.Zero);
		m_ReflectionNearAndFarClipMaterial.SetInt("_CullMode", (int)CullMode.Off);
		m_ReflectionNearAndFarClipMaterial.SetInt("_CompareFunc", (int)CompareFunction.Always);
					
		//shadows
		m_MatWorldToShadow = new Matrix4x4[k_MaxLights * k_MaxShadowmapPerLights];
		m_DirShadowSplitSpheres = new Vector4[k_MaxDirectionalSplit];
		m_Shadow3X3PCFTerms = new Vector4[4];
		m_ShadowPass = new ShadowRenderPass(m_ShadowSettings);

		m_shadowBufferID = Shader.PropertyToID("g_tShadowBuffer");
	}

	public void Render(ScriptableRenderContext context, IEnumerable<Camera> cameras)
	{
		RenderDeferred(context, cameras);

		context.Submit ();
	}
	void RenderDeferred(ScriptableRenderContext context, IEnumerable<Camera> cameras)
	{
		foreach (var camera in cameras) {
			// Culling
			CullingParameters cullingParams;
			if (!CullResults.GetCullingParameters (camera, out cullingParams))
				continue;
			var cullResults = CullResults.Cull (ref cullingParams, context);

			m_ShadowPass.UpdateCullingParameters(ref cullingParams);
			RenderShadowMaps(cullResults, context);

			// Setup camera for rendering (sets render target, view/projection matrices and other
			// per-camera built-in shader variables).
			context.SetupCameraProperties(camera);

			using (RenderPass rp = new RenderPass (context, camera.pixelWidth, camera.pixelHeight, 1, new[] { s_GBufferAlbedo, s_GBufferSpecRough, s_GBufferNormal, s_GBufferEmission, s_CameraTarget }, s_GBufferZ)) 
			{
				using (new RenderPass.SubPass(rp, new[] { s_GBufferAlbedo, s_GBufferSpecRough, s_GBufferNormal, s_GBufferEmission }, null))
				{
					using (var cmd = new CommandBuffer { name = "Create G-Buffer" } )
					{
						cmd.EnableShaderKeyword("UNITY_HDR_ON");

						context.ExecuteCommandBuffer(cmd);
					}

					var settings = new DrawRendererSettings(cullResults, camera, new ShaderPassName("Deferred"))
					{
						sorting = { flags = SortFlags.CommonOpaque },
						rendererConfiguration = RendererConfiguration.PerObjectLightmaps

					};

					settings.inputFilter.SetQueuesOpaque();
					context.DrawRenderers(ref settings);
				}

				PushGlobalShadowParams (context);

				// lighting pass
				using (new RenderPass.SubPass(rp, new[] { s_GBufferEmission }, new[] { s_GBufferAlbedo, s_GBufferSpecRough, s_GBufferNormal, s_GBufferZ }, true))
				{
					using (var cmd = new CommandBuffer { name = "Lighting and Reflections"} )
					{
						RenderLightGeometry (camera, cullResults, cmd, context);
						RenderReflections (camera, cmd, cullResults, context);

						context.ExecuteCommandBuffer(cmd);
					}
				}

				using (new RenderPass.SubPass (rp, new[] { s_GBufferEmission }, null, true)) {
					context.DrawSkybox (camera);
				}

				//needed?
				context.SetupCameraProperties(camera);

				using (new RenderPass.SubPass(rp, new[] { s_CameraTarget }, new[] { s_GBufferEmission }))
				{
					var cmd = new CommandBuffer { name = "FinalPass" };

					cmd.DrawProcedural(new Matrix4x4(), m_BlitMaterial, 0, MeshTopology.Triangles, 3);

					context.ExecuteCommandBuffer(cmd);
					cmd.Dispose();

				}
			}
		}
	}


	void RenderReflections(Camera camera, CommandBuffer cmd, CullResults cullResults, ScriptableRenderContext loop)
	{
		var probes = cullResults.visibleReflectionProbes;
		var worldToView = camera.worldToCameraMatrix; //WorldToCamera(camera);

		float nearDistanceFudged = camera.nearClipPlane * 1.001f;
		float farDistanceFudged = camera.farClipPlane * 0.999f;
		var viewDir = camera.cameraToWorldMatrix.GetColumn(2);
		var viewDirNormalized = -1 * Vector3.Normalize(new Vector3 (viewDir.x, viewDir.y, viewDir.z));

		Plane eyePlane = new Plane ();
		eyePlane.SetNormalAndPosition(viewDirNormalized, camera.transform.position);

		// TODO: need this? --> Set the ambient probe into the SH constants otherwise
		// SetSHConstants(builtins, m_LightprobeContext.ambientProbe);

		// render all probes in reverse order so they are blended into the existing emission buffer with the correct blend settings as follows:
		// emisNew = emis + Lerp( Lerp( Lerp(base,probe0,1-t0), probe1, 1-t1 ), probe2, 1-t2)....
		// DST_COL = DST_COL + DST_ALPHA * SRC_COLOR
		// DST_ALPHA = DST_ALPHA * SRC_ALPHA

		int numProbes = probes.Length;
		for (int i = numProbes-1; i >= 0; i--)
		{
			var rl = probes [i];
			var cubemap = rl.texture;

			// always a box for now
			if (cubemap == null)
				continue;

			var bnds = rl.bounds;
			var boxOffset = rl.center;                  // reflection volume offset relative to cube map capture point
			var blendDistance = rl.blendDistance;

			// TODO: fix for rotations on probes... Builtin Unity also does not take these into account, for now just grab position for mat
			//var mat = rl.localToWorld;
			Matrix4x4 mat = Matrix4x4.identity;
			mat.SetColumn (3, rl.localToWorld.GetColumn (3));

			var boxProj = (rl.boxProjection != 0);
			var probePosition = mat.GetColumn (3); // translation vector
			var probePosition1 = new Vector4 (probePosition [0], probePosition [1], probePosition [2], boxProj ? 1f : 0f);

			// C is reflection volume center in world space (NOT same as cube map capture point)
			var e = bnds.extents;       // 0.5f * Vector3.Max(-boxSizes[p], boxSizes[p]);
			var combinedExtent = e + new Vector3(blendDistance, blendDistance, blendDistance);

			Matrix4x4 scaled = Matrix4x4.Scale (combinedExtent * 2.0f);
			mat = mat * Matrix4x4.Translate (boxOffset) * scaled;
					
			var probeRadius = combinedExtent.magnitude;
			var viewDistance = eyePlane.GetDistanceToPoint(boxOffset);
			bool intersectsNear = viewDistance - probeRadius <= nearDistanceFudged;
			bool intersectsFar = viewDistance + probeRadius >= farDistanceFudged;
			bool renderAsQuad = (intersectsNear && intersectsFar);

			var props = new MaterialPropertyBlock ();
			props.SetFloat ("_LightAsQuad", renderAsQuad ? 1 : 0);

			var min = rl.bounds.min;
			var max = rl.bounds.max;

			cmd.SetGlobalTexture("unity_SpecCube0", cubemap);
			cmd.SetGlobalVector("unity_SpecCube0_HDR", rl.probe.textureHDRDecodeValues);
			cmd.SetGlobalVector ("unity_SpecCube0_BoxMin", min);
			cmd.SetGlobalVector ("unity_SpecCube0_BoxMax", max);
			cmd.SetGlobalVector ("unity_SpecCube0_ProbePosition", probePosition1);
			cmd.SetGlobalVector ("unity_SpecCube1_ProbePosition", new Vector4(0, 0, 0, blendDistance));

			if (renderAsQuad) {
				cmd.DrawMesh (m_QuadMesh, Matrix4x4.identity, m_ReflectionNearAndFarClipMaterial, 0, 0, props);
			} else if (intersectsNear) {
				cmd.DrawMesh (m_BoxMesh, mat, m_ReflectionNearClipMaterial, 0, 0, props);
			} else{
				cmd.DrawMesh (m_BoxMesh, mat, m_ReflectionMaterial, 0, 0, props);
			}
		}

		// draw the base probe
		{ 
			var props = new MaterialPropertyBlock ();
			props.SetFloat ("_LightAsQuad", 1.0f);

			// base reflection probe
			var topCube = ReflectionProbe.defaultTexture;
			var defdecode = ReflectionProbe.defaultTextureHDRDecodeValues;
			cmd.SetGlobalTexture ("unity_SpecCube0", topCube);
			cmd.SetGlobalVector ("unity_SpecCube0_HDR", defdecode);

			float max = float.PositiveInfinity;
			float min = float.NegativeInfinity;
			cmd.SetGlobalVector("unity_SpecCube0_BoxMin", new Vector4(min, min, min, 1));
			cmd.SetGlobalVector("unity_SpecCube0_BoxMax", new Vector4(max, max, max, 1));

			cmd.SetGlobalVector ("unity_SpecCube0_ProbePosition", new Vector4 (0.0f, 0.0f, 0.0f, 0.0f));
			cmd.SetGlobalVector ("unity_SpecCube1_ProbePosition", new Vector4 (0.0f, 0.0f, 0.0f, 1.0f));

			cmd.DrawMesh (m_QuadMesh, Matrix4x4.identity, m_ReflectionNearAndFarClipMaterial, 0, 0, props);
		}
	}

	void RenderShadowMaps(CullResults cullResults, ScriptableRenderContext loop)
	{
		ShadowOutput shadows;
		m_ShadowPass.Render(loop, cullResults, out shadows);
		UpdateShadowConstants (cullResults.visibleLights, ref shadows);
	}

	void PushGlobalShadowParams(ScriptableRenderContext loop)
	{
		var cmd = new CommandBuffer { name = "Push Global Parameters" };

		// Shadow constants
		cmd.SetGlobalMatrixArray("g_matWorldToShadow", m_MatWorldToShadow);
		cmd.SetGlobalVectorArray("g_vDirShadowSplitSpheres", m_DirShadowSplitSpheres);
		cmd.SetGlobalVector("g_vShadow3x3PCFTerms0", m_Shadow3X3PCFTerms[0]);
		cmd.SetGlobalVector("g_vShadow3x3PCFTerms1", m_Shadow3X3PCFTerms[1]);
		cmd.SetGlobalVector("g_vShadow3x3PCFTerms2", m_Shadow3X3PCFTerms[2]);
		cmd.SetGlobalVector("g_vShadow3x3PCFTerms3", m_Shadow3X3PCFTerms[3]);

		loop.ExecuteCommandBuffer(cmd);
		cmd.Dispose();
	}

	void UpdateShadowConstants(IList<VisibleLight> visibleLights, ref ShadowOutput shadow)
	{
		var nNumLightsIncludingTooMany = 0;

		var numLights = 0;

		var lightShadowIndex_LightParams = new Vector4[k_MaxLights];
		var lightFalloffParams = new Vector4[k_MaxLights];

		for (int nLight = 0; nLight < visibleLights.Count; nLight++)
		{
			nNumLightsIncludingTooMany++;
			if (nNumLightsIncludingTooMany > k_MaxLights)
				continue;

			var light = visibleLights[nLight];
			var lightType = light.lightType;
			var position = light.light.transform.position;
			var lightDir = light.light.transform.forward.normalized;

			// Setup shadow data arrays
			var hasShadows = shadow.GetShadowSliceCountLightIndex(nLight) != 0;

			if (lightType == LightType.Directional)
			{
				lightShadowIndex_LightParams[numLights] = new Vector4(0, 0, 1, 1);
				lightFalloffParams[numLights] = new Vector4(0.0f, 0.0f, float.MaxValue, (float)lightType);

				if (hasShadows)
				{
					for (int s = 0; s < k_MaxDirectionalSplit; ++s)
					{
						m_DirShadowSplitSpheres[s] = shadow.directionalShadowSplitSphereSqr[s];
					}
				}
			}
			else if (lightType == LightType.Point)
			{
				lightShadowIndex_LightParams[numLights] = new Vector4(0, 0, 1, 1);
				lightFalloffParams[numLights] = new Vector4(1.0f, 0.0f, light.range * light.range, (float)lightType);
			}
			else if (lightType == LightType.Spot)
			{
				lightShadowIndex_LightParams[numLights] = new Vector4(0, 0, 1, 1);
				lightFalloffParams[numLights] = new Vector4(1.0f, 0.0f, light.range * light.range, (float)lightType);
			}

			if (hasShadows)
			{
				// Enable shadows
				lightShadowIndex_LightParams[numLights].x = 1;
				for (int s = 0; s < shadow.GetShadowSliceCountLightIndex(nLight); ++s)
				{
					var shadowSliceIndex = shadow.GetShadowSliceIndex(nLight, s);
					m_MatWorldToShadow[numLights * k_MaxShadowmapPerLights + s] = shadow.shadowSlices[shadowSliceIndex].shadowTransform.transpose;
				}
			}

			numLights++;
		}

		// Warn if too many lights found
		if (nNumLightsIncludingTooMany > k_MaxLights)
		{
			if (nNumLightsIncludingTooMany > m_WarnedTooManyLights)
			{
				Debug.LogError("ERROR! Found " + nNumLightsIncludingTooMany + " runtime lights! Renderer supports up to " + k_MaxLights +
					" active runtime lights at a time!\nDisabling " + (nNumLightsIncludingTooMany - k_MaxLights) + " runtime light" +
					((nNumLightsIncludingTooMany - k_MaxLights) > 1 ? "s" : "") + "!\n");
			}
			m_WarnedTooManyLights = nNumLightsIncludingTooMany;
		}
		else
		{
			if (m_WarnedTooManyLights > 0)
			{
				m_WarnedTooManyLights = 0;
				Debug.Log("SUCCESS! Found " + nNumLightsIncludingTooMany + " runtime lights which is within the supported number of lights, " + k_MaxLights + ".\n\n");
			}
		}

		// PCF 3x3 Shadows
		var flTexelEpsilonX = 1.0f / m_ShadowSettings.shadowAtlasWidth;
		var flTexelEpsilonY = 1.0f / m_ShadowSettings.shadowAtlasHeight;
		m_Shadow3X3PCFTerms[0] = new Vector4(20.0f / 267.0f, 33.0f / 267.0f, 55.0f / 267.0f, 0.0f);
		m_Shadow3X3PCFTerms[1] = new Vector4(flTexelEpsilonX, flTexelEpsilonY, -flTexelEpsilonX, -flTexelEpsilonY);
		m_Shadow3X3PCFTerms[2] = new Vector4(flTexelEpsilonX, flTexelEpsilonY, 0.0f, 0.0f);
		m_Shadow3X3PCFTerms[3] = new Vector4(-flTexelEpsilonX, -flTexelEpsilonY, 0.0f, 0.0f);
	}

	void RenderLightGeometry (Camera camera, CullResults inputs, CommandBuffer cmd, ScriptableRenderContext loop)
	{
		int lightCount = inputs.visibleLights.Length;
		for (int lightNum = 0; lightNum < lightCount; lightNum++) 
		{
			VisibleLight light = inputs.visibleLights[lightNum];

			bool intersectsNear = (light.flags & VisibleLightFlags.IntersectsNearPlane) != 0;
			bool intersectsFar = (light.flags & VisibleLightFlags.IntersectsFarPlane) != 0;
			bool renderAsQuad =  (intersectsNear && intersectsFar) || (light.lightType == LightType.Directional);
				
			Vector3 lightPos = light.localToWorld.GetColumn (3); //position
			Vector3 lightDir = light.localToWorld.GetColumn (2); //z axis
			float range = light.range;
			var lightToWorld = light.localToWorld;
			var worldToLight = lightToWorld.inverse;

			cmd.SetGlobalMatrix ("unity_WorldToLight", lightToWorld.inverse);

			var props = new MaterialPropertyBlock ();
			props.SetFloat ("_LightAsQuad", renderAsQuad ? 1 : 0);
			props.SetVector ("_LightPos", new Vector4(lightPos.x, lightPos.y, lightPos.z, 1.0f / (range * range)));
			props.SetVector ("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0.0f));
			props.SetVector ("_LightColor", light.finalColor);

			float lightShadowNDXOrNot = (light.light.shadows != LightShadows.None) ? (float)lightNum : -1.0f;
			props.SetFloat ("_LightIndexForShadowMatrixArray", lightShadowNDXOrNot);

			// TODO:OPTIMIZATION DeferredRenderLoop.cpp:660 -- split up into shader varients

			cmd.DisableShaderKeyword ("POINT");
			cmd.DisableShaderKeyword ("POINT_COOKIE");
			cmd.DisableShaderKeyword ("SPOT");
			cmd.DisableShaderKeyword ("DIRECTIONAL");
			cmd.DisableShaderKeyword ("DIRECTIONAL_COOKIE");
			switch (light.lightType)
			{
			case LightType.Point:
				cmd.EnableShaderKeyword ("POINT");
				break;
			case LightType.Spot:
				cmd.EnableShaderKeyword ("SPOT");
				break;
			case LightType.Directional:
				cmd.EnableShaderKeyword ("DIRECTIONAL");
				break;
			}
				
			Texture cookie = light.light.cookie;
			if (cookie != null)
				cmd.SetGlobalTexture ("_LightTexture0", cookie);

			if ((light.lightType == LightType.Point)) {

				// scalingFactor corrosoponds to the scale factor setting (and wether file scale is used) of mesh in Unity mesh inspector. 
				// A scale factor setting in Unity of 0.01 would require this to be set to 100. A scale factor setting of 1, is just 1 here. 
				var matrix = Matrix4x4.TRS (lightPos, Quaternion.identity, new Vector3 (range*PointLightMeshScaleFactor, range*PointLightMeshScaleFactor, range*PointLightMeshScaleFactor));

				if (cookie != null) {
					cmd.DisableShaderKeyword ("POINT");
					cmd.EnableShaderKeyword ("POINT_COOKIE");
				}
				if (renderAsQuad) {
					cmd.DrawMesh (m_QuadMesh, Matrix4x4.identity, m_DirectionalDeferredLightingMaterial, 0, 0, props);
				} else if (intersectsNear) {
					cmd.DrawMesh (m_PointLightMesh, matrix, m_FiniteNearDeferredLightingMaterial, 0, 0, props);
				} else {
					cmd.DrawMesh (m_PointLightMesh, matrix, m_FiniteDeferredLightingMaterial, 0, 0, props);
				}

			} else if ((light.lightType == LightType.Spot)) {

				float chsa = GetCotanHalfSpotAngle (light.spotAngle);

				// Setup Light Matrix
				Matrix4x4 temp1 = Matrix4x4.Scale(new Vector3 (-.5f, -.5f, 1.0f));
				Matrix4x4 temp2 = Matrix4x4.Translate( new Vector3 (.5f, .5f, 0.0f));
				Matrix4x4 temp3 = PerspectiveCotanMatrix (chsa, 0.0f, range);
				var LightMatrix0 = temp2 * temp1 * temp3 * worldToLight;
				props.SetMatrix ("_LightMatrix0", LightMatrix0);

				// Setup Spot Rendering mesh matrix
				float sideLength = range / chsa;

				// scalingFactor corrosoponds to the scale factor setting (and wether file scale is used) of mesh in Unity mesh inspector. 
				// A scale factor setting in Unity of 0.01 would require this to be set to 100. A scale factor setting of 1, is just 1 here. 
				lightToWorld = lightToWorld * Matrix4x4.Scale (new Vector3(sideLength*SpotLightMeshScaleFactor, sideLength*SpotLightMeshScaleFactor, range*SpotLightMeshScaleFactor));

				//set default cookie for spot light if there wasnt one added to the light manually
				if (cookie == null)
					cmd.SetGlobalTexture ("_LightTexture0", m_DefaultSpotCookie);

				if (renderAsQuad) {
					cmd.DrawMesh (m_QuadMesh, Matrix4x4.identity, m_DirectionalDeferredLightingMaterial, 0, 0, props);
				} else if (intersectsNear) {
					cmd.DrawMesh (m_SpotLightMesh, lightToWorld, m_FiniteNearDeferredLightingMaterial, 0, 0, props);
				} else {
					cmd.DrawMesh (m_SpotLightMesh, lightToWorld, m_FiniteDeferredLightingMaterial, 0, 0, props);
				}
					
			} else {

				// Setup Light Matrix
				float scale = 1.0f / light.light.cookieSize;
				Matrix4x4 temp1 = Matrix4x4.Scale(new Vector3 (scale, scale, 0.0f));
				Matrix4x4 temp2 = Matrix4x4.Translate( new Vector3 (.5f, .5f, 0.0f));
				var LightMatrix0 = temp2 * temp1 * worldToLight;
				props.SetMatrix ("_LightMatrix0", LightMatrix0);
			
				if (cookie != null) {
					cmd.DisableShaderKeyword ("DIRECTIONAL");
					cmd.EnableShaderKeyword ("DIRECTIONAL_COOKIE");
				}
				cmd.DrawMesh (m_QuadMesh, Matrix4x4.identity, m_DirectionalDeferredLightingMaterial, 0, 0, props);
			}
		}
	}

	Matrix4x4 PerspectiveCotanMatrix(float cotangent, float zNear, float zFar )
	{
		float deltaZ = zNear - zFar;
		var m = Matrix4x4.zero;

		m.m00 = cotangent;			m.m01 = 0.0f;      m.m02 = 0.0f;                    m.m03 = 0.0f;
		m.m10 = 0.0f;               m.m11 = cotangent; m.m12 = 0.0f;                    m.m13 = 0.0f;
		m.m20 = 0.0f;               m.m21 = 0.0f;      m.m22 = (zFar + zNear) / deltaZ; m.m23 = 2.0f * zNear * zFar / deltaZ;
		m.m30 = 0.0f;               m.m31 = 0.0f;      m.m32 = -1.0f;                   m.m33 = 0.0f;

		return m;
	}

	float GetCotanHalfSpotAngle (float spotAngle)
	{
		const float degToRad = (float)(Mathf.PI / 180.0);
		var cs = Mathf.Cos(0.5f * spotAngle * degToRad);
		var ss = Mathf.Sin(0.5f * spotAngle * degToRad);
		return cs / ss; //cothalfspotangle
		//m_InvCosHalfSpotAngle = 1.0f / cs;
	}

	static Matrix4x4 GetFlipMatrix()
	{
		Matrix4x4 flip = Matrix4x4.identity;
		bool isLeftHand = ((int)LightDefinitions.USE_LEFTHAND_CAMERASPACE) != 0;
		if (isLeftHand) flip.SetColumn(2, new Vector4(0.0f, 0.0f, -1.0f, 0.0f));
		return flip;
	}

	static Matrix4x4 WorldToCamera(Camera camera)
	{
		return GetFlipMatrix() * camera.worldToCameraMatrix;
	}
}

