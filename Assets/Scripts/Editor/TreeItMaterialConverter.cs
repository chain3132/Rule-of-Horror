using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// แปลง material ที่ TreeIt export มา (ชี้ shader Standard ของ built-in → เป็นสีชมพูใน URP)
/// ให้ใช้ "Rule of Horror/TreeIt Wind" พร้อมหา texture ข้างๆ มาใส่ให้อัตโนมัติ:
///     xxx.png              → Base Map
///     xxx_Normal.png       → Normal Map
///     xxx_Transmission.png → Transmission Map
/// เลือก material (หรือโฟลเดอร์) ใน Project แล้วกดเมนู
/// </summary>
public static class TreeItMaterialConverter
{
    private const string ShaderName = "Rule of Horror/TreeIt Wind";

    [MenuItem("Rule of Horror/TreeIt/แปลง Material ที่เลือกเป็น TreeIt Wind")]
    private static void ConvertSelected()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[TreeIt] หา shader '{ShaderName}' ไม่เจอ — ตรวจว่า Assets/Shaders/TreeIt_Wind.shader compile ผ่านไหม");
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
        Debug.Log($"[TreeIt] แปลงแล้ว {count} material → {ShaderName}");
    }

    [MenuItem("Rule of Horror/TreeIt/แปลง Material ที่เลือกเป็น TreeIt Wind", true)]
    private static bool ConvertSelectedValidate() => Selection.objects.Length > 0;

    private static bool Convert(Material mat, Shader shader)
    {
        if (mat == null) return false;

        // เก็บ texture เดิมไว้ก่อนเปลี่ยน shader (property จะหายถ้าชื่อไม่ตรง)
        Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                        : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture bump    = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

        Undo.RecordObject(mat, "Convert to TreeIt Wind");
        mat.shader = shader;

        if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);

        // หา texture คู่กันจากชื่อไฟล์ base map
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

            // ใบต้องอ่าน alpha จาก png — TreeIt export มาโดยไม่ได้ติ๊ก Alpha Is Transparency
            if (AssetImporter.GetAtPath(basePath) is TextureImporter imp && !imp.alphaIsTransparency)
            {
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
        }

        // ใบเรนเดอร์สองด้าน + cutout
        mat.SetFloat("_Cull", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mat);
        return true;
    }
}
