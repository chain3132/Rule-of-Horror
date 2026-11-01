using System.Linq;
using System.Text;
using Rule6;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the normal-girl Paeng (ePangSuay) for the Escape ending (bus 2).
///
/// 1. PangSuay.controller - parameters and transitions:
///      Idle  --walk=true-->  ePang_Walk  --walk=false-->  Idle
///      Idle / Walk  --nod-->  ePang_Nod  --(end)-->  Idle
///      Idle / Walk  --sit-->  ePang_Sitting  --(end)-->  ePang_IdleSit (loop)
///      ePang_IdleSit  --lookOut-->  ePang_LookOut  --(end)-->  ePang_LookOutIdle (loop)
///    Walk, IdleSit and LookOutIdle are set to loop.
///    Idle has no clip, which shows the bind pose; it is given the first frame of ePang_Nod
///    (speed 0) as a standing pose until a real standing idle exists.
///
/// 2. Ending Cutscene 2 Escape:
///      - the nod / sit triggers point at ePangSuay's Animator (they pointed at the ghost model)
///      - MoveTo steps after the model swap drive ePangSuay's "walk" bool
///      - adds a "lookOut" step after the bus pulls out (once)
///
/// Safe to run again: transitions are rebuilt, the lookOut step is not added twice.
/// </summary>
public static class PangSuaySetup
{
    private const string ControllerPath = "Assets/Animation/PangSuay.controller";
    private const string CutsceneName   = "Ending Cutscene 2 Escape";

    private const string WalkBool   = "walk";
    private const string NodTrigger = "nod";
    private const string SitTrigger = "sit";
    private const string LookOut    = "lookOut";

    private const string LookOutNote = "SHOT 8 · she looks out the window (lookOut)";

    [MenuItem("Rule of Horror/Rule 6/Setup PangSuay Animator (Escape)", false, 80)]
    private static void Setup()
    {
        var report = new StringBuilder();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            EditorUtility.DisplayDialog("PangSuay", $"Animator Controller not found at {ControllerPath}", "OK");
            return;
        }

        if (!SetupController(controller, report)) return;
        SetupCutscene(controller, report);

