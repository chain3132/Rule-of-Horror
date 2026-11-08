using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rule6;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes bus windows solid for the player.
///
/// Ways a window lets the CharacterController through even though the bus "has colliders":
///   1. the glass part simply has no collider (Bus4 has one part out of 66 without one)
///   2. the glass is a single-sided plane - a MeshCollider made from it is only solid from the side
///      its face points to, and the player walks into it from the other side
///   3. the bus is a SKINNED mesh (Bus1: one mesh bent by an Armature). A MeshCollider on it uses
///      the raw mesh in the renderer's own space, not the shape the bones draw, so the collider can
///      sit offset / scaled away from the bus you see
///
/// For every renderer under a Rule6Bus (Mesh or Skinned) that uses a material with "Glass" in its
/// name, this bakes the mesh exactly as it is drawn right now, adds every triangle a second time
/// wound the other way, saves it in a "Colliders" folder next to the model and puts it on a
/// MeshCollider on that object (adding one if missing). Only the collider uses it; rendering is
/// untouched. Select a bus (or anything inside it) to fix just that one; nothing selected = all.
/// Safe to re-run.
/// </summary>
public static class BusWindowColliderFixer
{
    private const string Suffix = " (double-sided)";

    [MenuItem("Rule of Horror/Rule 6/Fix Bus Window Colliders", false, 40)]
    private static void Fix()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        var selected = Selection.gameObjects
                                .Select(g => g.GetComponentInParent<Rule6Bus>(true))
                                .Where(b => b != null)
                                .Distinct()
                                .ToList();

        var buses = selected.Count > 0
            ? selected
            : Object.FindObjectsByType<Rule6Bus>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(b => b.gameObject.scene == scene)
                    .OrderBy(b => b.name)
                    .ToList();

