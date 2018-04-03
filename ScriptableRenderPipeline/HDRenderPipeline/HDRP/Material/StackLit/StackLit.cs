using UnityEngine;
using System;

//-----------------------------------------------------------------------------
// structure definition
//-----------------------------------------------------------------------------
namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public class StackLit : RenderPipelineMaterial
    {
        //-----------------------------------------------------------------------------
        // SurfaceData
        //-----------------------------------------------------------------------------

        // Main structure that store the user data (i.e user input of master node in material graph)
        [GenerateHLSL(PackingRules.Exact, false, true, 1200)]
        public struct SurfaceData
        {
            [SurfaceDataAttributes("Color", false, true)]
            public Vector3 color;
        };

        //-----------------------------------------------------------------------------
        // BSDFData
        //-----------------------------------------------------------------------------

        [GenerateHLSL(PackingRules.Exact, false, true, 1230)]
        public struct BSDFData
        {
            [SurfaceDataAttributes("", false, true)]
            public Vector3 color;
        };
    }
}
