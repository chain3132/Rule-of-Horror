using System.Linq;
using System.Text;
using Rule6;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts a BusWheels component on every bus model in the open scene whose FBX carries a
/// "BusRunning" clip (Models/Wk10/Bus/Bus1..4), so the wheels spin while the bus drives and stop
/// when it parks.
///
/// The component goes on the model's root (the FBX instance, parent of "Armature") because the
/// clip's curve paths start at "Armature/...". An Animator is added there too if the model has
/// none - no controller needed, BusWheels drives it through a PlayableGraph.
///
/// Each bus gets the clip from its own FBX. Safe to re-run: existing components are re-pointed,
/// tuning values are left alone.
/// </summary>
public static class BusWheelsSetup
{
    [MenuItem("Rule of Horror/Rule 6/Setup Bus Wheel Animation", false, 41)]
    private static void Setup()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        Undo.SetCurrentGroupName("Setup Bus Wheel Animation");
        int group = Undo.GetCurrentGroup();

        var report = new StringBuilder("[Rule 6] Bus wheel animation").AppendLine();
        int added = 0, updated = 0;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            if (PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.Model) continue;

            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path)) continue;

            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                                    .OfType<AnimationClip>()
                                    .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.Contains("BusRunning"));
            if (clip == null) continue;

            if (t.Find("Armature") == null)
            {
                report.AppendLine($"  ! {PathOf(t)}: no child called 'Armature' - the clip would not move anything, skipped");
                continue;
            }

            if (!go.TryGetComponent(out Animator animator))
                animator = Undo.AddComponent<Animator>(go);
            else
                Undo.RecordObject(animator, "Setup Bus Wheel Animation");

            animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

            bool isNew = !go.TryGetComponent(out BusWheels wheels);
            if (isNew) wheels = Undo.AddComponent<BusWheels>(go);

            var so = new SerializedObject(wheels);
            so.FindProperty("runningClip").objectReferenceValue = clip;
            so.FindProperty("animator").objectReferenceValue    = animator;
            so.ApplyModifiedProperties();

            if (isNew) added++; else updated++;
            report.AppendLine($"  {(isNew ? "+" : "~")} {PathOf(t)}  <- {clip.name} ({path})" +
                              (go.activeInHierarchy ? "" : "  (inactive now - fine)"));
        }

        Undo.CollapseUndoOperations(group);

        if (added + updated == 0)
        {
            EditorUtility.DisplayDialog("Setup Bus Wheel Animation",
                "No bus model with a 'BusRunning' clip found in the open scene.", "OK");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        report.AppendLine($"  = {added} added, {updated} updated. Save the scene.");
        Debug.Log(report.ToString());
    }

    private static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
