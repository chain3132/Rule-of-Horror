using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Terrain draws trees and plants through DrawMeshInstanced, but it can only merge them into
/// one batch when the material has "Enable GPU Instancing" ticked
/// (m_EnableInstancingVariants: 1 in the .mat file).
/// Without it you pay one draw call per tree per submesh, so Batches jumps into the tens of
/// thousands as soon as a few thousand trees are painted.
///
/// This menu turns it on for a whole folder at once and reports how many materials each prefab
/// has — that material count is the multiplier on the draw calls that remain after instancing.
/// </summary>
public static class PlantInstancingFixer
{
    [MenuItem("Rule of Horror/TreeIt/Enable GPU Instancing On Selection (folders work)", false, 40)]
    private static void EnableInstancing()
    {
        var mats = CollectMaterials();
        if (mats.Count == 0)
        {
            Debug.LogWarning("[Instancing] No material in the selection — select .mat files or a folder " +
                             "in the Project window and try again");
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
        Debug.Log($"[Instancing] Enabled GPU Instancing on {changed} more material(s) out of {mats.Count} " +
                  "selected — check Stats again in Play mode, Batches should drop a lot");
    }

    [MenuItem("Rule of Horror/TreeIt/Enable GPU Instancing On Selection (folders work)", true)]
    private static bool EnableInstancingValidate() => Selection.objects.Length > 0;

    /// <summary>
    /// Audits the selected tree prefabs: how many materials (= draw calls per tree), whether there
    /// is an LOD group, and whether they cast shadows.
    /// Use it to work out why Batches is still high after instancing is on.
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/Audit Selected Tree Prefabs (materials / LOD / shadows)", false, 41)]
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
                    // The Terrain only draws the first renderer (or the LOD set), so LOD0's material
                    // count is the multiplier that actually matters
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
                string lod = lodGroup != null
                    ? $"{lodGroup.lodCount} LOD level(s)"
                    : "no LOD (drawn at full detail at every distance)";

                Debug.Log($"[Instancing] {Path.GetFileNameWithoutExtension(prefabPath)}: " +
                          $"{matCount} material(s)/tree (= draw calls per tree) | {triCount:N0} tris at LOD0 | {lod} | " +
                          $"shadows {(castsShadows ? "on (multiplies draw calls by the cascade count)" : "off")}" +
                          (missingInst > 0 ? $" | ⚠ {missingInst} material(s) still without GPU Instancing" : ""),
                          go);
            }
        }

        if (audited == 0)
            Debug.LogWarning("[Instancing] No prefab in the selection — select a prefab or a folder containing one");
    }

    [MenuItem("Rule of Horror/TreeIt/Audit Selected Tree Prefabs (materials / LOD / shadows)", true)]
    private static bool AuditPrefabsValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// Repairs older prefabs by forcing every LOD to use the same materials as LOD0.
    ///
    /// The _LOD1..N.fbx files carry their own embedded materials (the URP/Lit ones Unity creates on
    /// import), so prefabs built before this fix have distant LODs still on URP/Lit — no instancing
    /// and no wind.
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/Fix LOD Materials On Selected Prefabs (use LOD0's)", false, 42)]
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
                    Debug.LogWarning($"[Instancing] {go.name}: no MeshRenderer on the root — skipped " +
                                     "(this prefab did not come from the 'Build Terrain Tree Prefab' menu)", go);
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
        Debug.Log($"[Instancing] Fixed {fixedRenderers} renderer(s) across {prefabs} prefab(s) — " +
                  "every LOD now uses LOD0's materials");
    }

    [MenuItem("Rule of Horror/TreeIt/Fix LOD Materials On Selected Prefabs (use LOD0's)", true)]
    private static bool FixLodMaterialsValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// Fixes the three import settings Unity guesses wrong on textures exported from TreeIt:
    ///
    ///   1. *_normal → Texture Type must be "Normal map" (it imports as Default + sRGB).
    ///      The shader calls UnpackNormalScale, which reads the A/G channels of a texture
    ///      compressed as a normal map. Imported as an ordinary color image those channels are
    ///      garbage, so the normals are wrong and lighting comes out in strange colors.
    ///
    ///   2. Leaves with alpha → turn on "Mip Maps ▸ Preserve Coverage".
    ///      Without it each mip level averages the alpha down, so leaves dissolve into speckles
    ///      with distance — the "odd colored dots far away that look fine up close" symptom.
    ///
    ///   3. *_roughness / *_transmission → data, not color, so sRGB must be off.
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/Fix Texture Import On Selection (normal map / mip coverage)", false, 43)]
    private static void FixTextureImport()
    {
        const float leafCutoff = 0.4f;   // keep in sync with the shader's _Cutoff

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
                // 3. maps that hold data rather than color
                else if ((lower.EndsWith("_roughness") || lower.EndsWith("_transmission") ||
                          lower.EndsWith("_metallic")  || lower.EndsWith("_mask")) && imp.sRGBTexture)
                {
                    imp.sRGBTexture = false;
                    linear++; change = true;
                }
                // 2. leaf/bark base maps — always turn Preserve Coverage on
                //    (harmless on an opaque texture, where alpha is 1 everywhere anyway;
                //     we do not gate on DoesSourceTextureHaveAlpha() because it answers false
                //     on some files that do have alpha)
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
                Debug.Log($"[Instancing] Reimported: {Path.GetFileName(path)} " +
                          $"(type={imp.textureType}, preserveCoverage={imp.mipMapsPreserveCoverage}, " +
                          $"cutoff={imp.alphaTestReferenceValue:0.00}, sRGB={imp.sRGBTexture})",
                          AssetDatabase.LoadAssetAtPath<Texture>(path));
            }
        }

        Debug.Log($"[Instancing] Checked {total} texture(s) — set {normals} to Normal map, " +
                  $"turned on Preserve Coverage for {coverage}, turned off sRGB on {linear} data map(s)");
    }

    [MenuItem("Rule of Horror/TreeIt/Fix Texture Import On Selection (normal map / mip coverage)", true)]
    private static bool FixTextureImportValidate() => Selection.assetGUIDs.Length > 0;

    /// <summary>
    /// Adds an LOD Group to tree prefabs that do not have one.
    ///
    /// The Terrain has a rule: a tree prototype without an LODGroup goes through the default
    /// impostor system, and past Tree Billboard Distance it stops drawing the mesh and draws a
    /// billboard Unity generates itself. That only works correctly for the Nature/Soft Occlusion
    /// shader family — anything else (TreeIt Wind included) turns into colored speckles at range.
    ///
    /// Simply having an LODGroup, even with a single LOD, makes the Terrain draw the mesh normally
    /// at every distance.
    /// </summary>
    [MenuItem("Rule of Horror/TreeIt/Add LOD Groups To Selected Prefabs (avoid Terrain impostors)", false, 44)]
    private static void AddLodGroups()
    {
        const float cullScreenHeight = 0.002f;   // disappears below 0.2% of the screen height

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
                    Debug.LogWarning($"[Instancing] {go.name}: no MeshRenderer — skipped", go);
                    continue;
                }

                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(cullScreenHeight, renderers) });
                group.RecalculateBounds();

                PrefabUtility.SavePrefabAsset(go);
                added++;
                Debug.Log($"[Instancing] Added an LOD Group to {go.name} ({renderers.Length} renderer(s)) " +
                          "— the Terrain will stop using impostors for this tree", go);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Instancing] Added LOD Groups to {added} prefab(s), {already} already had one — " +
                  "remember to hit Refresh under Terrain ▸ Paint Trees");
    }

    [MenuItem("Rule of Horror/TreeIt/Add LOD Groups To Selected Prefabs (avoid Terrain impostors)", true)]
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

        // Materials embedded in an FBX cannot be selected as assets — pick them up from the
        // selected objects as well
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
