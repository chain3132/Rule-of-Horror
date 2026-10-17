using System.Text;
using Manager;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds the rule book's fact list so long facts fit and unknown facts read as blanks.
///
/// Three faults, one shared cause — every row was a bare 200x50 TMP label in a layout group that
/// was told not to control child height:
///
///   • no marker, so a fact was a wall of text with nothing separating one line from the next
///   • an unknown fact was the character "—", which looks like a fact that happens to be short,
///     not like information the player was denied
///   • a fact too long for one line wrapped to two, but the row stayed 50 tall and TMP's overflow
///     mode draws outside the rect — so the second line was painted over the row below it
///
/// After this runs the layout group measures each row from its text, so a two-line fact makes its
/// own row taller and pushes the rest down instead of bleeding into them.
///
/// Safe to re-run: existing children are reused and restyled rather than duplicated, so tuning
/// the constants here and running again reaches prefabs that were already upgraded once.
/// </summary>
public static class RuleBookUIFixer
{
    private const string FactPath    = "Assets/Prefab/RuleBook/FactPrefab.prefab";
    private const string MissingPath = "Assets/Prefab/RuleBook/FactMissingPrefab.prefab";

    private const string DashName = "Dash";
    private const string LineName = "Line";

    // 48pt was the old size and it put the longest fact (rule 4's "ถ้าคนที่แขวนคอหายไป…", 45 Thai
    // characters) onto three lines. 38pt fits most facts on one line and the worst case on two,
    // while staying readable through the phone's 962-wide RenderTexture.
    private const float FactFontSize = 38f;

    // One line at FactFontSize, used to place both markers on the first line's middle rather than
    // at the top of a row that may be two lines tall
    private const float LineHeight = FactFontSize * 1.3f;

    private const float DashWidth  = 34f;
    private const float DashHeight = 7f;
    private const float LeftInset  = 6f;

    // Text starts clear of the dash on EVERY line, which makes a wrapped fact hang under itself
    // instead of sliding back under the marker
    private const float TextIndent = LeftInset + DashWidth + 18f;

