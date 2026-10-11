using System.Collections.Generic;
using Enum;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reward test window — pin down which options the chat offers instead of waiting on the roll.
///
/// A pinned reward bypasses every condition (round gate, already claimed, all candles already
/// lit), because the whole point is to test something that normally would not be offered yet,
/// such as looking at the radio during the first round.
///
/// The pinned list lives in the Forced Offer field on the scene's RewardSystem rather than in
/// EditorPrefs — it stays visible in the Inspector and is saved with the scene, so it is hard
/// to leave one on by accident and then wonder why the draw never changes.
/// </summary>
public class RewardTestWindow : EditorWindow
{
    [MenuItem("Rule of Horror/Reward/Reward Test Window", false, 80)]
    private static void Open()
    {
        var window = GetWindow<RewardTestWindow>("Reward Test");
        window.minSize = new Vector2(300f, 360f);
    }

    private Vector2 _scroll;

    // These values change during Play with no event to listen to, so repaint continuously
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        var system = Object.FindAnyObjectByType<RewardSystem>();
        if (system == null)
        {
            EditorGUILayout.HelpBox("No RewardSystem in the open scene.", MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawStatus(system);
        EditorGUILayout.Space();
        DrawLockSection(system);
        EditorGUILayout.Space();
        DrawGrantSection(system);

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────── status ───────────────────────────

    private static void DrawStatus(RewardSystem system)
    {
        EditorGUILayout.LabelField("Current State", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Round", system.Round.ToString());
            EditorGUILayout.LabelField("Options per offer", system.OptionsPerOffer.ToString());

            var claimed = new List<string>();
            foreach (var kind in system.Claimed) claimed.Add(LabelOf(system, kind));

            EditorGUILayout.LabelField("Claimed",
                claimed.Count > 0 ? string.Join(", ", claimed) : "nothing yet");
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Not playing — you can pin rewards now and they take effect on Play.",
                                    MessageType.None);
    }

    // ─────────────────────────── pinning the offer ───────────────────────────

    private static void DrawLockSection(RewardSystem system)
    {
        EditorGUILayout.LabelField("Pin The Offered Options", EditorStyles.boldLabel);

        var forced  = new List<RewardKind>(system.ForcedOffer);
        bool changed = false;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            foreach (var entry in system.Catalog)
            {
                if (entry == null || entry.kind == RewardKind.None) continue;

                bool on  = forced.Contains(entry.kind);
                bool now = EditorGUILayout.ToggleLeft(RowLabel(system, entry), on);

                if (now == on) continue;

                if (now) forced.Add(entry.kind);
                else     forced.Remove(entry.kind);

                changed = true;
            }
        }

        if (forced.Count > system.OptionsPerOffer)
            EditorGUILayout.HelpBox($"{forced.Count} pinned but only {system.OptionsPerOffer} can be " +
                                    "offered — the ones at the bottom will not appear.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(forced.Count == 0))
        {
            if (GUILayout.Button("Clear Pins (back to random)"))
            {
                forced.Clear();
                changed = true;
            }
        }

        if (!changed) return;

        Undo.RecordObject(system, "Pin Reward Options");
        system.SetForcedOffer(forced.ToArray());
        EditorUtility.SetDirty(system);
    }

    private static string RowLabel(RewardSystem system, RewardSystem.RewardEntry entry)
    {
        // Most specific reason first — otherwise every row would just read "not eligible"
        if (system.HasReward(entry.kind))             return $"{entry.Label}  (claimed)";
        if (system.Round < entry.availableFromRound)  return $"{entry.Label}  (waits for round {entry.availableFromRound})";
        if (!entry.available)                         return $"{entry.Label}  (disabled)";
        if (!system.CanBeDrawn(entry.kind))           return $"{entry.Label}  (not eligible)";

        return entry.Label;
    }

    // ─────────────────────────── grant directly / reset ───────────────────────────

    private static void DrawGrantSection(RewardSystem system)
    {
        EditorGUILayout.LabelField("Grant Without The Chat", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var entry in system.Catalog)
                {
                    if (entry == null || entry.kind == RewardKind.None) continue;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(entry.Label);

                        // Granting twice is allowed — handy for re-checking the Tension look after a reset
                        if (GUILayout.Button("Grant", GUILayout.Width(70f)))
                            system.Grant(entry.kind);
                    }
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Reset Everything (as if the game just started)"))
                system.ClearProgress();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("These two sections only work in Play mode — reward state lives in " +
                                    "memory and is never saved to the scene.", MessageType.None);
    }

    private static string LabelOf(RewardSystem system, RewardKind kind)
    {
        foreach (var entry in system.Catalog)
            if (entry != null && entry.kind == kind) return entry.Label;

        return kind.ToString();
    }
}
