using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity's Terrain only draws the first Renderer of a prefab for trees that are not SpeedTree or
/// TreeCreator. TreeIt exports two separate meshes (trunk + leaves), so painting one straight onto
/// a Terrain shows the trunk only.
///
/// This tool merges every mesh in the FBX into a single mesh (one submesh per material) and builds
/// a prefab with the MeshRenderer on the root, plus an LODGroup assembled from the sibling
/// &lt;name&gt;_LOD1..N.fbx files when they exist.
/// Select the main FBX (BananaTree.fbx, say) in the Project window and run the menu — the prefab
/// that comes out can go straight onto a Terrain.
/// </summary>
public static class TreeItTerrainPrefabBuilder
{
    // Screen-height fraction where LOD0 gives up — each level after it halves (0.35 → 0.175 →
    // 0.0875 …) with a 1% floor. It has to keep decreasing, otherwise SetLODs errors out and
    // leaves the LOD Group empty, and the Terrain then draws nothing at all.
    private const float FirstLodTransition = 0.35f;

    // Screen-height fraction where the last LOD disappears — kept low so distant trees stay
    // visible. (The Terrain already limits range with Tree Distance; letting this value do the
    // culling cuts them off far too early.)
    private const float LastLodCull = 0.002f;

    [MenuItem("Rule of Horror/TreeIt/Build Terrain Tree Prefab (from selected FBX)")]
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

    [MenuItem("Rule of Horror/TreeIt/Build Terrain Tree Prefab (from selected FBX)", true)]
    private static bool BuildValidate() => Selection.activeObject is GameObject go && AssetDatabase.GetAssetPath(go).ToLower().EndsWith(".fbx");

    private static void BuildFor(GameObject lod0Model, string lod0Path)
    {
        // AssetDatabase always wants "/" — Path.* returns "\\" on Windows and LoadAssetAtPath
        // then silently returns null
        string dir    = Path.GetDirectoryName(lod0Path).Replace('\\', '/');
        string name   = Path.GetFileNameWithoutExtension(lod0Path);
        string outDir = $"{dir}/{name}_Terrain";
        if (!AssetDatabase.IsValidFolder(outDir)) AssetDatabase.CreateFolder(dir, name + "_Terrain");

        // Collect every level: the main FBX plus whichever _LOD1, _LOD2, … sit next to it
        var models = new List<GameObject> { lod0Model };
        for (int i = 1; i < 16; i++)
        {
            var lod = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}_LOD{i}.fbx");
            if (lod == null) break;
            // The imposter (billboard) uses a different texture set — skip it and let the previous
            // LOD do the culling instead
            var firstMesh = lod.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (firstMesh == null || firstMesh.name.ToLower().Contains("imposter") || firstMesh.vertexCount <= 8) break;
            models.Add(lod);
        }

        var root = new GameObject(name + "_Terrain");
        var lods = new List<LOD>();

        // LOD0's materials, used as the reference for every other level.
        // The _LOD1..N.fbx files carry their own embedded materials (the URP/Lit ones Unity creates
        // on import). Left alone, the distant LODs are not on the tree shader — no wind, no
        // instancing (the SRP Batcher takes over instead) — and it is baffling to debug, because
        // LOD0, the one you see up close, looks completely correct.
        Material[] lod0Materials = null;

        for (int i = 0; i < models.Count; i++)
        {
            if (!TryCombine(models[i], out Mesh mesh, out Material[] mats))
            {
                Debug.LogWarning($"[TreeIt] {models[i].name} has no mesh — skipped");
                continue;
            }

            mesh.name = $"{name}_LOD{i}";
            AssetDatabase.CreateAsset(mesh, $"{outDir}/{mesh.name}.asset");

            // LOD0 lives on the root (the Terrain reads the first renderer); the rest are children
            GameObject go = i == 0 ? root : new GameObject($"LOD{i}");
            if (i != 0) go.transform.SetParent(root.transform, false);

            if (i == 0) lod0Materials = mats;
            else         mats = RemapToLod0(mats, lod0Materials, models[i].name);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;

            // The last LOD has to cull at a very low value, otherwise trees vanish while still
            // inside Tree Distance. (With a single LOD this is the one and only cull point.)
            bool  isLast = i == models.Count - 1;
            float h = isLast
                ? LastLodCull
                : Mathf.Max(FirstLodTransition / Mathf.Pow(2f, i), LastLodCull + 0.001f * (models.Count - i));
            lods.Add(new LOD(h, new Renderer[] { mr }));
        }

        // There must ALWAYS be an LODGroup, even with a single LOD.
        // Terrain: a prototype without one goes through the default impostor system (Unity says so
        //          itself in the Paint Trees inspector). Past Tree Billboard Distance it stops
        //          drawing the mesh and draws a billboard it generates, which only supports the
        //          Nature/Soft Occlusion shader family — anything else turns into colored speckles.
        // With an LODGroup the Terrain draws the mesh normally at every distance and never touches
        // the impostor system.
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
            Debug.LogWarning($"[TreeIt] No {name}_LOD1.fbx found next to it — the prefab has no LODs");

        Debug.Log($"[TreeIt] Built {prefabPath} ({lods.Count} LOD(s)) — drag it into " +
                  "Terrain ▸ Paint Trees ▸ Edit Trees",
                  AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    }

    /// <summary>
    /// Matches an LOD's materials to LOD0's — by name first, falling back to submesh order.
    /// (A material embedded in an LOD's FBX always has the same name as LOD0's standalone one,
    /// since both come from the same model.)
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

            if (match == null && i < lod0Mats.Length) match = lod0Mats[i];   // no name match → go by order

            if (match == null)
            {
                result[i] = lodMats[i];
                Debug.LogWarning($"[TreeIt] {lodName}: no counterpart for material '{wanted}' in LOD0 — " +
                                 "this LOD keeps the material embedded in the FBX (a different shader from LOD0)");
            }
            else result[i] = match;
        }
        return result;
    }

    /// <summary>Merges every MeshFilter in the model into one mesh — one submesh per material,
    /// with the child transforms baked in.</summary>
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

            // Pass 1: merge all the pieces that share a material into one lump
            var perMaterial = new List<CombineInstance>();
            foreach (var mat in order)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(byMaterial[mat].ToArray(), mergeSubMeshes: true, useMatrices: true);
                perMaterial.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
            }

            // Pass 2: chain those into submeshes in material order — vertex colors, tangents and
            // UVs all come along
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
