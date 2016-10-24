namespace UnityEngine.Experimental.ScriptableRenderLoop
{
    public class ShaderGeneratorMenu
    {
        [UnityEditor.MenuItem("Renderloop/Generate Shader Includes")]
        static void GenerateShaderIncludes()
        {
            CSharpToHLSL.GenerateAll();
        }
    }
}