    private static readonly Color DashColor = new Color(0.78f, 0.15f, 0.13f);   // แดงหมึกแห้ง
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.32f);

    // ─────────────────────────── Menu ───────────────────────────

    [MenuItem("Rule of Horror/Phone/Upgrade Rule Book Fact Rows", false, 81)]
    private static void Upgrade()
    {
        var report = new StringBuilder("[RuleBook] Fact rows\n");

        BuildFactRow(report);
        BuildMissingRow(report);
        FixListLayout(report);

        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    // ─────────────────────────── Fact row ───────────────────────────

    /// <summary>
    /// A fact row: red dash on the left, text indented clear of it.
    ///
    /// The dash is a child Image rather than a rich-text glyph because the project's Thai SDF font
    /// has no box-drawing characters — a "▬" would come out as a missing-glyph box.
    /// </summary>
    private static void BuildFactRow(StringBuilder report)
    {
        var root = PrefabUtility.LoadPrefabContents(FactPath);
        if (root == null)
        {
            Debug.LogError($"[RuleBook] No prefab at {FactPath}");
            return;
        }

        try
        {
            var label = root.GetComponent<TMP_Text>();
            if (label == null)
            {
                Debug.LogError("[RuleBook] FactPrefab has no TMP text on its root", root);
                return;
            }

            // ── text ──
            label.fontSize          = FactFontSize;
            label.enableAutoSizing  = false;   // auto-size + a layout group that reads
                                               // preferredHeight is a feedback loop
            label.textWrappingMode  = TextWrappingModes.Normal;
            label.overflowMode      = TextOverflowModes.Overflow;   // รู้ครบแล้วห้ามมีคำหาย
            label.alignment         = TextAlignmentOptions.TopLeft;
            label.margin            = new Vector4(TextIndent, 0f, 0f, 0f);

            report.AppendLine($"  • Fact text {FactFontSize:0}pt, indented {TextIndent:0}px clear of the dash");

            // ── the row's own height has to come from the text ──
            // A LayoutElement would win over TMP's own ILayoutElement (higher layoutPriority) and
            // pin every row to one fixed height again, which is the bug. So there must not be one.
            var pinned = root.GetComponent<LayoutElement>();
            if (pinned != null)
            {
                Object.DestroyImmediate(pinned);
                report.AppendLine("  • Removed the LayoutElement — it was overriding the text's own height");
            }

            // ── dash ──
            var dash = root.transform.Find(DashName) as RectTransform;
            bool created = dash == null;

            if (created)
            {
                var go = new GameObject(DashName, typeof(RectTransform));
                go.layer = root.layer;
                go.transform.SetParent(root.transform, false);
                dash = (RectTransform)go.transform;
            }

            // Pinned to the row's top-left and dropped half a line, so it lands on the middle of
            // the first line whether the row is one line tall or three
            dash.anchorMin        = new Vector2(0f, 1f);
            dash.anchorMax        = new Vector2(0f, 1f);
            dash.pivot            = new Vector2(0f, 0.5f);
            dash.sizeDelta        = new Vector2(DashWidth, DashHeight);
            dash.anchoredPosition = new Vector2(LeftInset, -LineHeight * 0.5f);

            var image = dash.GetComponent<Image>();
            if (image == null) image = dash.gameObject.AddComponent<Image>();

            image.sprite        = null;          // no sprite = a plain white quad, tinted below
            image.color         = DashColor;
            image.raycastTarget = false;

            report.AppendLine(created
                ? $"  • Added the red dash ({DashWidth:0}x{DashHeight:0}) on the first line"
                : $"  • Dash repositioned and recoloured ({DashWidth:0}x{DashHeight:0})");

            PrefabUtility.SaveAsPrefabAsset(root, FactPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ─────────────────────────── Missing row ───────────────────────────

    /// <summary>
    /// A denied fact: one long white line and nothing else.
    ///
    /// It is a row of its own rather than placeholder text in a fact row, because the player has to
    /// be able to count them — answer rule 4 rudely and four of the five details never arrive, and
    /// four blanks in a row say that far louder than a shorter list would.
    /// </summary>
    private static void BuildMissingRow(StringBuilder report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MissingPath);
        GameObject root;
        bool created = existing == null;

        if (created)
        {
            root = new GameObject("FactMissingPrefab", typeof(RectTransform));
            root.layer = 5;   // UI
        }
        else
        {
            root = PrefabUtility.LoadPrefabContents(MissingPath);
        }

        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, LineHeight);

            // A blank is always exactly one line tall. There is no text to measure, so unlike a
            // fact row this one does need a LayoutElement to claim its height from the group.
            var element = root.GetComponent<LayoutElement>();
            if (element == null) element = root.AddComponent<LayoutElement>();

            element.minHeight       = LineHeight;
            element.preferredHeight = LineHeight;
            element.flexibleHeight  = 0f;

            var line = root.transform.Find(LineName) as RectTransform;
            if (line == null)
            {
                var go = new GameObject(LineName, typeof(RectTransform));
                go.layer = root.layer;
                go.transform.SetParent(root.transform, false);
                line = (RectTransform)go.transform;
            }

            // Stretched across the full row width — "ขีดขาวๆยาวๆ" — and centred on the line
            line.anchorMin        = new Vector2(0f, 0.5f);
            line.anchorMax        = new Vector2(1f, 0.5f);
            line.pivot            = new Vector2(0.5f, 0.5f);
            line.sizeDelta        = new Vector2(-LeftInset * 2f, DashHeight * 0.7f);
            line.anchoredPosition = Vector2.zero;

            var image = line.GetComponent<Image>();
            if (image == null) image = line.gameObject.AddComponent<Image>();

            image.sprite        = null;
            image.color         = LineColor;
            image.raycastTarget = false;

            PrefabUtility.SaveAsPrefabAsset(root, MissingPath);

            report.AppendLine(created
                ? $"  • Created {MissingPath} — one long white line per denied fact"
                : $"  • {MissingPath} restyled");
        }
        finally
        {
            if (created) Object.DestroyImmediate(root);
            else         PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ─────────────────────────── List layout ───────────────────────────

    /// <summary>
    /// Makes the list measure each row instead of assuming they are all the same height, and hands
    /// the new blank prefab to the binder.
    /// </summary>
    private static void FixListLayout(StringBuilder report)
    {
        var binder = Object.FindAnyObjectByType<RuleBookPanelBinder>(FindObjectsInactive.Include);
        if (binder == null)
        {
            report.AppendLine("  ⚠ No RuleBookPanelBinder in the open scene — open the scene with " +
                              "the phone and run this again to finish the wiring");
            return;
        }

        var so   = new SerializedObject(binder);
        var slot = so.FindProperty("factMissingRowPrefab");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MissingPath);

        if (slot != null && prefab != null && slot.objectReferenceValue != prefab)
        {
            slot.objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            report.AppendLine("  • Binder's Fact Missing Row Prefab wired");
        }

        var root = so.FindProperty("factRoot").objectReferenceValue as Transform;
        if (root == null)
        {
            report.AppendLine("  ⚠ Binder has no Fact Root assigned — the layout fix was skipped");
            return;
        }

        var group = root.GetComponent<VerticalLayoutGroup>();
        if (group == null)
        {
            report.AppendLine($"  ⚠ '{root.name}' has no VerticalLayoutGroup — add one, then run this again");
            return;
        }

        Undo.RecordObject(group, "Fix Rule Book Fact Layout");

        // childControlHeight was off, which is the whole fault: the group kept every row at the
        // prefab's 50px and let TMP paint a wrapped second line over the row below it.
        // On, the group asks each row how tall its text is and lays them out accordingly.
        group.childControlHeight     = true;
        group.childControlWidth      = true;

        // Expand would share the leftover space out so three facts fill the same box as five, which
        // makes the gap between rows change per rule. Off, rows keep their text height and the
        // unused space stays at the bottom where it reads as "that's all there is".
        group.childForceExpandHeight = false;
        group.childForceExpandWidth  = true;

        if (group.spacing < 10f) group.spacing = 14f;

        report.AppendLine($"  • '{root.name}' now sizes rows from their text (spacing {group.spacing:0})");

        // Enough room for the worst case: five facts that each wrap to two lines
        var rect   = root as RectTransform;
        float need = 5f * (LineHeight * 2f) + 4f * group.spacing
                   + group.padding.top + group.padding.bottom;

        if (rect != null && rect.rect.height < need)
        {
            report.AppendLine($"  ⚠ '{root.name}' is {rect.rect.height:0}px tall but five two-line " +
                              $"facts need about {need:0}px — drag it taller (keep the bottom edge " +
                              "where it is; there is empty space above it, under the rule summary). " +
                              "The rows redistribute on their own, nothing else to change");
        }

        EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
        Selection.activeGameObject = root.gameObject;
    }
}