        if (buses.Count == 0)
        {
            EditorUtility.DisplayDialog("Fix Bus Window Colliders", "No Rule6Bus in the open scene.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Fix Bus Window Colliders");
        int group = Undo.GetCurrentGroup();

        var report = new StringBuilder("[Rule 6] Bus window colliders" +
                                       (selected.Count > 0 ? " (selected bus only)" : "")).AppendLine();
        int added = 0, replaced = 0;

        foreach (var bus in buses)
        {
            var glass = bus.GetComponentsInChildren<Renderer>(true)
                           .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                           .Where(r => r.sharedMaterials.Any(m => m != null && m.name.ToLowerInvariant().Contains("glass")))
                           .ToList();

            if (glass.Count == 0)
            {
                report.AppendLine($"  ! {bus.name}: no renderer with a Glass material - nothing to do");
                continue;
            }

            foreach (var renderer in glass)
            {
                if (!renderer.gameObject.activeInHierarchy)
                {
                    report.AppendLine($"  - {bus.name} / {renderer.name}: inactive model - skipped");
                    continue;
                }

                var source = DrawnMesh(renderer, out bool baked);
                if (source == null) continue;

                var original = renderer is SkinnedMeshRenderer sk ? sk.sharedMesh
                             : renderer.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                var collider = renderer.GetComponents<MeshCollider>().FirstOrDefault(c => !c.isTrigger);

                // The model's collider may sit on another object (root, armature) - it would still
                // use the raw mesh in the wrong space, so say where it is
                if (collider == null)
                    foreach (var other in bus.GetComponentsInChildren<MeshCollider>(true))
                        if (other.sharedMesh == original)
                            report.AppendLine($"  ! {bus.name} / {other.name}: another collider uses the same raw mesh " +
                                              $"(off by {Offset(other.bounds, renderer.bounds)}) - remove it if the bus feels off");

                Physics.SyncTransforms();
                string before = collider == null ? "had NO collider"
                              : collider.sharedMesh == null ? "collider had no mesh"
                              : $"collider was off by {Offset(collider.bounds, renderer.bounds)}";

                var twoSided = SaveDoubleSided(source, renderer, report);
                if (baked) Object.DestroyImmediate(source);
                if (twoSided == null) continue;

                if (collider == null)
                {
                    collider = Undo.AddComponent<MeshCollider>(renderer.gameObject);
                    added++;
                }
                else
                {
                    Undo.RecordObject(collider, "Fix Bus Window Colliders");
                    replaced++;
                }

                collider.convex     = false;
                collider.sharedMesh = twoSided;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);

                Physics.SyncTransforms();
                string after = Offset(collider.bounds, renderer.bounds);
                report.AppendLine($"  - {bus.name} / {renderer.name} ({(baked ? "skinned, baked" : "mesh")}): " +
                                  $"{before} -> now double-sided, off by {after}" +
                                  (collider.enabled ? "" : "  ! the collider is DISABLED - tick it on"));
            }
        }

        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);

        report.AppendLine($"  = {added} added, {replaced} replaced. Save the scene.");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// The mesh as it is drawn, in the renderer's own local space (what a MeshCollider on the same
    /// object expects). For a skinned mesh that means baking the bones' current pose.
    /// </summary>
    private static Mesh DrawnMesh(Renderer renderer, out bool baked)
    {
        baked = false;

        if (renderer is SkinnedMeshRenderer skinned)
        {
            if (skinned.sharedMesh == null) return null;

            // BakeMesh with and without the transform's scale differ exactly by that scale - keep
            // whichever lands on the drawn bus, judged against the renderer's world bounds
            var withScale    = new Mesh();
            var withoutScale = new Mesh();
            skinned.BakeMesh(withScale, true);
            skinned.BakeMesh(withoutScale, false);

            var t = skinned.transform;
            float a = Mismatch(WorldBounds(withScale, t),    skinned.bounds);
            float b = Mismatch(WorldBounds(withoutScale, t), skinned.bounds);

            baked = true;
            if (a <= b) { Object.DestroyImmediate(withoutScale); withScale.name = skinned.sharedMesh.name;    return withScale; }
            Object.DestroyImmediate(withScale);                  withoutScale.name = skinned.sharedMesh.name; return withoutScale;
        }

        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static Bounds WorldBounds(Mesh mesh, Transform t)
    {
        var b = mesh.bounds;
        var result = new Bounds(t.TransformPoint(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            result.Encapsulate(t.TransformPoint(corner));
        }
        return result;
    }

    private static float Mismatch(Bounds a, Bounds b) =>
        (a.center - b.center).magnitude + (a.size - b.size).magnitude;

    private static string Offset(Bounds collider, Bounds drawn) =>
        $"{(collider.center - drawn.center).magnitude:0.00} m (size {collider.size.x:0.0}x{collider.size.y:0.0}x{collider.size.z:0.0} vs drawn {drawn.size.x:0.0}x{drawn.size.y:0.0}x{drawn.size.z:0.0})";

    /// <summary>Every triangle of every submesh, plus the same triangle wound the other way, in one submesh.</summary>
    private static Mesh SaveDoubleSided(Mesh source, Renderer renderer, StringBuilder report)
    {
        var original = renderer is SkinnedMeshRenderer s ? s.sharedMesh
                     : renderer.GetComponent<MeshFilter>() is MeshFilter f ? f.sharedMesh : null;

        string sourcePath = original != null ? AssetDatabase.GetAssetPath(original) : null;
        if (string.IsNullOrEmpty(sourcePath))
        {
            report.AppendLine($"  ! {renderer.name}: mesh is not an asset - skipped");
            return null;
        }

        string parent = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
        string folder = parent + "/Colliders";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, "Colliders");

        string model = Path.GetFileNameWithoutExtension(sourcePath);
        string file  = $"{folder}/{Clean(model)}_{Clean(source.name)}_DoubleSided.asset";

        var tris = new List<int>();
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
            tris.AddRange(source.GetTriangles(sub));
        }

        int count = tris.Count;
        for (int i = 0; i < count; i += 3)
        {
            tris.Add(tris[i]);
            tris.Add(tris[i + 2]);
            tris.Add(tris[i + 1]);
        }

        var mesh = new Mesh
        {
            name        = source.name + Suffix,
            indexFormat = source.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            vertices    = source.vertices,
        };
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(file);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, file);
        report.AppendLine($"  + {file}");
        return mesh;
    }

    private static string Clean(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }
}
