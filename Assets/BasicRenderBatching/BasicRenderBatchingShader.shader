// Example shader for a scriptable render loop that calculates multiple lights
// in a single forward-rendered shading pass. Uses same PBR shading model as the
// Standard shader.
//
// The parameters and inspector of the shader are the same as Standard shader,
// for easier experimentation.
Shader "RenderLoop/Batching/Standard"
{
    // Properties is just a copy of Standard.shader. Our example shader does not use all of them,
    // but the inspector UI expects all these to exist.
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
        [HideInInspector] _Mode("__mode", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "PerformanceChecks" = "False" }
        LOD 300

        // Multiple lights at once pass, for our example Basic render loop.
        Pass
        {
            Tags { "LightMode" = "BasicPass" }

            // Use same blending / depth states as Standard shader
            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]

CGPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
#pragma shader_feature _METALLICGLOSSMAP
#include "UnityCG.cginc"

float4 _Color;

// Global lighting data (setup from C# code once per frame).

CBUFFER_START(GlobalLightData)
    // The variables are very similar to built-in unity_LightColor, unity_LightPosition,
    // unity_LightAtten, unity_SpotDirection as used by the VertexLit shaders, except here
    // we use world space positions instead of view space.
    half4 globalLightColor[8];
    float4 globalLightPos[8];
    float4 globalLightSpotDir[8];
    float4 globalLightAtten[8];
    int4  globalLightCount;
    // Global ambient/SH probe, similar to unity_SH* built-in variables.
    float4 globalSH[7];
CBUFFER_END

// Compute attenuation & illumination from one light
half3 EvaluateOneLight(int idx, float3 positionWS, half3 normalWS, float3 vAlbedo)
{
    // direction to light
    float3 dirToLight = globalLightPos[idx].xyz;
    dirToLight -= positionWS * globalLightPos[idx].w;
    // distance attenuation
    float att = 1.0;
    float distSqr = dot(dirToLight, dirToLight);
    att /= (1.0 + globalLightAtten[idx].z * distSqr);
    if (globalLightPos[idx].w != 0 && distSqr > globalLightAtten[idx].w) att = 0.0; // set to 0 if outside of range
    distSqr = max(distSqr, 0.000001); // don't produce NaNs if some vertex position overlaps with the light
    dirToLight *= rsqrt(distSqr);

	// spotlight angular attenuation
    // Fill in light & indirect structures, and evaluate Standard BRDF
    half3 light = globalLightColor[idx].rgb * att;
	half3 c = dot(dirToLight, normalWS) * light * vAlbedo;
    return c;
}

// Vertex shader
struct v2f
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 hpos : SV_POSITION;
};

v2f vert(appdata_base v)
{
    v2f o;
    o.hpos = UnityObjectToClipPos(v.vertex);
    o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.normalWS = UnityObjectToWorldNormal(v.normal);
    return o;
}

// Fragment shader
half4 frag(v2f i) : SV_Target
{
    i.normalWS = normalize(i.normalWS);
	float4 color;
	color.rgb = EvaluateOneLight(0, i.positionWS, i.normalWS, _Color.rgb);
    return color;
}

ENDCG
		}
	}

	CustomEditor "StandardShaderGUI"
}
