using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts materials exported by TreeIt (they point at the built-in Standard shader,
/// which renders magenta under URP) over to "Rule of Horror/TreeIt Wind", picking up the
/// sibling textures automatically:
///     xxx.png              → Base Map
///     xxx_Normal.png       → Normal Map
///     xxx_Transmission.png → Transmission Map
/// Select the materials (or a folder) in the Project window, then run the menu item.
/// </summary>
public static class TreeItMaterialConverter
{
    private const string ShaderName = "Rule of Horror/TreeIt Wind";

    [MenuItem("Rule of Horror/TreeIt/Convert Selected Materials To TreeIt Wind")]
    private static void ConvertSelected()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[TreeIt] Shader '{ShaderName}' not found — check that " +
                           "Assets/Shaders/TreeIt_Wind.shader compiles");
            return;
        }

        int count = 0;
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (Directory.Exists(path))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                    if (Convert(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)), shader)) count++;
            }
            else if (obj is Material mat)
            {
                if (Convert(mat, shader)) count++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TreeIt] Converted {count} material(s) → {ShaderName}");
    }

    [MenuItem("Rule of Horror/TreeIt/Convert Selected Materials To TreeIt Wind", true)]
    private static bool ConvertSelectedValidate() => Selection.objects.Length > 0;

    private static bool Convert(Material mat, Shader shader)
    {
        if (mat == null) return false;

        // Grab the existing textures before swapping the shader — a property whose name
        // does not exist on the new shader is dropped on assignment
        Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                        : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture bump    = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

        Undo.RecordObject(mat, "Convert to TreeIt Wind");
        mat.shader = shader;

        if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);

        // Find the companion textures from the base map's file name
        if (baseMap != null)
        {
            string basePath = AssetDatabase.GetAssetPath(baseMap);
            string dir      = Path.GetDirectoryName(basePath);
            string stem     = Path.GetFileNameWithoutExtension(basePath);
            string ext      = Path.GetExtension(basePath);

            if (bump == null) bump = AssetDatabase.LoadAssetAtPath<Texture>(Path.Combine(dir, $"{stem}_Normal{ext}"));
            var transmission  = AssetDatabase.LoadAssetAtPath<Texture>(Path.Combine(dir, $"{stem}_Transmission{ext}"));

            if (bump != null)         mat.SetTexture("_BumpMap", bump);
            if (transmission != null) mat.SetTexture("_TransmissionMap", transmission);

            // Leaves have to read alpha from the png — TreeIt exports without Alpha Is Transparency set
            if (AssetImporter.GetAtPath(basePath) is TextureImporter imp && !imp.alphaIsTransparency)
            {
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
        }

        // Leaves render double sided, with cutout transparency
        mat.SetFloat("_Cull", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

        // GPU Instancing must always be on — the Terrain draws trees through DrawMeshInstanced,
        // so a material without it costs one draw call per tree per submesh
        // (a few thousand painted trees turn into tens of thousands of batches immediately)
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return true;
    }
}
