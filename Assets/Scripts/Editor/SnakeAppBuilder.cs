using System.IO;
using Manager;
using ScriptableObject;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the snake minigame app panel (key 4) inside the phone and wires it up in one click.
///
/// Why this exists: the phone screen is rendered into a RenderTexture, so an app panel's position,
/// size and layer have to match the other panels exactly. Get one wrong and it only looks broken
/// inside the phone screen while the Scene view shows it as fine, which is very hard to track down.
/// So this copies the rule app panel's anchors, offsets and layer rather than setting its own.
///
/// It also copies the font from a TMP_Text that already exists in the phone — TMP's default font
/// has no Thai glyphs, so letting it pick for itself turns the whole panel into blank space.
///
/// Safe to re-run: anything that already exists is left as it is (size and color tweaks survive)
/// and only the missing wiring is filled in.
/// </summary>
public static class SnakeAppBuilder
{
    private const string PanelName = "SnakePanel";
    private const string AssetPath = "Assets/Scripts/Scriptable/ScriptableObject/PhoneApp/Snake.asset";

    /// <summary>Key 4 = the fourth slot in the apps list (zero-based index).</summary>
    private const int AppIndex = 3;

    private const string UndoName = "Build Snake App";

    [MenuItem("Rule of Horror/Phone/Build Snake Minigame App (key 4)", false, 62)]
    private static void Build()
    {
        var ui = Object.FindAnyObjectByType<PhoneUIController>(FindObjectsInactive.Include);
        if (ui == null)
        {
            EditorUtility.DisplayDialog("No Phone Found",
                "No PhoneUIController in the open scene.\n" +
                "Open the scene that has the phone, then run this again.", "OK");
            return;
        }

        var uiSo     = new SerializedObject(ui);
        var template = ResolveTemplate(uiSo);
        if (template == null)
        {
            EditorUtility.DisplayDialog("No Template Panel",
                "PhoneUIController has neither ruleBookPanel nor homePanel assigned.\n" +
                "At least one panel is needed to copy the layout from.", "OK");
            return;
        }

        var panel = GetOrCreatePanel(template, out bool created);
        var game  = BuildContents(panel, template);

        var panelProp = uiSo.FindProperty("snakePanel");
        if (panelProp != null)
        {
            panelProp.objectReferenceValue = panel.gameObject;
            uiSo.ApplyModifiedProperties();
        }

        bool wired = WireAppList();

        panel.gameObject.SetActive(false);   // every app panel starts off; PhoneUIController opens them
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeGameObject = game.gameObject;

        Debug.Log($"[Snake] {(created ? "Created" : "Updated")} {PanelName} — " +
                  (wired
                      ? "Snake.asset is in PhoneAppController's fourth slot"
                      : "you still need to put Snake.asset in the fourth slot of PhoneAppController's apps list") +
                  "\nPress Play → Tab to raise the phone → press 4", game);
    }

    // ─────────────────────────── Hierarchy ───────────────────────────

    /// <summary>The panel to copy the layout from — the rule app first, the home screen otherwise.</summary>
    private static RectTransform ResolveTemplate(SerializedObject uiSo)
    {
        foreach (string field in new[] { "ruleBookPanel", "homePanel" })
        {
            var go = uiSo.FindProperty(field)?.objectReferenceValue as GameObject;
            if (go != null) return go.GetComponent<RectTransform>();
        }
        return null;
    }

    private static RectTransform GetOrCreatePanel(RectTransform template, out bool created)
    {
        var parent   = template.parent;
        var existing = parent != null ? parent.Find(PanelName) as RectTransform : null;

        created = existing == null;
        if (!created)
        {
            Undo.RecordObject(existing.gameObject, UndoName);
            return existing;
        }

        var rect = NewRect(PanelName, parent, template.gameObject.layer);
        CopyRect(template, rect);

        var bg = Undo.AddComponent<Image>(rect.gameObject);
        bg.color         = new Color32(8, 12, 8, 255);
        bg.raycastTarget = false;

        return rect;
    }

