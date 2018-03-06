﻿using UnityEngine.Experimental.Rendering.HDPipeline;

namespace UnityEngine.Experimental.Rendering
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(ReflectionProbe), typeof(MeshFilter), typeof(MeshRenderer))]
    public class HDAdditionalReflectionData : MonoBehaviour
    {
#pragma warning disable 414 // CS0414 The private field '...' is assigned but its value is never used
        // We can't rely on Unity for our additional data, we need to version it ourself.
        [SerializeField]
        float m_Version = 1.0f;
#pragma warning restore 414

        public ShapeType influenceShape;
        [Range(0.0f,1.0f)]
        public float dimmer = 1.0f;
        public float influenceSphereRadius = 3.0f;
        public float sphereReprojectionVolumeRadius = 1.0f;
        public bool useSeparateProjectionVolume = false;
        public Vector3 boxReprojectionVolumeSize = Vector3.one;
        public Vector3 boxReprojectionVolumeCenter = Vector3.zero;
        public float maxSearchDistance = 8.0f;
        public Texture previewCubemap;
        public Vector3 blendDistancePositive = Vector3.zero;
        public Vector3 blendDistanceNegative = Vector3.zero;
        public Vector3 blendNormalDistancePositive = Vector3.zero;
        public Vector3 blendNormalDistanceNegative = Vector3.zero;
        public Vector3 boxSideFadePositive = Vector3.one;
        public Vector3 boxSideFadeNegative = Vector3.one;
        public Cubemap bakedTexture;

        public ReflectionProxyVolumeComponent proxyVolumeComponent;

        public Vector3 boxBlendCenterOffset { get { return (blendDistanceNegative - blendDistancePositive) * 0.5f; } }
        public Vector3 boxBlendSizeOffset { get { return -(blendDistancePositive + blendDistanceNegative); } }
        public Vector3 boxBlendNormalCenterOffset { get { return (blendNormalDistanceNegative - blendNormalDistancePositive) * 0.5f; } }
        public Vector3 boxBlendNormalSizeOffset { get { return -(blendNormalDistancePositive + blendNormalDistanceNegative); } }


        public float sphereBlendRadiusOffset { get { return -blendDistancePositive.x; } }
        public float sphereBlendNormalRadiusOffset { get { return -blendNormalDistancePositive.x; } }

        public BoundingSphere boundingSphere
        {
            get
            {
                switch (influenceShape)
                {
                    default:
                    case ShapeType.Sphere:
                        return new BoundingSphere(transform.TransformPoint(Vector3.zero), influenceSphereRadius);
                    case ShapeType.Box:
                    {
                        var extents = GetComponent<ReflectionProbe>().bounds.extents;
                        var position = transform.TransformPoint(boxBlendCenterOffset);
                        var radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
                        return new BoundingSphere(position, radius);
                    }
                }
            }
        }

        void OnEnable()
        {
            var probe = GetComponent<ReflectionProbe>();
            ReflectionSystem.RegisterProbe(probe);
            probe.bakedTexture = bakedTexture;
        }

        void OnDisable()
        {
            ReflectionSystem.UnregisterProbe(GetComponent<ReflectionProbe>());
        }

        void OnValidate()
        {
            ReflectionSystem.UnregisterProbe(GetComponent<ReflectionProbe>());

            if (isActiveAndEnabled)
                ReflectionSystem.RegisterProbe(GetComponent<ReflectionProbe>());
        }
    }
}
