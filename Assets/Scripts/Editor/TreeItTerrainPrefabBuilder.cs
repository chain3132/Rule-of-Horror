using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain ของ Unity วาดต้นไม้ที่ไม่ใช่ SpeedTree/TreeCreator ได้แค่ "Renderer ตัวแรก" ของ prefab
/// TreeIt export มา 2 mesh (ลำต้น + ใบ) แยกกัน → ลง Terrain แล้วเห็นแต่ลำต้น
///
/// tool นี้รวม mesh ทุกชิ้นของ FBX เป็น mesh เดียว (แยก submesh ตาม material) แล้วสร้าง prefab
/// ที่มี MeshRenderer อยู่ที่ root + LODGroup จากไฟล์ <ชื่อ>_LOD1..N.fbx ข้างๆ (ถ้ามี)
/// เลือกไฟล์ FBX หลัก (เช่น BananaTree.fbx) ใน Project แล้วกดเมนู → ได้ prefab ไปใส่ Terrain ได้เลย
/// </summary>
public static class TreeItTerrainPrefabBuilder
{
    // สัดส่วนหน้าจอที่ LOD0 หมดอายุ — ระดับถัดไปหารครึ่งไปเรื่อยๆ (0.35 → 0.175 → 0.0875 …) cull ต่ำสุด 1%
    // ต้องลดลงเสมอ ไม่งั้น SetLODs จะ error แล้ว LOD Group ว่างเปล่า → Terrain วาดไม่ได้เลย
    private const float FirstLodTransition = 0.35f;

    // สัดส่วนหน้าจอที่ LOD สุดท้ายหายไป — ต่ำไว้เพื่อให้ยังเห็นต้นไม้ที่อยู่ไกล
    // (Terrain คุมระยะด้วย Tree Distance อยู่แล้ว ปล่อยให้ค่านี้เป็นตัว cull จะตัดเร็วเกินไป)
    private const float LastLodCull = 0.002f;

