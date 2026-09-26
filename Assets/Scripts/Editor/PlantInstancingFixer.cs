using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain วาดต้นไม้/พืชด้วย DrawMeshInstanced — แต่จะรวมเป็น batch เดียวได้ก็ต่อเมื่อ
/// material ติ๊ก "Enable GPU Instancing" (ในไฟล์ .mat คือ m_EnableInstancingVariants: 1)
/// ถ้าไม่ติ๊ก = 1 draw call ต่อต้นต่อ submesh → Batches พุ่งเป็นหมื่นทันทีเมื่อ paint เยอะ
///
/// เมนูนี้เปิดให้ทีเดียวทั้งโฟลเดอร์ + รายงานว่าแต่ละ material มี submesh/material กี่ตัว
/// (จำนวน material ต่อ prefab คือตัวคูณของ draw call ที่เหลืออยู่หลังเปิด instancing แล้ว)
/// </summary>
public static class PlantInstancingFixer
{
    [MenuItem("Rule of Horror/TreeIt/เปิด GPU Instancing ให้ Material ที่เลือก (โฟลเดอร์ได้)", false, 40)]
    private static void EnableInstancing()
    {
        var mats = CollectMaterials();
        if (mats.Count == 0)
        {
            Debug.LogWarning("[Instancing] ไม่พบ material ใน selection — เลือกไฟล์ .mat หรือโฟลเดอร์ใน Project แล้วลองอีกครั้ง");
            return;
        }

        int changed = 0;
        foreach (var mat in mats)
        {
            if (mat.enableInstancing) continue;

            Undo.RecordObject(mat, "Enable GPU Instancing");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            changed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Instancing] เปิด GPU Instancing เพิ่ม {changed} material (จากที่เลือกมา {mats.Count}) — " +
                  "กลับไปดู Stats ตอน Play อีกครั้ง Batches ควรลดลงมาก");
    }

    [MenuItem("Rule of Horror/TreeIt/เปิด GPU Instancing ให้ Material ที่เลือก (โฟลเดอร์ได้)", true)]
    private static bool EnableInstancingValidate() => Selection.objects.Length > 0;

    /// <summary>
    /// ตรวจ prefab ต้นไม้ที่เลือก: material กี่ตัว (= draw call ต่อต้น), มี LOD ไหม, cast shadow ไหม
    /// ใช้ไล่ว่าทำไม Batches ยังเยอะหลังเปิด instancing แล้ว
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/ตรวจ Prefab ต้นไม้ที่เลือก (material / LOD / เงา)", false, 41)]
    private static void AuditPrefabs()
    {
        int audited = 0;

        foreach (string guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            foreach (string prefabPath in ExpandPrefabs(path))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (go == null) continue;

                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                var lodGroup  = go.GetComponent<LODGroup>();

                int  matCount     = 0;
                int  missingInst  = 0;
                int  triCount     = 0;
                bool castsShadows = false;

                foreach (var r in renderers)
                {
                    // Terrain วาดแค่ renderer ตัวแรก (หรือชุด LOD) — นับ material ของ LOD0 เป็นตัวคูณหลัก
                    if (r.transform == go.transform || lodGroup == null)
                    {
                        matCount += r.sharedMaterials.Length;

                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) triCount += mf.sharedMesh.triangles.Length / 3;
                    }

                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) castsShadows = true;

                    foreach (var m in r.sharedMaterials)
                        if (m != null && !m.enableInstancing) missingInst++;
                }

                audited++;
                string lod = lodGroup != null ? $"LOD {lodGroup.lodCount} ระดับ" : "ไม่มี LOD (วาดเต็มความละเอียดทุกระยะ)";

                Debug.Log($"[Instancing] {Path.GetFileNameWithoutExtension(prefabPath)}: " +
                          $"{matCount} material/ต้น (= draw call ต่อต้น) | {triCount:N0} tris ที่ LOD0 | {lod} | " +
                          $"เงา {(castsShadows ? "เปิด (คูณ draw call ตามจำนวน cascade)" : "ปิด")}" +
                          (missingInst > 0 ? $" | ⚠ ยังมี {missingInst} material ที่ไม่ได้เปิด GPU Instancing" : ""),
                          go);
            }
        }

        if (audited == 0)
            Debug.LogWarning("[Instancing] ไม่พบ prefab ใน selection — เลือก prefab หรือโฟลเดอร์ที่มี prefab");
    }

    [MenuItem("Rule of Horror/TreeIt/ตรวจ Prefab ต้นไม้ที่เลือก (material / LOD / เงา)", true)]
    private static bool AuditPrefabsValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// ซ่อม prefab ที่สร้างไว้ก่อนหน้า: บังคับให้ทุก LOD ใช้ material ชุดเดียวกับ LOD0
    ///
    /// ไฟล์ _LOD1..N.fbx มี material ของตัวเองฝังอยู่ (URP/Lit ที่ Unity สร้างตอน import)
    /// prefab รุ่นเก่าจึงมี LOD ไกลๆ ที่ยังเป็น URP/Lit → ไม่ได้ instancing และไม่มีลม
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/ซ่อม Material ของ LOD ใน Prefab ที่เลือก (ใช้ของ LOD0)", false, 42)]
    private static void FixLodMaterials()
    {
        int fixedRenderers = 0, prefabs = 0;

        foreach (string guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            foreach (string prefabPath in ExpandPrefabs(path))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (go == null) continue;

                var rootRenderer = go.GetComponent<MeshRenderer>();
                if (rootRenderer == null)
                {
                    Debug.LogWarning($"[Instancing] {go.name}: ไม่มี MeshRenderer ที่ root — ข้าม " +
                                     "(prefab นี้ไม่ได้สร้างจากเมนู 'สร้าง Prefab ต้นไม้สำหรับ Terrain')", go);
                    continue;
                }

                Material[] lod0 = rootRenderer.sharedMaterials;
                bool dirty = false;

                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == rootRenderer) continue;

                    var mats    = r.sharedMaterials;
                    var updated = new Material[mats.Length];
                    bool changed = false;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        updated[i] = mats[i];
                        if (mats[i] == null) continue;

                        Material match = null;
                        foreach (var m in lod0)
                            if (m != null && m.name == mats[i].name) { match = m; break; }

                        if (match == null && i < lod0.Length) match = lod0[i];
                        if (match == null || match == mats[i]) continue;

                        updated[i] = match;
                        changed = true;
                    }

                    if (!changed) continue;

                    r.sharedMaterials = updated;
                    fixedRenderers++;
                    dirty = true;
                }

                if (!dirty) continue;

                PrefabUtility.SavePrefabAsset(go);
                prefabs++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Instancing] ซ่อม {fixedRenderers} renderer ใน {prefabs} prefab — " +
                  "ทุก LOD ใช้ material ชุดเดียวกับ LOD0 แล้ว");
    }

    [MenuItem("Rule of Horror/TreeIt/ซ่อม Material ของ LOD ใน Prefab ที่เลือก (ใช้ของ LOD0)", true)]
    private static bool FixLodMaterialsValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// แก้ค่า import ของ texture ต้นไม้/พืช 3 อย่างที่ TreeIt export มาแล้ว Unity เดาผิด:
    ///
    ///   1. *_normal → Texture Type ต้องเป็น "Normal map" (ตอน import มาเป็น Default + sRGB)
    ///      shader เรียก UnpackNormalScale ซึ่งอ่านช่อง A/G ของ texture ที่บีบอัดแบบ normal map
    ///      ถ้า import เป็นภาพสีธรรมดา ค่าที่อ่านได้เป็นขยะ → normal มั่ว → แสงตกกระทบเพี้ยนเป็นสีแปลกๆ
    ///
    ///   2. ใบไม้ที่มี alpha → เปิด "Mip Maps ▸ Preserve Coverage"
    ///      ไม่เปิด = ยิ่งไกล mip ยิ่งเฉลี่ย alpha ลง ใบจะค่อยๆ ละลายเหลือเป็นจุดๆ
    ///      (อาการ "อยู่ไกลแล้วเห็นเป็นจุดสีประหลาด พอเดินเข้าไปใกล้กลับปกติ")
    ///
    ///   3. *_roughness / *_transmission → เป็นข้อมูล ไม่ใช่สี ต้องปิด sRGB
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/แก้ค่า Import ของ Texture ต้นไม้ที่เลือก (normal map / mip coverage)", false, 43)]
    private static void FixTextureImport()
    {
        const float leafCutoff = 0.4f;   // ให้ตรงกับ _Cutoff ของ shader

        int normals = 0, coverage = 0, linear = 0, total = 0;

        foreach (string guid in Selection.assetGUIDs)
        {
            string root = AssetDatabase.GUIDToAssetPath(guid);
            string[] paths = AssetDatabase.IsValidFolder(root)
                ? System.Array.ConvertAll(AssetDatabase.FindAssets("t:Texture2D", new[] { root }), AssetDatabase.GUIDToAssetPath)
                : new[] { root };

            foreach (string path in paths)
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter imp) continue;

                string lower  = Path.GetFileNameWithoutExtension(path).ToLower();
                bool   change = false;
                total++;

                // 1. normal map
                if ((lower.EndsWith("_normal") || lower.EndsWith("_norm")) &&
                    imp.textureType != TextureImporterType.NormalMap)
                {
                    imp.textureType = TextureImporterType.NormalMap;
                    normals++; change = true;
                }
                // 3. map ที่เป็นข้อมูล ไม่ใช่สี
                else if ((lower.EndsWith("_roughness") || lower.EndsWith("_transmission") ||
                          lower.EndsWith("_metallic")  || lower.EndsWith("_mask")) && imp.sRGBTexture)
                {
                    imp.sRGBTexture = false;
                    linear++; change = true;
                }
                // 2. base map ของใบ/เปลือก — เปิด Preserve Coverage เสมอ
                //    (texture ทึบไม่มี alpha ก็ไม่เสียหาย เพราะ alpha = 1 ทุกพิกเซลอยู่แล้ว
                //     ไม่เช็คด้วย DoesSourceTextureHaveAlpha() เพราะบางไฟล์มันตอบ false ทั้งที่มี alpha)
                else
                {
                    if (!imp.mipmapEnabled)           { imp.mipmapEnabled = true;           change = true; }
                    if (!imp.mipMapsPreserveCoverage) { imp.mipMapsPreserveCoverage = true; change = true; }
                    if (!Mathf.Approximately(imp.alphaTestReferenceValue, leafCutoff))
                    {
                        imp.alphaTestReferenceValue = leafCutoff;
                        change = true;
                    }
                    if (imp.DoesSourceTextureHaveAlpha() && !imp.alphaIsTransparency)
                    {
                        imp.alphaIsTransparency = true;
                        change = true;
                    }
                    if (change) coverage++;
                }

                if (!change) continue;

                imp.SaveAndReimport();
                Debug.Log($"[Instancing] แก้ import: {Path.GetFileName(path)} " +
                          $"(type={imp.textureType}, preserveCoverage={imp.mipMapsPreserveCoverage}, " +
                          $"cutoff={imp.alphaTestReferenceValue:0.00}, sRGB={imp.sRGBTexture})",
                          AssetDatabase.LoadAssetAtPath<Texture>(path));
            }
        }

        Debug.Log($"[Instancing] ตรวจ texture {total} ไฟล์ — ตั้งเป็น Normal map {normals} ไฟล์, " +
                  $"เปิด Preserve Coverage {coverage} ไฟล์, ปิด sRGB (map ข้อมูล) {linear} ไฟล์");
    }

    [MenuItem("Rule of Horror/TreeIt/แก้ค่า Import ของ Texture ต้นไม้ที่เลือก (normal map / mip coverage)", true)]
    private static bool FixTextureImportValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// เติม LOD Group ให้ prefab ต้นไม้ที่ยังไม่มี
    ///
    /// Terrain มีกฎ: tree prototype ที่ไม่มี LODGroup จะถูกส่งเข้า "default impostor system"
    /// พอเลยระยะ Tree Billboard Distance มันจะเลิกวาด mesh แล้ววาด billboard ที่ Unity สร้างเอง
    /// ซึ่งทำงานถูกต้องเฉพาะ shader ตระกูล Nature/Soft Occlusion
    /// เจอ shader อื่น (เช่น TreeIt Wind) จะได้ภาพมั่วเป็นจุดสีตอนมองไกล
    ///
    /// แค่มี LODGroup (แม้จะมี LOD เดียว) Terrain ก็จะวาด mesh ตามปกติทุกระยะ
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/เติม LOD Group ให้ Prefab ที่เลือก (กัน Terrain ใช้ระบบ impostor)", false, 44)]
    private static void AddLodGroups()
    {
        const float cullScreenHeight = 0.002f;   // หายเมื่อเล็กกว่า 0.2% ของความสูงจอ

        int added = 0, already = 0;

        foreach (string guid in Selection.assetGUIDs)
        {
            foreach (string prefabPath in ExpandPrefabs(AssetDatabase.GUIDToAssetPath(guid)))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (go == null) continue;

                if (go.GetComponent<LODGroup>() != null) { already++; continue; }

                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogWarning($"[Instancing] {go.name}: ไม่มี MeshRenderer — ข้าม", go);
                    continue;
                }

                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(cullScreenHeight, renderers) });
                group.RecalculateBounds();

                PrefabUtility.SavePrefabAsset(go);
                added++;
                Debug.Log($"[Instancing] เติม LOD Group ให้ {go.name} ({renderers.Length} renderer) " +
                          $"— Terrain จะเลิกใช้ระบบ impostor กับต้นนี้แล้ว", go);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Instancing] เติม LOD Group {added} prefab (มีอยู่แล้ว {already}) — " +
                  "กลับไปที่ Terrain ▸ Paint Trees ▸ กดปุ่ม Refresh ด้วย");
    }

    [MenuItem("Rule of Horror/TreeIt/เติม LOD Group ให้ Prefab ที่เลือก (กัน Terrain ใช้ระบบ impostor)", true)]
    private static bool AddLodGroupsValidate() => Selection.assetGUIDs.Length > 0;

    // ─────────────────────────── helpers ───────────────────────────

    private static List<Material> CollectMaterials()
    {
        var result = new List<Material>();
        var seen   = new HashSet<Material>();

        foreach (string guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { path }))
                    Add(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)));
            }
            else Add(AssetDatabase.LoadAssetAtPath<Material>(path));
        }

        // material ที่ฝังมากับ FBX เลือกตรงๆ ไม่ได้ — เก็บจาก object ที่เลือกด้วย
        foreach (Object obj in Selection.objects)
            if (obj is Material m) Add(m);

        void Add(Material m)
        {
            if (m != null && seen.Add(m)) result.Add(m);
        }

        return result;
    }

    private static IEnumerable<string> ExpandPrefabs(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
                yield return AssetDatabase.GUIDToAssetPath(g);
        }
        else if (path.EndsWith(".prefab")) yield return path;
    }
}
