using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upgrades the old key-hint row prefabs (where Key was a plain TMP label) so they can host the
/// key prefabs from KeyIconLibrary.
///
/// Before                        After
///   Row  [HLG]                   Row  [HLG] + KeyHintRow
///   ├─ Key    (TMP)              ├─ KeyCap [HLG+CSF] + KeyCapView
///   └─ Action (TMP)              │  └─ Key (TMP)        ← fallback when a key has no prefab
///                                └─ Action (TMP)
///
/// KeyCap is where the key prefab gets instantiated; the original Key label is moved under it and
/// hidden automatically whenever that key does have a prefab. Nothing is lost, and keys without a
/// prefab yet are still readable.
///
/// Works on several prefabs selected in the Project window at once, and is safe to re-run
/// (prefabs that are already upgraded are skipped).
/// </summary>
public static class KeyHintRowUpgrader
{
    private const string MenuPath = "Rule of Horror/Phone/Add Key Icons To Hint Row Prefabs";

    [MenuItem(MenuPath)]
    private static void Upgrade()
    {
        var paths = Selection.objects
            .OfType<GameObject>()
            .Select(AssetDatabase.GetAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab"))
            .Distinct()
            .ToList();

        if (paths.Count == 0)
        {
            Debug.LogWarning("[KeyHintRow] No prefab selected — select the hint row prefabs in the " +
                             "Project window (Keyprompt_Text, YourHand, …) and run this again");
            return;
        }

        int done = 0, skipped = 0;
        foreach (var path in paths)
        {
            switch (UpgradeOne(path))
            {
                case Result.Upgraded: done++;   break;
                case Result.AlreadyDone: skipped++; break;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KeyHintRow] Upgraded {done} prefab(s), skipped {skipped} already done");
    }

    private enum Result { Upgraded, AlreadyDone, Failed }

    private static Result UpgradeOne(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // Already-upgraded prefabs still go through FixLayout, in case the layout values drifted
            // or a KeyCap was added by hand afterwards
            if (root.GetComponent<KeyHintRow>() != null)
            {
                FixLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[KeyHintRow] Re-applied layout to: {path}");
                return Result.AlreadyDone;
            }

            // Hierarchy order is reading order — the first label is the key, the second the description
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 0)
            {
                Debug.LogError($"[KeyHintRow] '{path}' has no TMP_Text at all — skipped");
                return Result.Failed;
            }

            TMP_Text keyText    = texts[0];
            TMP_Text actionText = texts.Length > 1 ? texts[1] : null;

            var keyCap = WrapInKeyCap(keyText);

            var view = keyCap.AddComponent<KeyCapView>();
            var so   = new SerializedObject(view);
            so.FindProperty("keyText").objectReferenceValue = keyText;
            // Leaving iconSlot empty makes the key prefab spawn under KeyCap itself, which is what we want
            so.ApplyModifiedPropertiesWithoutUndo();

            var row   = root.AddComponent<KeyHintRow>();
            var rowSo = new SerializedObject(row);
            var caps  = rowSo.FindProperty("keyCaps");
            caps.arraySize = 1;
            caps.GetArrayElementAtIndex(0).objectReferenceValue = view;
            rowSo.FindProperty("actionText").objectReferenceValue = actionText;
            rowSo.ApplyModifiedPropertiesWithoutUndo();

            FixLayout(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[KeyHintRow] Upgraded: {path}" +
                      (actionText == null ? "  (no description label found — assign one in the Inspector)" : ""));
            return Result.Upgraded;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Fixes the layout rules across the whole row — this is what keeps the key and the
    /// description from landing on top of each other.
    ///
    /// The rule that has to hold all the way down: whoever controls a child's size means that
    /// child must not carry a ContentSizeFitter of its own.
    ///   Row   : an HLG that controls its children (KeyCap, Action), plus its own CSF so the row
    ///           shrinks to its contents
    ///   KeyCap: an HLG so it can report the size it needs to the row, but no CSF, because the
    ///           row is already the one setting its size
    ///
    /// The old prefabs had Control Child Size off and Force Expand on, exactly backwards — so the
    /// row read every child's width as 0 and stacked them all at the same spot.
    /// </summary>
    private static void FixLayout(GameObject root)
    {
        var group = root.GetComponent<HorizontalLayoutGroup>();
        if (group != null)
        {
            group.childControlWidth      = true;
            group.childControlHeight     = true;
            group.childForceExpandWidth  = false;
            group.childForceExpandHeight = false;
            group.childAlignment         = TextAnchor.MiddleLeft;
            if (group.spacing < 1f) group.spacing = 12f;
        }

        // Without a CSF the row is stuck at its authored size (200x50), so a long description wraps
        // and spills over the key
        var fitter = root.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var cap in root.GetComponentsInChildren<KeyCapView>(true))
        {
            var capFitter = cap.GetComponent<ContentSizeFitter>();
            if (capFitter != null) Object.DestroyImmediate(capFitter, true);

            var capGroup = cap.GetComponent<HorizontalLayoutGroup>();
            if (capGroup == null) continue;

            capGroup.childControlWidth      = true;
            capGroup.childControlHeight     = true;
            capGroup.childForceExpandWidth  = false;
            capGroup.childForceExpandHeight = false;
        }
    }

    /// <summary>
    /// Reparents the key's TMP label under a new GameObject called KeyCap without moving it
    /// within the row.
    ///
    /// KeyCap gets a layout group so it grows and shrinks with whatever ends up inside it —
    /// a 70x70 key prefab or the fallback label — leaving no dead space in the row.
    /// </summary>
    private static GameObject WrapInKeyCap(TMP_Text keyText)
    {
        var parent  = keyText.transform.parent;
        int sibling = keyText.transform.GetSiblingIndex();

        var keyCap = new GameObject("KeyCap", typeof(RectTransform));
        keyCap.layer = keyText.gameObject.layer;
        keyCap.transform.SetParent(parent, false);
        keyCap.transform.SetSiblingIndex(sibling);

        var group = keyCap.AddComponent<HorizontalLayoutGroup>();
        group.childControlWidth       = true;
        group.childControlHeight      = true;
        group.childForceExpandWidth   = false;
        group.childForceExpandHeight  = false;
        group.padding                 = new RectOffset(0, 0, 0, 0);
        group.spacing                 = 0f;
        group.childAlignment          = TextAnchor.MiddleCenter;

        // No ContentSizeFitter here — the row is the one sizing KeyCap, and adding one makes the two fight
        keyText.transform.SetParent(keyCap.transform, false);
        return keyCap;
    }
}