    /// <summary>Copies every placement value, so the panel lands exactly on top of the others,
    /// without being off by even a pixel.</summary>
    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin        = from.anchorMin;
        to.anchorMax        = from.anchorMax;
        to.pivot            = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta        = from.sizeDelta;
        to.localScale       = from.localScale;
        to.localRotation    = from.localRotation;
    }

    // ─────────────────────────── Contents ───────────────────────────

    private static SnakeGameController BuildContents(RectTransform panel, RectTransform template)
    {
        var game = GetOrAdd<SnakeGameController>(panel, out _);
        var font = FindThaiFont(template);

        // The outer frame defines what is left over for the playfield (leaving room for the score
        // bar at the top). The RawImage sits inside it and carries the AspectRatioFitter — putting
        // the fitter on this object instead would let FitInParent eat the margin we just reserved
        // and the playfield would cover the score bar.
        var area = GetOrCreateRect(panel, "Playfield", out _, r => Stretch(r, 40f, 40f, 220f, 160f));

        var screen = GetOrCreateRect(area, "Screen", out _, r => Stretch(r, 0f, 0f, 0f, 0f));
        var raw    = GetOrAdd<RawImage>(screen, out bool rawAdded);
        if (rawAdded) raw.raycastTarget = false;

        // The cells have to stay square, or the snake stretches into an ellipse following the
        // screen's aspect
        var fitter = GetOrAdd<AspectRatioFitter>(screen, out _);
        fitter.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = ScreenAspect(game);

        var scoreRect = GetOrCreateRect(panel, "Score", out _, r =>
        {
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.sizeDelta        = new Vector2(-80f, 70f);
            r.anchoredPosition = new Vector2(0f, -130f);
        });
        var score = GetOrAdd<TextMeshProUGUI>(scoreRect, out bool scoreAdded);
        if (scoreAdded)
        {
            score.fontSize         = 40f;
            score.alignment        = TextAlignmentOptions.Center;
            score.color            = new Color32(150, 210, 120, 255);
            score.raycastTarget    = false;
            score.textWrappingMode = TextWrappingModes.NoWrap;
            if (font != null) score.font = font;
        }

        var bannerRect = GetOrCreateRect(panel, "Banner", out _, r => Stretch(r, 60f, 60f, 0f, 0f));
        var banner = GetOrAdd<TextMeshProUGUI>(bannerRect, out bool bannerAdded);
        if (bannerAdded)
        {
            banner.fontSize      = 54f;
            banner.alignment     = TextAlignmentOptions.Center;
            banner.color         = new Color32(222, 245, 205, 255);
            banner.raycastTarget = false;
            if (font != null) banner.font = font;
        }

        bannerRect.SetAsLastSibling();   // must sit above the playfield, or the game-over text is covered

        var gameSo = new SerializedObject(game);
        gameSo.FindProperty("screen").objectReferenceValue     = raw;
        gameSo.FindProperty("scoreText").objectReferenceValue  = score;
        gameSo.FindProperty("bannerText").objectReferenceValue = banner;
        gameSo.ApplyModifiedProperties();

        return game;
    }

    /// <summary>The aspect of the texture the game will generate — read from the Inspector rather
    /// than hard-coded.</summary>
    private static float ScreenAspect(SnakeGameController game)
    {
        var so    = new SerializedObject(game);
        var grid  = so.FindProperty("grid").vector2IntValue;
        int cell  = so.FindProperty("cellPixels").intValue;
        int frame = so.FindProperty("framePixels").intValue;

        float w = grid.x * cell + frame * 2f;
        float h = grid.y * cell + frame * 2f;
        return h > 0f ? w / h : 1f;
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static RectTransform NewRect(string name, Transform parent, int layer)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoName);

        go.layer = layer;   // must be on the layer PhoneCamera renders, or the whole panel is invisible

        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    /// <summary>Finds the child by name, creating it when missing — setup only runs for a freshly
    /// created object, so hand edits are never overwritten.</summary>
    private static RectTransform GetOrCreateRect(RectTransform parent, string name, out bool created,
                                                 System.Action<RectTransform> setup)
    {
        var existing = parent.Find(name) as RectTransform;
        created = existing == null;
        if (!created) return existing;

        var rect = NewRect(name, parent, parent.gameObject.layer);
        setup?.Invoke(rect);
        return rect;
    }

    private static T GetOrAdd<T>(Component target, out bool added) where T : Component
    {
        var existing = target.GetComponent<T>();
        added = existing == null;
        return added ? Undo.AddComponent<T>(target.gameObject) : existing;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot     = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// Copies the font from text that already exists in the phone.
    ///
    /// TMP's default font has no Thai vowels or consonants, so without this the whole panel would
    /// render as blank space. The search starts at the Canvas root so it finds something even when
    /// the template panel has no text of its own.
    /// </summary>
    private static TMP_FontAsset FindThaiFont(RectTransform template)
    {
        Transform root = template;
        while (root.parent != null) root = root.parent;

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.font != null) return text.font;

        Debug.LogWarning("[Snake] No TMP font found in the phone — the text may not render Thai glyphs. " +
                         "Assign a font to Score/Banner by hand in the Inspector");
        return null;
    }

    // ─────────────────────────── App list ───────────────────────────

    /// <summary>
    /// Puts Snake.asset into the fourth slot of PhoneAppController.apps.
    /// That list is what maps a pressed number key to a PhoneState — with no fourth entry,
    /// pressing 4 simply does nothing.
    /// </summary>
    private static bool WireAppList()
    {
        var controller = Object.FindAnyObjectByType<PhoneAppController>(FindObjectsInactive.Include);
        if (controller == null) return false;

        var asset = LoadOrCreateAsset();
        if (asset == null) return false;

        var so   = new SerializedObject(controller);
        var apps = so.FindProperty("apps");
        if (apps == null) return false;

        if (apps.arraySize <= AppIndex) apps.arraySize = AppIndex + 1;
        apps.GetArrayElementAtIndex(AppIndex).objectReferenceValue = asset;
        so.ApplyModifiedProperties();

        return true;
    }

    private static PhoneAppData LoadOrCreateAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<PhoneAppData>(AssetPath);
        if (asset != null) return asset;

        string dir = Path.GetDirectoryName(AssetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        asset = UnityEngine.ScriptableObject.CreateInstance<PhoneAppData>();
        asset.openState = Enum.PhoneState.Snake;

        AssetDatabase.CreateAsset(asset, AssetPath);
        AssetDatabase.SaveAssets();
        return asset;
    }
}
