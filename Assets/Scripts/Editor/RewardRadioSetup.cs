using Enum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the Radio reward sound (RewardRadio) to the pavilion radio in the open scene.
///
/// The radio is found through AudioManager's Radio Transform - the same object Rule 2's radio
/// sounds come from, so the reward music plays from the radio the player already knows.
/// Safe to run again: an existing RewardRadio is left alone.
/// </summary>
public static class RewardRadioSetup
{
    [MenuItem("Rule of Horror/Reward/Add Radio Reward Sound", false, 40)]
    private static void Add()
    {
        var audio = Object.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
        if (audio == null)
        {
            EditorUtility.DisplayDialog("Radio Reward", "No AudioManager in the open scene.", "OK");
            return;
        }

        var radio = new SerializedObject(audio).FindProperty("radioTransform").objectReferenceValue as Transform;
        if (radio == null)
        {
            EditorUtility.DisplayDialog("Radio Reward",
                "AudioManager has no Radio Transform set.\n\n" +
                "Select the radio object and add the RewardRadio component by hand (Kind = Radio).", "OK");
            return;
        }

        var existing = radio.GetComponentInParent<RewardRadio>(true);
        if (existing != null)
        {
            Debug.Log($"[Reward] '{existing.name}' already has RewardRadio - nothing to do.", existing);
            Selection.activeObject = existing;
            return;
        }

        var effect = Undo.AddComponent<RewardRadio>(radio.gameObject);
        var so = new SerializedObject(effect);
        so.FindProperty("kind").intValue = (int)RewardKind.Radio;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(radio.gameObject.scene);
        Selection.activeObject = effect;
        Debug.Log($"[Reward] Added RewardRadio to '{radio.name}'. " +
                  "Relax = event:/Radio/Release Radio, Tension = event:/Radio/Tension Radio. Save the scene.", effect);
    }
}
