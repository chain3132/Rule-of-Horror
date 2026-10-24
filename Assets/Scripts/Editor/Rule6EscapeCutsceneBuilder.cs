using System.Collections.Generic;
using System.Text;
using Enum;
using Rule6;
using ScriptableObject;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Step   = Rule6.EndingCutscene.Step;
using Action = Rule6.EndingCutscene.StepAction;

/// <summary>
/// Builds the in-game ending cutscene for bus 2 (Escape) from the storyboard, in the open scene.
///
///   SHOT 1  we choose bus 2 and sit by the window      (Rule6 does this before the cutscene)
///   SHOT 2  look out at Paeng, still a ghost, in front of the pavilion
///   SHOT 3  she walks slowly to the bus door
///   SHOT 4  camera at the door: she climbs in, half hidden by the bus body
///   SHOT 5  same angle: she steps out from behind the wall as a normal girl (model swap while hidden)
///   SHOT 6  back in our eyes: she stands by the seat across from us and nods - thank you
///   SHOT 7  she sits in her seat and turns to the window - door closes, engine starts
///   SHOT 8  the bus pulls out, the pavilion falls away past the window
///   SHOT 9  chat with Mom: "Mom, I'm home - is dinner ready?" - cut to black
///
/// What it creates:
///   - "Ending Cutscene 2 Escape" under the Rule 6 root, wired into Rule6.escapeCutscene
///   - under Bus 2: "Cutscene Markers" with the points Paeng walks to (drawn as purple figures)
///     and the "Shot 4 Door" camera - children of the bus, so they come along to the stop
///   - under Paeng: "Normal Body" - an empty slot for the human model that does not exist yet
///   - "Ending Chat Screen Mom" (a full-screen copy of the phone's ChatPanel) and the
///     Rule6_EscapeEndingChat conversation asset
///
/// Marker positions are a guess from the bus seat - drag them into place. Re-runnable: everything
/// is looked up by name, and steps are only filled when the list is empty.
/// </summary>
public static class Rule6EscapeCutsceneBuilder
{
    private const string CutsceneName  = "Ending Cutscene 2 Escape";
    private const string ChatAssetPath = "Assets/Scripts/Scriptable/ScriptableObject/Rule6_EscapeEndingChat.asset";
    private const string NormalBody    = "Normal Body";

