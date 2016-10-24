using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    //@TODO: This should be moved into GraphicsSettings
    [ExecuteInEditMode]
    public class ScriptableRenderLoopPicker : MonoBehaviour
    {
        public ScriptableRenderLoop renderloop
        {
            get { return m_RenderLoop; }
            set { m_RenderLoop = value; }
        }

        [SerializeField]
        private ScriptableRenderLoop m_RenderLoop;

        void OnEnable()
        {
            RenderLoop.renderLoopDelegate += Render;

            SyncRenderingFeatures();
        }

        void OnValidate()
        {
            SyncRenderingFeatures();
        }

        void SyncRenderingFeatures()
        {
#if UNITY_EDITOR
            if (m_RenderLoop != null && isActiveAndEnabled)
                UnityEditor.SupportedRenderingFeatures.active = m_RenderLoop.GetSupportedRenderingFeatures();
            else
                UnityEditor.SupportedRenderingFeatures.active = UnityEditor.SupportedRenderingFeatures.Default;
#endif
        }

        void OnDisable()
        {
            RenderLoop.renderLoopDelegate -= Render;

            #if UNITY_EDITOR
            UnityEditor.SupportedRenderingFeatures.active = UnityEditor.SupportedRenderingFeatures.Default;
            #endif
        }

        bool Render(Camera[] cameras, RenderLoop loop)
        {
            if (m_RenderLoop == null)
                return false;

#if UNITY_EDITOR
            if (m_AssetVersion != s_GlobalAssetVersion)
            {
                m_AssetVersion = s_GlobalAssetVersion;
                m_RenderLoop.Rebuild();
            }
#endif

            m_RenderLoop.Render(cameras, loop);
            return true;
        }

#if UNITY_EDITOR
        // Temporary hack to allow compute shader reloading
        internal class AssetReloader : UnityEditor.AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (var str in importedAssets)
                {
                    if (str.EndsWith(".compute"))
                    {
                        s_GlobalAssetVersion++;
                        break;
                    }
                }
            }
        }
        static int s_GlobalAssetVersion = 0;
        int m_AssetVersion = 0;
#endif
    }
}
