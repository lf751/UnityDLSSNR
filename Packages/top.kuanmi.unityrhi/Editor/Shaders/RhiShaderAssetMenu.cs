using UnityEditor;

namespace UnityRhi.EditorTools
{
    internal static class RhiShaderAssetMenu
    {
        [MenuItem("Assets/Create/UnityRHI/RHI Shader")]
        private static void CreateShaderModule() =>
            CreateTextAsset("New RHI Shader.rhishader");

        [MenuItem("Assets/Create/UnityRHI/Shader Keywords")]
        private static void CreateShaderKeywords() =>
            CreateTextAsset("New RHI Shader Keywords.rhikeywords");

        private static void CreateTextAsset(string name)
        {
#if UNITY_6000_7_OR_NEWER
            ProjectWindowUtil.CreateAssetWithTextContent(name, "");
#else
            ProjectWindowUtil.CreateAssetWithContent(name, "");
#endif
        }
    }
}
