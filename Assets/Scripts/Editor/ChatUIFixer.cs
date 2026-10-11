using System.Text;
using Manager;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two authoring fixes the chat UI needs, each as its own menu item.
///
/// Both are the kind of setup that is invisible until it is wrong: a Mask with no graphic looks
/// exactly like a Mask that works until content runs past the fold, and a text field with no
/// margin looks fine until somebody writes a long line. Doing them in code means the reasoning
/// is written down next to the change instead of living in whoever set the scene up.
/// </summary>
public static class ChatUIFixer
{
    private const string ReplyBoxPath = "Assets/Prefab/Chatting/ReplyBox.prefab";

    private const string KeyLabelName = "KeyLabel";

    // Sized against the chat bubbles, which sit at 48pt in ChatBox 1.prefab. A reply is read in
    // the same glance as the message above it, so anything much smaller looks like fine print —
    // and the phone screen is a 962-wide RenderTexture, so "24pt" here is half the size it sounds.
    private const float ReplyFontMin = 28f;
    private const float ReplyFontMax = 44f;
    private const float KeyFontSize  = 40f;

    // Two lines at the maximum size need roughly 110px, plus the padding
    private const float ReplyBoxHeight = 150f;

    // Room reserved on the left of a reply for the key badge, in the prefab's own units
    private const float KeyLabelWidth = 70f;
    private const float Padding       = 16f;

    // ─────────────────────────── Reply box ───────────────────────────

    /// <summary>
    /// Makes the reply text fit its frame and adds the "press 1 / press 2" badge.
    ///
    /// The text was stretched edge to edge with no margin at a fixed 24pt, so a long line ran
    /// straight through the border and out of the box (overflow mode spills rather than clips).
    /// Auto-sizing with a floor plus real margins keeps it inside whatever length the writer uses.
    ///
    /// Safe to re-run — an existing badge is reused rather than duplicated.
    /// </summary>
    [MenuItem("Rule of Horror/Chat/Upgrade Reply Box Prefab", false, 95)]
    private static void UpgradeReplyBox()
    {
        var root = PrefabUtility.LoadPrefabContents(ReplyBoxPath);
        if (root == null)
        {
            Debug.LogError($"[ChatUI] No prefab at {ReplyBoxPath}");
            return;
        }

        var report = new StringBuilder($"[ChatUI] {ReplyBoxPath}\n");

        try
        {
            // The reply's own label is the one that is NOT the badge, so find it before adding one
            TMP_Text replyText = null;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.gameObject.name == KeyLabelName) continue;

                replyText = text;
                break;
            }

            if (replyText == null)
            {
                Debug.LogError("[ChatUI] ReplyBox has no TMP text to use for the reply", root);
                return;
            }

            // ── give the box room for two lines at full size ──
            var box = root.GetComponent<RectTransform>();
            if (!Mathf.Approximately(box.sizeDelta.y, ReplyBoxHeight))
            {
                report.AppendLine($"  • Box height {box.sizeDelta.y:0} → {ReplyBoxHeight:0}");
                box.sizeDelta = new Vector2(box.sizeDelta.x, ReplyBoxHeight);
            }

            // ── fit the text to the frame ──
            replyText.enableAutoSizing  = true;
            replyText.fontSizeMin       = ReplyFontMin;
            replyText.fontSizeMax       = ReplyFontMax;
            replyText.textWrappingMode  = TextWrappingModes.Normal;
            replyText.overflowMode      = TextOverflowModes.Truncate;
            replyText.alignment         = TextAlignmentOptions.Left;

            // Left margin clears the badge; the rest is breathing room off the border
            replyText.margin = new Vector4(KeyLabelWidth + Padding, Padding * 0.5f, Padding, Padding * 0.5f);

            report.AppendLine($"  • Reply text: auto-size {ReplyFontMin:0}–{ReplyFontMax:0}pt, " +
                              "truncate on overflow, margins cleared of the badge");

            // ── the key badge ──
            var badge = root.transform.Find(KeyLabelName) as RectTransform;
            bool created = badge == null;

            if (created)
            {
                var go = new GameObject(KeyLabelName, typeof(RectTransform));
                go.layer = root.layer;
                go.transform.SetParent(root.transform, false);
                badge = (RectTransform)go.transform;
            }

            // Pinned to the left edge so it stays put however wide the button is laid out
            badge.anchorMin        = new Vector2(0f, 0f);
            badge.anchorMax        = new Vector2(0f, 1f);
            badge.pivot            = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(Padding, 0f);
            badge.sizeDelta        = new Vector2(KeyLabelWidth, 0f);

            var keyLabel = badge.GetComponent<TextMeshProUGUI>();
            if (keyLabel == null) keyLabel = badge.gameObject.AddComponent<TextMeshProUGUI>();

            // The badge is generated, so it is restyled on every run rather than preserved —
            // otherwise tuning the sizes here would never reach a prefab that was upgraded once
            if (created) keyLabel.text = "1";

            keyLabel.font              = replyText.font;     // the default TMP font has no Thai glyphs
            keyLabel.enableAutoSizing  = false;
            keyLabel.fontSize          = KeyFontSize;
            keyLabel.fontStyle         = FontStyles.Bold;
            keyLabel.color             = new Color(1f, 0.85f, 0.4f);
            keyLabel.alignment         = TextAlignmentOptions.Center;
            keyLabel.raycastTarget     = false;