    [MenuItem("Rule of Horror/Rule 6/Build Bus 2 Ending Cutscene", false, 21)]
    private static void Build()
    {
        var report = new StringBuilder("[Rule 6] Bus 2 ending cutscene");
        report.AppendLine();

        var rule = Object.FindAnyObjectByType<RuleSystem.Rule.Rule6>(FindObjectsInactive.Include);
        if (rule == null)
        {
            EditorUtility.DisplayDialog("Bus 2 Ending Cutscene",
                "No Rule6 in the open scene. Run Rule of Horror ▸ Rule 6 ▸ Build Bus Stop first.", "OK");
            return;
        }

        var ruleSo = new SerializedObject(rule);
        var paeng  = ruleSo.FindProperty("paeng").objectReferenceValue as PaengBusPointer;
        var bus    = FindBus(ruleSo, Rule6BusKind.Escape);

        if (bus == null)
        {
            EditorUtility.DisplayDialog("Bus 2 Ending Cutscene", "No bus with Kind = Escape in Rule6.buses.", "OK");
            return;
        }
        if (bus.Seat == null)
            report.AppendLine("  ! Bus 2 has no Seat / Sit Pose yet - the player cannot board it. Set the window seat first");
        if (paeng == null)
            report.AppendLine("  ! Rule6 has no Paeng - her steps will have empty targets");

        Transform root = rule.transform.Find(CutsceneName);
        if (root == null)
        {
            var go = new GameObject(CutsceneName);
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 2 Ending Cutscene");
            go.transform.SetParent(rule.transform, false);
            root = go.transform;
            report.AppendLine($"  + Created '{CutsceneName}'");
        }

        var cutscene = root.GetComponent<EndingCutscene>();
        if (cutscene == null) cutscene = Undo.AddComponent<EndingCutscene>(root.gameObject);

        if (cutscene.EditorCinematicCamera == null)
            cutscene.EditorCinematicCamera = Rule6EndingCutsceneBuilder.FindCinematicCamera();
        if (cutscene.EditorCinematicCamera == null)
            report.AppendLine("  ! No CinemachineBrain camera found - assign Cinematic Camera by hand (shot 4 needs it)");

        Transform lookTarget = paeng != null ? Rule6EndingCutsceneBuilder.EnsureLookTarget(paeng.transform, report) : null;
        Animator  animator   = paeng != null ? paeng.GetComponentInChildren<Animator>(true) : null;
        GameObject ghost     = paeng != null ? FindGhostModel(paeng.transform) : null;
        GameObject normal    = paeng != null ? EnsureNormalBody(paeng.transform, report) : null;
        if (paeng != null && ghost == null)
            report.AppendLine("  ! Could not find Paeng's ghost model - set the 'ghost off' step's Target by hand");

        var markers  = EnsureMarkers(bus, paeng, report);
        var pavilion = EnsurePavilionPoint(root, ruleSo);
        var chatData = EnsureChatAsset(report);
        var screen   = EnsureMomScreen(root, chatData, report);

        if (cutscene.EditorSteps.Count == 0)
        {
            FillSteps(cutscene.EditorSteps, bus, paeng != null ? paeng.transform : null, lookTarget, animator,
                      ghost, normal, markers, pavilion, screen);
            report.AppendLine($"  + Filled {cutscene.EditorSteps.Count} steps from the storyboard");
        }
        else
        {
            report.AppendLine("  - Steps already exist - left untouched (clear the list to regenerate)");
        }
        EditorUtility.SetDirty(cutscene);

        var slot = ruleSo.FindProperty("escapeCutscene");
        if (slot.objectReferenceValue == null)
        {
            slot.objectReferenceValue = cutscene;
            report.AppendLine("  + Rule6.escapeCutscene wired");
        }
        ruleSo.FindProperty("paengPointsAtEscape").boolValue = false;
        ruleSo.ApplyModifiedProperties();

        report.AppendLine();
        report.AppendLine("Still to do by hand:");
        report.AppendLine("  - Drag the purple figures under Bus 2 ▸ Cutscene Markers into place (door, doorway, hidden, stand, seat)");
        report.AppendLine("    'Paeng Hidden' must be somewhere the Shot 4 Door camera cannot see - the model swaps there");
        report.AppendLine("  - Frame 'Shot 4 Door' (also under Bus 2 ▸ Cutscene Markers)");
        report.AppendLine("  - Put the human model inside Paeng ▸ Normal Body when it exists");
        report.AppendLine("  - The 'nod' and 'sit' triggers need animations - point those steps at the human model's Animator");
        report.AppendLine("  - Fill the FMOD event paths on the Sound steps");
        report.AppendLine("  - Put something in Rule6 ▸ Escape Ending Object (credits / back to menu)");
        report.AppendLine("  - Run Localization ▸ 1. Setup so the chat lines get translated");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;

        Debug.Log(report.ToString(), root.gameObject);
    }

    // ─────────────────────────── Steps ───────────────────────────

    private struct Markers
    {
        public Transform door, doorway, hidden, stand, seat;
        public CinemachineCamera doorCam;
    }

