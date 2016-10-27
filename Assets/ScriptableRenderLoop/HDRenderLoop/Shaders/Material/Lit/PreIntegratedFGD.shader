Shader "Hidden/HDRenderLoop/PreIntegratedFGD" 
{
   SubShader {
        Pass {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 5.0
            #pragma only_renderers d3d11 // TEMP: unitl we go futher in dev

            #include "Common.hlsl"
            #include "ImageBasedLighting.hlsl"
            #include "Assets/ScriptableRenderLoop/HDRenderLoop/Shaders/ShaderVariables.hlsl"


            struct Attributes 
            {
                float3 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct Varyings 
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = TransformWorldToHClip(input.vertex);
                output.texcoord = input.texcoord.xy;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // These coordinate sampling must match the decoding in GetPreIntegratedDFG in lit.hlsl, i.e here we use perceptualRoughness, must be the same in shader
                float NdotV					= input.texcoord.x;
                float perceptualRoughness	= input.texcoord.y;
                float3 V			        = float3(sqrt(1 - NdotV * NdotV), 0, NdotV);
                float3 N			        = float3(0.0, 0.0, 1.0);

                const int numSamples = 2048;
        
                // Pre integrate GGX with smithJoint visibility as well as DisneyDiffuse
                float4 preFGD = IntegrateGGXAndDisneyFGD(V, N, PerceptualRoughnessToRoughness(perceptualRoughness), numSamples);

                return float4(preFGD.xyz, 1.0);
            }

            ENDHLSL
        }
    }
    Fallback Off
}