            report.AppendLine(created
                ? $"  • Added the {KeyLabelName} badge on the left, {KeyFontSize:0}pt"
                : $"  • {KeyLabelName} repositioned and restyled at {KeyFontSize:0}pt");

            // ── the component that hands both to the chat ──
            var view = root.GetComponent<ReplyButtonView>();
            if (view == null) view = root.AddComponent<ReplyButtonView>();

            var so = new SerializedObject(view);
            so.FindProperty("replyText").objectReferenceValue = replyText;
            so.FindProperty("keyLabel").objectReferenceValue  = keyLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            report.AppendLine("  • ReplyButtonView wired to both labels");

            PrefabUtility.SaveAsPrefabAsset(root, ReplyBoxPath);
            AssetDatabase.SaveAssets();

            report.Append("  Press Play and open a chat with choices — the buttons should read 1 and 2");
            Debug.Log(report.ToString(), AssetDatabase.LoadAssetAtPath<GameObject>(ReplyBoxPath));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ─────────────────────────── Scroll masking ───────────────────────────

    /// <summary>
    /// Stops chat bubbles drawing outside the scroll view.
    ///
    /// The Viewport carries a Mask, but a Mask is a stencil cut from a Graphic on the same object,
    /// and this one has no Image at all — so it masks nothing and every bubble renders over the
    /// header and past the bottom of the phone.
    ///
    /// The fix is RectMask2D rather than adding an invisible Image: a chat list is a plain
    /// rectangle, and RectMask2D clips one without a graphic, without a stencil buffer, and
    /// without the extra draw call.
    /// </summary>
    [MenuItem("Rule of Horror/Chat/Fix Chat Scroll Clipping", false, 96)]
    private static void FixChatScroll()
    {
        var chat = Object.FindAnyObjectByType<ChatUIController>(FindObjectsInactive.Include);
        if (chat == null)
        {
            Debug.LogError("[ChatUI] No ChatUIController in the open scene — open the scene with the phone first");
            return;
        }

        var so     = new SerializedObject(chat);
        var scroll = so.FindProperty("scrollRect").objectReferenceValue as ScrollRect;

        if (scroll == null)
        {
            Debug.LogError("[ChatUI] ChatUIController has no Scroll Rect assigned", chat);
            return;
        }

        var report = new StringBuilder($"[ChatUI] Chat scroll on '{scroll.name}'\n");

        RectTransform viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;

        // ── masking ──
        var oldMask = viewport.GetComponent<Mask>();
        if (oldMask != null)
        {
            bool hadGraphic = viewport.GetComponent<Graphic>() != null;

            Undo.DestroyObjectImmediate(oldMask);
            report.AppendLine(hadGraphic
                ? "  • Removed the Mask (replaced by RectMask2D, which needs no graphic)"
                : "  • Removed the Mask — it had no Image on the same object, so it was clipping nothing. " +
                  "That is why the bubbles ran past the viewport");
        }

        if (viewport.GetComponent<RectMask2D>() == null)
        {
            Undo.AddComponent<RectMask2D>(viewport.gameObject);
            report.AppendLine($"  • Added RectMask2D to '{viewport.name}'");
        }
        else
        {
            report.AppendLine($"  • '{viewport.name}' already had RectMask2D");
        }

        // ── the content rect ──
        // A chat grows downwards from the top, so the content has to be pinned by its top edge.
        // Any other pivot makes the ContentSizeFitter grow it in both directions and the first
        // bubbles drift up out of view as the list gets longer.
        var content = scroll.content;
        if (content != null)
        {
            if (content.pivot != new Vector2(0.5f, 1f) ||
                content.anchorMin != new Vector2(0f, 1f) ||
                content.anchorMax != new Vector2(1f, 1f))
            {
                Undo.RecordObject(content, "Fix Chat Content Anchors");

                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot     = new Vector2(0.5f, 1f);

                report.AppendLine("  • Content anchored to the top edge (top-stretch, pivot 0.5/1)");
            }
            else
            {
                report.AppendLine("  • Content anchors were already correct");
            }

            if (!Mathf.Approximately(content.anchoredPosition.y, 0f) ||
                !Mathf.Approximately(content.anchoredPosition.x, 0f))
            {
                Undo.RecordObject(content, "Fix Chat Content Position");
                content.anchoredPosition = Vector2.zero;
                report.AppendLine("  • Content position reset to 0,0");
            }

            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                report.AppendLine("  ⚠ Content has no ContentSizeFitter — it cannot grow with the messages");
            else if (fitter.verticalFit != ContentSizeFitter.FitMode.PreferredSize)
            {
                Undo.RecordObject(fitter, "Fix Chat Content Fitter");
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                report.AppendLine("  • ContentSizeFitter vertical fit set to Preferred Size");
            }
        }

        EditorSceneManager.MarkSceneDirty(chat.gameObject.scene);
        Selection.activeGameObject = viewport.gameObject;

        report.Append("  Save the scene to keep this");
        Debug.Log(report.ToString(), viewport);
    }
}