    private static void FillSteps(List<Step> steps, Rule6Bus bus, Transform paeng, Transform look, Animator animator,
                                  GameObject ghost, GameObject normal, Markers m, Transform pavilion, EndingChat screen)
    {
        Transform busT = bus.transform;

        steps.Add(new Step { note = "SHOT 1 · sat down by the window", action = Action.Wait, seconds = 0.5f });

        steps.Add(new Step { note = "SHOT 2 · look out at Paeng, still a ghost", action = Action.PlayerLook,
                             lookTarget = look, seconds = 1.4f });
        steps.Add(new Step { note = "SHOT 2 · wind (put the FMOD event)", action = Action.Sound });
        steps.Add(new Step { note = "SHOT 2", action = Action.Wait, seconds = 2.5f });

        steps.Add(new Step { note = "SHOT 3 · footsteps on the road (put the FMOD event)", action = Action.Sound, soundAt = paeng });
        steps.Add(new Step { note = "SHOT 3 · she walks slowly to the bus door (our eyes follow her)", action = Action.MoveTo,
                             mover = paeng, moveTo = m.door, seconds = 3.5f });

        steps.Add(new Step { note = "SHOT 4 · camera at the door", action = Action.CameraCut, vcam = m.doorCam, seconds = 0f });
        steps.Add(new Step { note = "SHOT 4 · footsteps on the steps (put the FMOD event)", action = Action.Sound, soundAt = paeng });
        steps.Add(new Step { note = "SHOT 4 · she climbs in", action = Action.MoveTo,
                             mover = paeng, moveTo = m.doorway, seconds = 1.4f });
        steps.Add(new Step { note = "SHOT 4 · ...and goes behind the bus wall", action = Action.MoveTo,
                             mover = paeng, moveTo = m.hidden, seconds = 1.0f });

        steps.Add(new Step { note = "SHOT 5 · hidden: ghost model off", action = Action.SetActive, target = ghost, active = false });
        steps.Add(new Step { note = "SHOT 5 · hidden: normal model on", action = Action.SetActive, target = normal, active = true });
        steps.Add(new Step { note = "SHOT 5 · soft music - first time in the game (put the FMOD event)", action = Action.Sound });
        steps.Add(new Step { note = "SHOT 5 · she steps out as a normal girl", action = Action.MoveTo,
                             mover = paeng, moveTo = m.stand, seconds = 2.5f });

        steps.Add(new Step { note = "SHOT 6 · back to our eyes", action = Action.CameraCut, vcam = null, seconds = 0f });
        steps.Add(new Step { note = "SHOT 6 · look at her standing by the seat across from us", action = Action.PlayerLook,
                             lookTarget = look, seconds = 0.6f });
        steps.Add(new Step { note = "SHOT 6 · she nods - thank you (needs a 'nod' animation)", action = Action.AnimatorTrigger,
                             animator = animator, trigger = "nod" });
        steps.Add(new Step { note = "SHOT 6", action = Action.Wait, seconds = 2.5f });

        steps.Add(new Step { note = "SHOT 7 · she sits in her seat, facing the window", action = Action.MoveTo,
                             mover = paeng, moveTo = m.seat, seconds = 1.2f });
        steps.Add(new Step { note = "SHOT 7 · sit (needs a 'sit' animation)", action = Action.AnimatorTrigger,
                             animator = animator, trigger = "sit" });
        steps.Add(new Step { note = "SHOT 7 · door closes (put the FMOD event)", action = Action.Sound, soundAt = busT });
        steps.Add(new Step { note = "SHOT 7 · engine starts (put the FMOD event)", action = Action.Sound, soundAt = busT });
        steps.Add(new Step { note = "SHOT 7", action = Action.Wait, seconds = 2f });

        steps.Add(new Step { note = "SHOT 8 · Paeng rides with the bus from here", action = Action.Parent,
                             mover = paeng, parentTo = busT });
        steps.Add(new Step { note = "SHOT 8 · the bus pulls out", action = Action.BusDrive, seconds = 14f, waitToFinish = false });
        steps.Add(new Step { note = "SHOT 8 · watch the pavilion fall away past the window", action = Action.PlayerLook,
                             lookTarget = pavilion, seconds = 1.2f, holdUntilHidden = true, maxHoldSeconds = 4f });

        steps.Add(new Step { note = "SHOT 9 · cut to the chat with Mom", action = Action.SetActive,
                             target = screen != null ? screen.gameObject : null, active = true });
        steps.Add(new Step { note = "SHOT 9 · \"Mom, I'm home - is dinner ready?\"", action = Action.Chat, chat = screen });
        steps.Add(new Step { note = "SHOT 9 · cut to black", action = Action.Fade, fade = EndingCutscene.FadeDirection.ToBlack, seconds = 0f });
        steps.Add(new Step { note = "SHOT 9", action = Action.Wait, seconds = 1f });
    }