    [MenuItem("Rule of Horror/TreeIt/สร้าง Prefab ต้นไม้สำหรับ Terrain (จาก FBX ที่เลือก)")]
    private static void Build()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.ToLower().EndsWith(".fbx") || obj is not GameObject model) continue;
            BuildFor(model, path);
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Rule of Horror/TreeIt/สร้าง Prefab ต้นไม้สำหรับ Terrain (จาก FBX ที่เลือก)", true)]
    private static bool BuildValidate() => Selection.activeObject is GameObject go && AssetDatabase.GetAssetPath(go).ToLower().EndsWith(".fbx");

    private static void BuildFor(GameObject lod0Model, string lod0Path)
    {
        // AssetDatabase ต้องการ "/" เสมอ — Path.* บน Windows คืน "\\" แล้ว LoadAssetAtPath จะได้ null เงียบๆ
        string dir    = Path.GetDirectoryName(lod0Path).Replace('\\', '/');
        string name   = Path.GetFileNameWithoutExtension(lod0Path);
        string outDir = $"{dir}/{name}_Terrain";
        if (!AssetDatabase.IsValidFolder(outDir)) AssetDatabase.CreateFolder(dir, name + "_Terrain");

        // เก็บ FBX ทุกระดับ: ตัวหลัก + _LOD1, _LOD2, ... ที่มีอยู่ข้างๆ
        var models = new List<GameObject> { lod0Model };
        for (int i = 1; i < 16; i++)
        {
            var lod = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}_LOD{i}.fbx");
            if (lod == null) break;
            // Imposter (billboard) ใช้ texture คนละชุด — ข้าม ให้ LOD ก่อนหน้า cull แทน
            var firstMesh = lod.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (firstMesh == null || firstMesh.name.ToLower().Contains("imposter") || firstMesh.vertexCount <= 8) break;
            models.Add(lod);
        }

        var root = new GameObject(name + "_Terrain");
        var lods = new List<LOD>();

        // material ของ LOD0 — ใช้เป็นตัวอ้างอิงให้ LOD อื่นทั้งหมด
        // ไฟล์ _LOD1..N.fbx มี material ฝังอยู่ในตัวเอง (URP/Lit ที่ Unity สร้างให้ตอน import)
        // ถ้าปล่อยไว้ LOD ไกลๆ จะไม่ใช่ shader ต้นไม้ → ไม่มีลม ไม่ instanced (SRP Batcher กินไปแทน)
        // แล้วจะงงมาก เพราะ LOD0 ที่เห็นใกล้ๆ ดูถูกต้องทุกอย่าง
        Material[] lod0Materials = null;

        for (int i = 0; i < models.Count; i++)
        {
            if (!TryCombine(models[i], out Mesh mesh, out Material[] mats))
            {
                Debug.LogWarning($"[TreeIt] {models[i].name} ไม่มี mesh — ข้าม");
                continue;
            }

            mesh.name = $"{name}_LOD{i}";
            AssetDatabase.CreateAsset(mesh, $"{outDir}/{mesh.name}.asset");

            // LOD0 อยู่ที่ root (Terrain อ่าน renderer ตัวแรก) LOD อื่นเป็นลูก
            GameObject go = i == 0 ? root : new GameObject($"LOD{i}");
            if (i != 0) go.transform.SetParent(root.transform, false);

            if (i == 0) lod0Materials = mats;
            else         mats = RemapToLod0(mats, lod0Materials, models[i].name);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;

            // LOD สุดท้ายต้อง cull ที่ค่าต่ำมาก ไม่งั้นต้นไม้จะหายไปทั้งที่ยังอยู่ในระยะ Tree Distance
            // (กรณีมี LOD เดียว ค่านี้คือจุด cull จุดเดียวของทั้งต้น)
            bool  isLast = i == models.Count - 1;
            float h = isLast
                ? LastLodCull
                : Mathf.Max(FirstLodTransition / Mathf.Pow(2f, i), LastLodCull + 0.001f * (models.Count - i));
            lods.Add(new LOD(h, new Renderer[] { mr }));
        }

        // ต้องมี LODGroup "เสมอ" แม้จะมี LOD เดียว
        // Terrain: prototype ที่ไม่มี LODGroup จะถูกส่งเข้า "default impostor system"
        //          (Unity เตือนเองใน Inspector ของ Paint Trees)
        //          พอเลย Tree Billboard Distance มันจะเลิกวาด mesh แล้ววาด billboard ที่สร้างเอง
        //          ซึ่งรองรับแค่ shader ตระกูล Nature/Soft Occlusion — เจอ shader อื่นแล้วได้ภาพมั่วเป็นจุดสี
        // มี LODGroup = Terrain วาด mesh ตามปกติทุกระยะ ไม่แตะระบบ impostor เลย
        if (lods.Count > 0)
        {
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        string prefabPath = $"{outDir}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        if (lods.Count <= 1)
            Debug.LogWarning($"[TreeIt] หาไฟล์ {name}_LOD1.fbx ข้างๆ ไม่เจอ — ได้ prefab แบบไม่มี LOD");

        Debug.Log($"[TreeIt] สร้าง {prefabPath} ({lods.Count} LOD) — ลากใส่ Terrain ▸ Paint Trees ▸ Edit Trees ได้เลย",
                  AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    }

    /// <summary>
    /// จับ material ของ LOD ให้ตรงกับของ LOD0 — จับคู่ด้วยชื่อก่อน ถ้าไม่เจอค่อยใช้ลำดับ submesh
    /// (material ฝังใน FBX ของ LOD ชื่อเดียวกับตัวที่แยกไฟล์ไว้ของ LOD0 เสมอ เพราะมาจากโมเดลเดียวกัน)
    /// </summary>
    private static Material[] RemapToLod0(Material[] lodMats, Material[] lod0Mats, string lodName)
    {
        if (lod0Mats == null || lod0Mats.Length == 0) return lodMats;

        var result = new Material[lodMats.Length];

        for (int i = 0; i < lodMats.Length; i++)
        {
            string wanted = lodMats[i] != null ? lodMats[i].name : null;
            Material match = null;

            if (!string.IsNullOrEmpty(wanted))
                foreach (var m in lod0Mats)
                    if (m != null && m.name == wanted) { match = m; break; }

            if (match == null && i < lod0Mats.Length) match = lod0Mats[i];   // ชื่อไม่ตรง → ใช้ลำดับ

            if (match == null)
            {
                result[i] = lodMats[i];
                Debug.LogWarning($"[TreeIt] {lodName}: หา material คู่ของ '{wanted}' ใน LOD0 ไม่เจอ — " +
                                 "LOD นี้จะใช้ material ที่ฝังมากับ FBX (คนละ shader กับ LOD0)");
            }
            else result[i] = match;
        }
        return result;
    }

    /// <summary>รวม MeshFilter ทุกตัวใน model เป็น mesh เดียว — 1 submesh ต่อ 1 material (bake transform ลูกเข้าไป)</summary>
    private static bool TryCombine(GameObject model, out Mesh combined, out Material[] materials)
    {
        combined = null; materials = null;

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var order      = new List<Material>();

            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;

                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    Material mat = sub < mr.sharedMaterials.Length ? mr.sharedMaterials[sub] : null;
                    if (mat == null) continue;
                    if (!byMaterial.TryGetValue(mat, out var list))
                    {
                        byMaterial[mat] = list = new List<CombineInstance>();
                        order.Add(mat);
                    }
                    list.Add(new CombineInstance
                    {
                        mesh         = mf.sharedMesh,
                        subMeshIndex = sub,
                        transform    = inst.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix,
                    });
                }
            }
            if (order.Count == 0) return false;

            // ขั้น 1: รวมชิ้นที่ material เดียวกันเป็นก้อนเดียว
            var perMaterial = new List<CombineInstance>();
            foreach (var mat in order)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(byMaterial[mat].ToArray(), mergeSubMeshes: true, useMatrices: true);
                perMaterial.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
            }

            // ขั้น 2: ต่อเป็น submesh ตามลำดับ material — vertex color / tangent / uv ติดมาด้วยครบ
            combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.CombineMeshes(perMaterial.ToArray(), mergeSubMeshes: false, useMatrices: false);
            combined.RecalculateBounds();
            foreach (var ci in perMaterial) Object.DestroyImmediate(ci.mesh);

            materials = order.ToArray();
            return true;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }
}