        Debug.Log("[Rule6] PangSuay setup done.\n" + report);
    }

    // ─────────────────────────── Controller ───────────────────────────

    private static bool SetupController(AnimatorController controller, StringBuilder report)
    {
        var machine = controller.layers[0].stateMachine;

        AnimatorState idle        = Find(machine, "Idle");
        AnimatorState walk        = Find(machine, "ePang_Walk");
        AnimatorState nod         = Find(machine, "ePang_Nod");
        AnimatorState sitting     = Find(machine, "ePang_Sitting");
        AnimatorState idleSit     = Find(machine, "ePang_IdleSit");
        AnimatorState lookOut     = Find(machine, "ePang_LookOut");
        AnimatorState lookOutIdle = Find(machine, "ePang_LookOutIdle");

        var missing = new[] { ("Idle", idle), ("ePang_Walk", walk), ("ePang_Nod", nod), ("ePang_Sitting", sitting),
                              ("ePang_IdleSit", idleSit), ("ePang_LookOut", lookOut), ("ePang_LookOutIdle", lookOutIdle) }
                      .Where(s => s.Item2 == null).Select(s => s.Item1).ToArray();
        if (missing.Length > 0)
        {
            EditorUtility.DisplayDialog("PangSuay", "Missing states in PangSuay.controller:\n" + string.Join("\n", missing), "OK");
            return false;
        }

        Undo.RecordObject(controller, "Setup PangSuay");

        AddParameter(controller, WalkBool,   AnimatorControllerParameterType.Bool);
        AddParameter(controller, NodTrigger, AnimatorControllerParameterType.Trigger);
        AddParameter(controller, SitTrigger, AnimatorControllerParameterType.Trigger);
        AddParameter(controller, LookOut,    AnimatorControllerParameterType.Trigger);

        // Rebuild from scratch so running twice does not stack duplicate transitions
        foreach (var child in machine.states)
            foreach (var t in child.state.transitions.ToArray())
                child.state.RemoveTransition(t);

        machine.defaultState = idle;

        // Idle has no clip = bind pose. Hold the first frame of the nod as a standing pose instead.
        if (idle.motion == null)
        {
            idle.motion = nod.motion;
            idle.speed  = 0f;
            report.AppendLine("  Idle had no clip - it now holds the first frame of ePang_Nod (speed 0). " +
                              "Swap in a real standing idle when there is one.");
        }

        // Walk
        Conditioned(idle, walk, 0.2f).AddCondition(AnimatorConditionMode.If,    0, WalkBool);
        Conditioned(walk, idle, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, WalkBool);

        // Nod and back
        Conditioned(idle, nod, 0.15f).AddCondition(AnimatorConditionMode.If, 0, NodTrigger);
        Conditioned(walk, nod, 0.25f).AddCondition(AnimatorConditionMode.If, 0, NodTrigger);
        AtEnd(nod, idle, 0.25f);

        // Sit down, stay seated
        Conditioned(idle, sitting, 0.2f).AddCondition(AnimatorConditionMode.If, 0, SitTrigger);
        Conditioned(walk, sitting, 0.25f).AddCondition(AnimatorConditionMode.If, 0, SitTrigger);
        AtEnd(sitting, idleSit, 0.2f);

        // Look out of the window, stay looking
        Conditioned(idleSit, lookOut, 0.25f).AddCondition(AnimatorConditionMode.If, 0, LookOut);
        AtEnd(lookOut, lookOutIdle, 0.2f);

        SetLoop(walk.motion as AnimationClip, report);
        SetLoop(idleSit.motion as AnimationClip, report);
        SetLoop(lookOutIdle.motion as AnimationClip, report);

        // Feet sinking into the ground: standing clips are placed by the feet, seated clips keep
        // the height they were animated at (placing a seated pose by the feet can lift the hips off the seat)
        GroundClip(walk.motion as AnimationClip,        fromFeet: true,  report);
        GroundClip(nod.motion as AnimationClip,         fromFeet: true,  report);
        GroundClip(sitting.motion as AnimationClip,     fromFeet: false, report);
        GroundClip(idleSit.motion as AnimationClip,     fromFeet: false, report);
        GroundClip(lookOut.motion as AnimationClip,     fromFeet: false, report);
        GroundClip(lookOutIdle.motion as AnimationClip, fromFeet: false, report);

        // Foot IK off: it pins the feet where the clip's source rig had them. ePangSuay's legs are a
        // different length, so reaching those spots bends her knees. Feet-based height is enough.
        foreach (var child in machine.states)
            child.state.iKOnFeet = false;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        report.AppendLine("  PangSuay.controller: parameters walk / nod / sit / lookOut, 11 transitions.");
        return true;
    }

    private static AnimatorState Find(AnimatorStateMachine machine, string name) =>
        machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name)) return;
        controller.AddParameter(name, type);
    }

    /// <summary>Transition on a parameter: leaves immediately, no exit time.</summary>
    private static AnimatorStateTransition Conditioned(AnimatorState from, AnimatorState to, float blend)
    {
        var t = from.AddTransition(to);
        t.hasExitTime         = false;
        t.hasFixedDuration    = true;
        t.duration            = blend;
        t.interruptionSource  = TransitionInterruptionSource.None;
        return t;
    }

    /// <summary>Transition when the clip finishes (one-shot clips: nod, sitting down, turning to the window).</summary>
    private static void AtEnd(AnimatorState from, AnimatorState to, float blend)
    {
        var t = from.AddTransition(to);
        t.hasExitTime      = true;
        t.exitTime         = 0.95f;
        t.hasFixedDuration = true;
        t.duration         = blend;
    }

    private static void SetLoop(AnimationClip clip, StringBuilder report)
    {
        if (clip == null) return;

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime) return;

        Undo.RecordObject(clip, "Loop clip");
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        report.AppendLine($"  {clip.name}: Loop Time on.");
    }

    /// <summary>
    /// Humanoid "Root Transform Position (Y)": Bake Into Pose on, Based Upon Feet or Original.
    ///
    /// Root motion is off on ePangSuay (the cutscene moves the parent). With Bake Into Pose off
    /// the clip's vertical root offset is treated as root motion and thrown away, so the body
    /// drops by that amount and the feet go into the ground. Baking it into the pose keeps it.
    /// </summary>
    private static void GroundClip(AnimationClip clip, bool fromFeet, StringBuilder report)
    {
        if (clip == null) return;

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        bool keepOriginal = !fromFeet;
        if (settings.loopBlendPositionY && settings.heightFromFeet == fromFeet &&
            settings.keepOriginalPositionY == keepOriginal) return;

        Undo.RecordObject(clip, "Ground clip");
        settings.loopBlendPositionY    = true;
        settings.heightFromFeet        = fromFeet;
        settings.keepOriginalPositionY = keepOriginal;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        report.AppendLine($"  {clip.name}: Root Y baked into pose, based upon {(fromFeet ? "Feet" : "Original")}.");
    }

    // ─────────────────────────── Cutscene ───────────────────────────

    private static void SetupCutscene(AnimatorController controller, StringBuilder report)
    {
        var cutscene = Object.FindObjectsByType<EndingCutscene>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                             .FirstOrDefault(c => c.name == CutsceneName);
        if (cutscene == null)
        {
            report.AppendLine($"  ! '{CutsceneName}' is not in the open scene - open Rule4 and run again for the cutscene part.");
            return;
        }

        Animator pangSuay = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                  .FirstOrDefault(a => a.runtimeAnimatorController == controller);
        if (pangSuay == null)
        {
            report.AppendLine("  ! No Animator in the scene uses PangSuay.controller - put it on ePangSuay and run again.");
            return;
        }

        // Walk clip carries root motion - the cutscene moves the parent "Paeng", the model must stay put inside it
        if (pangSuay.applyRootMotion)
        {
            Undo.RecordObject(pangSuay, "Root motion off");
            pangSuay.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(pangSuay);
            report.AppendLine($"  {pangSuay.name}: Apply Root Motion off.");
        }

        var so    = new SerializedObject(cutscene);
        var steps = so.FindProperty("steps");

        int swapIndex = -1;   // the "normal model on" step - walking before it is the ghost
        int driveIndex = -1;  // first BusDrive after the swap - she looks out after the bus pulls out
        bool hasLookOut = false;
        int triggers = 0, walks = 0;

        for (int i = 0; i < steps.arraySize; i++)
        {
            var step   = steps.GetArrayElementAtIndex(i);
            var action = (EndingCutscene.StepAction)step.FindPropertyRelative("action").enumValueIndex;

            if (action == EndingCutscene.StepAction.SetActive &&
                step.FindPropertyRelative("target").objectReferenceValue == pangSuay.gameObject &&
                step.FindPropertyRelative("active").boolValue)
                swapIndex = i;

            if (action == EndingCutscene.StepAction.AnimatorTrigger)
            {
                string trigger = step.FindPropertyRelative("trigger").stringValue;
                if (trigger == LookOut) hasLookOut = true;

                if (trigger == NodTrigger || trigger == SitTrigger || trigger == LookOut)
                {
                    step.FindPropertyRelative("animator").objectReferenceValue = pangSuay;
                    triggers++;
                }
            }

            if (action == EndingCutscene.StepAction.MoveTo && swapIndex >= 0 && i > swapIndex)
            {
                step.FindPropertyRelative("walkAnimator").objectReferenceValue = pangSuay;
                step.FindPropertyRelative("walkBool").stringValue = WalkBool;
                walks++;
            }

            if (action == EndingCutscene.StepAction.BusDrive && swapIndex >= 0 && driveIndex < 0)
                driveIndex = i;
        }

        report.AppendLine($"  {CutsceneName}: {triggers} trigger step(s) -> {pangSuay.name}, " +
                          $"{walks} walk step(s) use the '{WalkBool}' bool.");

        if (swapIndex < 0)
            report.AppendLine($"  ! No SetActive step turns on '{pangSuay.name}' - walk steps were left alone.");

        if (!hasLookOut && driveIndex >= 0)
        {
            // Inserting duplicates the drive step; the second of the two becomes the lookOut step
            int at = driveIndex + 1;
            steps.InsertArrayElementAtIndex(driveIndex);

            var step = steps.GetArrayElementAtIndex(at);
            step.FindPropertyRelative("note").stringValue                    = LookOutNote;
            step.FindPropertyRelative("action").enumValueIndex               = (int)EndingCutscene.StepAction.AnimatorTrigger;
            step.FindPropertyRelative("waitToFinish").boolValue              = false;
            step.FindPropertyRelative("animator").objectReferenceValue       = pangSuay;
            step.FindPropertyRelative("trigger").stringValue                 = LookOut;

            report.AppendLine($"  Added step {at}: '{LookOutNote}' (runs together with the bus pulling out).");
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(cutscene.gameObject.scene);
        report.AppendLine("  Save the scene.");
    }
}
