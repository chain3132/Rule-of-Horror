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

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;

            float h = Mathf.Max(FirstLodTransition / Mathf.Pow(2f, i), 0.01f + 0.001f * (models.Count - i));
            lods.Add(new LOD(h, new Renderer[] { mr }));
        }

        if (lods.Count > 1)
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