    // ─────────────────────────── Scene pieces ───────────────────────────

    private static Rule6Bus FindBus(SerializedObject ruleSo, Rule6BusKind kind)
    {
        var list = ruleSo.FindProperty("buses");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue is Rule6Bus b && b.Kind == kind)
                return b;
        return null;
    }

    /// <summary>The model Paeng shows now - her first child that renders anything, other than our own slots.</summary>
    private static GameObject FindGhostModel(Transform paeng)
    {
        foreach (Transform child in paeng)
        {
            if (child.name == NormalBody || child.name == "Cutscene Look Target") continue;
            if (child.GetComponentInChildren<Renderer>(true) != null) return child.gameObject;
        }
        return null;
    }

    private static GameObject EnsureNormalBody(Transform paeng, StringBuilder report)
    {
        var existing = paeng.Find(NormalBody);
        if (existing != null) return existing.gameObject;

        var go = new GameObject(NormalBody);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 2 Ending Cutscene");
        go.transform.SetParent(paeng, false);
        go.SetActive(false);
        report.AppendLine($"  + Added an empty '{NormalBody}' slot on Paeng (inactive) - put the human model inside");
        return go;
    }

    /// <summary>
    /// The points Paeng walks through, as children of bus 2 so they travel to the stop with it.
    /// Positions are guessed from the player's seat: the door on the pavilion side, the seat
    /// across the aisle mirrored from ours. They will need dragging into place.
    /// </summary>
    private static Markers EnsureMarkers(Rule6Bus bus, PaengBusPointer paeng, StringBuilder report)
    {
        Transform busT = bus.transform;

        Transform group = busT.Find("Cutscene Markers");
        if (group == null)
        {
            var g = new GameObject("Cutscene Markers");
            Undo.RegisterCreatedObjectUndo(g, "Build Bus 2 Ending Cutscene");
            g.transform.SetParent(busT, false);
            group = g.transform;
        }

        Transform sit   = bus.SitPose;
        float     floor = sit != null ? sit.position.y : busT.position.y + 0.5f;
        Vector3   fwd   = busT.forward;

        // Door side = the side facing Paeng (the pavilion); fall back to the bus's right.
        Vector3 side = busT.right;
        if (paeng != null && Vector3.Dot(paeng.transform.position - busT.position, busT.right) < 0f) side = -busT.right;

        Vector3 doorBase = busT.position + fwd * 2.5f;
        Vector3 ground   = new Vector3(0f, busT.position.y, 0f);

        Vector3 seatPos = sit != null ? sit.position : busT.position;
        Vector3 local   = busT.InverseTransformPoint(seatPos);
        Vector3 across  = busT.TransformPoint(new Vector3(-local.x, local.y, local.z));
        Vector3 aisle   = busT.TransformPoint(new Vector3(0f, local.y, local.z)) + fwd * 0.4f;

        Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : fwd; }

        var m = new Markers
        {
            door    = Marker(group, "Paeng 1 Door Outside", Horizontal(doorBase + side * 1.9f) + ground, Flat(-side), report),
            doorway = Marker(group, "Paeng 2 Doorway",      Horizontal(doorBase + side * 0.8f) + Vector3.up * floor, Flat(-side), report),
            hidden  = Marker(group, "Paeng 3 Hidden",       Horizontal(doorBase + side * 0.2f - fwd * 0.6f) + Vector3.up * floor, Flat(-fwd), report),
            stand   = Marker(group, "Paeng 4 Stand",        Horizontal(aisle) + Vector3.up * floor, Flat(seatPos - aisle), report),
            seat    = Marker(group, "Paeng 5 Seat",         Horizontal(across) + Vector3.up * floor, Flat(across - aisle), report),
        };

        m.doorCam = EnsureDoorCamera(group, m.door, m.doorway, side, fwd, report);
        return m;
    }

    private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static Transform Marker(Transform group, string name, Vector3 position, Vector3 facing, StringBuilder report)
    {
        var existing = group.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(CutsceneMarker));
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 2 Ending Cutscene");
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
        report.AppendLine($"  + Marker '{name}' (guessed position - drag it into place)");
        return go.transform;
    }

    private static CinemachineCamera EnsureDoorCamera(Transform group, Transform door, Transform doorway,
                                                      Vector3 side, Vector3 fwd, StringBuilder report)
    {
        const string name = "Shot 4 Door";
        var existing = group.Find(name);
        if (existing != null) return existing.GetComponent<CinemachineCamera>();

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 2 Ending Cutscene");
        go.transform.SetParent(group, false);
        var vcam = go.AddComponent<CinemachineCamera>();

        // Outside, a little behind the door, looking at the doorway at chest height.
        Vector3 pos  = door.position + side * 2.2f - fwd * 1.8f + Vector3.up * 1.5f;
        Vector3 look = doorway.position + Vector3.up * 1.2f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));

        go.SetActive(false);
        report.AppendLine($"  + Created '{name}' camera (inactive) - frame it in the Scene view");
        return vcam;
    }

    /// <summary>What we watch through the window as the bus pulls out - the pavilion bench at head height.</summary>
    private static Transform EnsurePavilionPoint(Transform root, SerializedObject ruleSo)
    {
        const string name = "Pavilion Look Point";
        var existing = root.Find(name);
        if (existing != null) return existing;

        var seat = ruleSo.FindProperty("pavilionSeat").objectReferenceValue as Transform;
        if (seat == null) return null;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 2 Ending Cutscene");
        go.transform.SetParent(root, false);
        go.transform.position = seat.position + Vector3.up * 1.2f;
        return go.transform;
    }

    // ─────────────────────────── Chat ───────────────────────────

    private static ConversationData EnsureChatAsset(StringBuilder report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ConversationData>(ChatAssetPath);
        if (existing != null) return existing;

        var data = UnityEngine.ScriptableObject.CreateInstance<ConversationData>();
        data.startNode = 0;
        data.nodes = new List<ChatNode>
        {
            Rule6EndingCutsceneBuilder.Line("วันนี้กินอะไรดีลูก เดี๋ยวแม่ทำให้กิน", mine: false, next: 1),
            Rule6EndingCutsceneBuilder.Line("แม่ ผมถึงบ้านแล้ว อาหารเย็นหรือยัง",  mine: true,  next: -1),
        };

        AssetDatabase.CreateAsset(data, ChatAssetPath);
        report.AppendLine($"  + Created {ChatAssetPath} (edit it in the Conversation Graph)");
        return data;
    }

    private static EndingChat EnsureMomScreen(Transform root, ConversationData chatData, StringBuilder report)
    {
        const string name = "Ending Chat Screen Mom";

        var existing = root.Find(name);
        EndingChat chat = existing != null ? existing.GetComponent<EndingChat>() : null;
        if (chat != null) return chat;

        chat = Rule6EndingCutsceneBuilder.BuildChatScreen(root, name, chatData, report);
        if (chat == null) return null;

        // Mom's message is already sitting in the chat when the screen comes up - only ours is typed.
        var so = new SerializedObject(chat);
        so.FindProperty("headerText").stringValue              = "แม่";
        so.FindProperty("firstMessageAlreadyThere").boolValue  = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        return chat;
    }
}
