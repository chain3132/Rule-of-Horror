using Rule6;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the subtitle for Paeng's "thank you" to the bus 2 (Escape) ending cutscene.
///
/// Finds the Sound step that plays her line (event path contains "ThankYou"), and inserts a
/// Dialogue step right after it. That step does not wait to finish, so the existing Wait step that
/// follows (SHOT 6) runs while the subtitle is on screen and the cutscene timing does not change.
///
/// The text goes through L10n like every other subtitle. Its English row is in Translations.tsv,
/// so run Localization step 1 once afterwards. Safe to re-run: it skips cutscenes that already
/// have the subtitle.
/// </summary>
internal static class PaengThankYouSubtitle
{
    private const string Line = "ขอบคุณนะ";

    [MenuItem("Rule of Horror/Rule 6/Add Paeng Thank-You Subtitle")]
    private static void Add()
    {
        int added = 0, already = 0;

        foreach (var cutscene in Object.FindObjectsByType<EndingCutscene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var steps = cutscene.EditorSteps;

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null || step.action != EndingCutscene.StepAction.Sound) continue;
                if (string.IsNullOrEmpty(step.fmodEvent) || step.fmodEvent.IndexOf("ThankYou", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                var next = i + 1 < steps.Count ? steps[i + 1] : null;
                if (next != null && next.action == EndingCutscene.StepAction.Dialogue &&
                    next.lines != null && next.lines.Length > 0 && next.lines[0] != null && next.lines[0].text == Line)
                {
                    already++;
                    continue;
                }

                Undo.RecordObject(cutscene, "Add Paeng thank-you subtitle");
                steps.Insert(i + 1, new EndingCutscene.Step
                {
                    note         = "SHOT 6 · subtitle: Paeng says thank you",
                    action       = EndingCutscene.StepAction.Dialogue,
                    waitToFinish = false,   // the Wait after it keeps the shot length; the line stays up meanwhile
                    lines        = new[] { new DialogueLine { text = Line, holdDuration = -1f } },
                    playerVoice  = true,    // she is a normal girl again here, not the ghost: normal text colour
                });
                EditorUtility.SetDirty(cutscene);
                EditorSceneManager.MarkSceneDirty(cutscene.gameObject.scene);
                added++;
                i++;   // skip the step just inserted
            }
        }

        if (added == 0 && already == 0)
            Debug.LogWarning("[Rule6] No Sound step with a \"ThankYou\" FMOD event found in the open scene - open GameScene first");
        else
            Debug.Log($"[Rule6] Paeng thank-you subtitle: added {added}, already there {already}" +
                      (added > 0 ? " - save the scene" : ""));
    }
}
