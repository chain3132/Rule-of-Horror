using System.Collections.Generic;
using System.Linq;
using System.Text;
using Enum;
using Manager;
using Rule6;
using ScriptableObject;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Step   = Rule6.EndingCutscene.Step;
using Action = Rule6.EndingCutscene.StepAction;

/// <summary>
/// Builds the in-game ending cutscene for bus 1 (Trap) from the storyboard, in the open scene.
///
///   SHOT 1  sit down                          (Rule6 already does this before the cutscene)
///   SHOT 3  turn to look at Paeng through the window   - door closes
///   SHOT 4  bus pulls out, keep watching her until she slides out of the window
///   SHOT 5  turn back to face the front of the bus
///   SHOT 6  camera behind Paeng, bus shrinks away, she cries "I got left behind again..."
///   SHOT 7  cut to black
///   SHOT 8  back in our eyes: we lift the real phone against black, its screen lights up
///   SHOT 9  cut to a full-screen chat: we send the first rule to the next stranger
///
/// What it creates under the Rule 6 root ("Ending Cutscene 1 Trap"):
///   - EndingCutscene with the steps above, wired into Rule6.trapCutscene
///   - "Shot 6 Behind Paeng" CinemachineCamera (inactive), placed behind Paeng facing the road
///   - "Shot 8 Phone": EndingPhone that lifts the player's real phone (visual only)
///   - "Ending Chat Screen": a full-screen copy of the phone's ChatPanel (same bubbles, header,
///     fonts) with an EndingChat on it - it does not touch the real phone or its chat history
///   - "Cutscene Look Target" on Paeng at head height
///   - Conversation asset Rule6_TrapEndingChat for the chat lines
///
/// Re-runnable: everything is looked up by name first. Steps are only filled when the list is
/// empty, so hand-tuned timings survive a second run. Delete the steps to regenerate them.
/// </summary>
public static class Rule6EndingCutsceneBuilder
{
    private const string CutsceneName = "Ending Cutscene 1 Trap";
    private const string ChatAssetPath = "Assets/Scripts/Scriptable/ScriptableObject/Rule6_TrapEndingChat.asset";

    [MenuItem("Rule of Horror/Rule 6/Build Bus 1 Ending Cutscene", false, 20)]
    private static void Build()
    {
        var report = new StringBuilder("[Rule 6] Bus 1 ending cutscene");
        report.AppendLine();

        var rule = Object.FindAnyObjectByType<RuleSystem.Rule.Rule6>(FindObjectsInactive.Include);
        if (rule == null)
        {
            EditorUtility.DisplayDialog("Bus 1 Ending Cutscene",
                "No Rule6 in the open scene. Run Rule of Horror ▸ Rule 6 ▸ Build Bus Stop first.", "OK");
            return;
        }

        var ruleSo = new SerializedObject(rule);
        var paeng  = ruleSo.FindProperty("paeng").objectReferenceValue as PaengBusPointer;
        var bus    = FindTrapBus(ruleSo);

        if (paeng == null) report.AppendLine("  ! Rule6 has no Paeng - look/animation steps will have empty targets");
        if (bus   == null) report.AppendLine("  ! No bus with Kind = Trap in Rule6.buses - sound steps have no position");

        // ── Cutscene object ──
        Transform root = rule.transform.Find(CutsceneName);
        if (root == null)
        {
            var go = new GameObject(CutsceneName);
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 1 Ending Cutscene");
            go.transform.SetParent(rule.transform, false);
            root = go.transform;
            report.AppendLine($"  + Created '{CutsceneName}'");
        }

        var cutscene = root.GetComponent<EndingCutscene>();
        if (cutscene == null) cutscene = Undo.AddComponent<EndingCutscene>(root.gameObject);

        Transform lookTarget = paeng != null ? EnsureLookTarget(paeng.transform, report) : null;
        Animator  animator   = paeng != null ? paeng.GetComponentInChildren<Animator>(true) : null;
        var       vcam       = EnsureShot6Camera(root, paeng, ruleSo, report);
        var       chatData   = EnsureChatAsset(report);
        var       screen     = EnsureChatScreen(root, chatData, report);
        var       phone      = EnsurePhone(root, report);

        if (cutscene.EditorCinematicCamera == null)
        {
            cutscene.EditorCinematicCamera = FindCinematicCamera();
            report.AppendLine(cutscene.EditorCinematicCamera != null
                ? $"  + Cinematic Camera = '{cutscene.EditorCinematicCamera.name}'"
                : "  ! No CinemachineBrain camera found - assign Cinematic Camera by hand (shot 6 needs it)");
        }

        if (cutscene.EditorSteps.Count == 0)
        {
            FillSteps(cutscene.EditorSteps, bus != null ? bus.transform : null, paeng, lookTarget,
                      animator, vcam, phone, screen);
            report.AppendLine($"  + Filled {cutscene.EditorSteps.Count} steps from the storyboard");
        }
        else
        {
            UpdateShots8And9(cutscene.EditorSteps, phone, screen, report);
            report.AppendLine("  - Other steps already exist - left untouched (clear the list to regenerate)");
        }

        EditorUtility.SetDirty(cutscene);

        var trap = ruleSo.FindProperty("trapCutscene");
        if (trap.objectReferenceValue == null)
        {
            trap.objectReferenceValue = cutscene;
            ruleSo.ApplyModifiedProperties();
            report.AppendLine("  + Rule6.trapCutscene wired");
        }

        report.AppendLine();
        report.AppendLine("Still to do by hand:");
        report.AppendLine("  - Frame 'Shot 6 Behind Paeng' in the Scene view");
        report.AppendLine("  - Fill the FMOD event paths on the Sound steps (door, sob, unlock) and on Ending Chat Screen");
        report.AppendLine("  - Put something in Rule6 ▸ Trap Ending Object (credits / back to menu) - otherwise the chat screen is the last frame forever");
        report.AppendLine("  - Paeng's Animator needs a 'cry' trigger, or change/remove that step");
        report.AppendLine("  - Run Localization ▸ 1. Setup and 3. Localize Scenes so the new texts get translated");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;

        Debug.Log(report.ToString(), root.gameObject);
    }

    // ─────────────────────────── Steps ───────────────────────────

    private static void FillSteps(List<Step> steps, Transform bus, PaengBusPointer paeng, Transform lookTarget,
                                  Animator animator, CinemachineCamera vcam, EndingPhone phone, EndingChat screen)
    {
        Transform paengT = paeng != null ? paeng.transform : null;

        steps.Add(new Step { note = "SHOT 1 · sat down - a beat before anything moves", action = Action.Wait, seconds = 0.5f });

        steps.Add(new Step { note = "SHOT 3 · door closes (put the FMOD event)", action = Action.Sound, soundAt = bus });
        steps.Add(new Step { note = "SHOT 3 · turn to look at Paeng through the window", action = Action.PlayerLook,
                             lookTarget = lookTarget, seconds = 1.4f });
        steps.Add(new Step { note = "SHOT 3 · she stands there looking at us", action = Action.Wait, seconds = 2.5f });

        steps.Add(new Step { note = "SHOT 4 · bus pulls out", action = Action.BusDrive, seconds = 16f, waitToFinish = false });
        steps.Add(new Step { note = "SHOT 4 · keep watching until she slides out of the window", action = Action.PlayerLook,
                             lookTarget = lookTarget, seconds = 0f, holdUntilHidden = true, maxHoldSeconds = 6f });

        steps.Add(new Step { note = "SHOT 5 · turn back to the front of the bus", action = Action.PlayerLook,
                             lookTarget = null, seconds = 1.2f });
        steps.Add(new Step { note = "SHOT 5", action = Action.Wait, seconds = 1.5f });

        steps.Add(new Step { note = "SHOT 6 · behind Paeng, the bus drives away", action = Action.CameraCut, vcam = vcam, seconds = 0f });
        steps.Add(new Step { note = "SHOT 6 · Paeng cries", action = Action.AnimatorTrigger, animator = animator, trigger = "cry" });
        steps.Add(new Step { note = "SHOT 6 · sobbing (put the FMOD event)", action = Action.Sound, soundAt = paengT });
        steps.Add(new Step { note = "SHOT 6 · her line", action = Action.Dialogue, waitToFinish = false,
                             lines = new[] { new DialogueLine { text = "ฉันโดนทิ้งอีกแล้ว..." } } });
        steps.Add(new Step { note = "SHOT 6", action = Action.Wait, seconds = 4f });

        steps.Add(new Step { note = "SHOT 7 · cut to black", action = Action.Fade, fade = EndingCutscene.FadeDirection.ToBlack, seconds = 0f });
        steps.Add(new Step { note = "SHOT 7", action = Action.Wait, seconds = 1f });

        AddShots8And9(steps, phone, screen);
    }

    private static void AddShots8And9(List<Step> steps, EndingPhone phone, EndingChat screen)
    {
        steps.Add(new Step { note = "SHOT 8 · back to our own eyes (still black)", action = Action.CameraCut, vcam = null, seconds = 0f });
        steps.Add(new Step { note = "SHOT 8 · we lift the phone - black around it, screen lights up", action = Action.Phone,
                             phone = phone, show = true, waitToFinish = false });
        steps.Add(new Step { note = "SHOT 8", action = Action.Fade, fade = EndingCutscene.FadeDirection.FromBlack, seconds = 0.3f });
        steps.Add(new Step { note = "SHOT 8 · unlock (put the FMOD event)", action = Action.Sound });
        steps.Add(new Step { note = "SHOT 8", action = Action.Wait, seconds = 2.8f });

        steps.Add(new Step { note = "SHOT 9 · cut to the chat screen", action = Action.SetActive,
                             target = screen != null ? screen.gameObject : null, active = true });
        steps.Add(new Step { note = "SHOT 9 · we send the first rule to the next person", action = Action.Chat,
                             chat = screen });
    }

    /// <summary>
    /// Steps already exist (hand-tuned): only bring shots 8-9 up to date. An earlier version had
    /// no phone shot - everything from the first "SHOT 8" step on is replaced by the current
    /// shots 8-9; steps before it are not touched.
    /// </summary>
    private static void UpdateShots8And9(List<Step> steps, EndingPhone phone, EndingChat screen, StringBuilder report)
    {
        bool hasPhone = false;
        foreach (var step in steps)
        {
            if (step.action == Action.Phone)
            {
                hasPhone = true;
                if (step.phone == null) step.phone = phone;
            }
            else if (step.action == Action.Chat && step.chat == null) step.chat = screen;
        }
        if (hasPhone) return;

        int from = steps.FindIndex(s => s.note != null && s.note.StartsWith("SHOT 8"));
        if (from < 0)
        {
            report.AppendLine("  ! No step noted 'SHOT 8' - add a Phone step before the chat by hand");
            return;
        }

        steps.RemoveRange(from, steps.Count - from);
        AddShots8And9(steps, phone, screen);
        report.AppendLine("  ~ Rebuilt shots 8-9: lift the phone, then cut to the chat screen");
    }

    // ─────────────────────────── Scene pieces ───────────────────────────

    private static Rule6Bus FindTrapBus(SerializedObject ruleSo)
    {
        var list = ruleSo.FindProperty("buses");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue is Rule6Bus b && b.Kind == Rule6BusKind.Trap)
                return b;
        return null;
    }

    internal static Transform EnsureLookTarget(Transform paeng, StringBuilder report)
    {
        const string name = "Cutscene Look Target";
        var existing = paeng.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 1 Ending Cutscene");
        go.transform.SetParent(paeng, false);
        go.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        report.AppendLine("  + Added 'Cutscene Look Target' on Paeng at head height (move it to her face)");
        return go.transform;
    }

    private static CinemachineCamera EnsureShot6Camera(Transform root, PaengBusPointer paeng,
                                                       SerializedObject ruleSo, StringBuilder report)
    {
        const string name = "Shot 6 Behind Paeng";
        var existing = root.Find(name);
        if (existing != null) return existing.GetComponent<CinemachineCamera>();

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 1 Ending Cutscene");
        go.transform.SetParent(root, false);
        var vcam = go.AddComponent<CinemachineCamera>();

        // Over Paeng's shoulder, looking down the road the bus leaves by.
        var depart = ruleSo.FindProperty("departPoint").objectReferenceValue as Transform;
        if (paeng != null)
        {
            Vector3 p    = paeng.transform.position;
            Vector3 look = depart != null ? depart.position : p + paeng.transform.forward * 20f;
            Vector3 away = look - p; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = paeng.transform.forward;

            go.transform.position = p - away.normalized * 2.2f + Vector3.up * 1.7f + Vector3.Cross(Vector3.up, away.normalized) * 0.6f;
            go.transform.rotation = Quaternion.LookRotation(look + Vector3.up * 1f - go.transform.position);
        }

        go.SetActive(false);
        report.AppendLine($"  + Created '{name}' (inactive) - frame it in the Scene view");
        return vcam;
    }

    internal static GameObject FindCinematicCamera()
    {
        var cutscene = Object.FindAnyObjectByType<CutsceneManager>(FindObjectsInactive.Include);
        if (cutscene != null)
        {
            var go = new SerializedObject(cutscene).FindProperty("cutsceneCameraGO").objectReferenceValue as GameObject;
            if (go != null) return go;
        }

        var brain = Object.FindAnyObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        return brain != null ? brain.gameObject : null;
    }

    // ─────────────────────────── Chat asset ───────────────────────────

    private static ConversationData EnsureChatAsset(StringBuilder report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ConversationData>(ChatAssetPath);
        if (existing != null) return existing;

        var data = UnityEngine.ScriptableObject.CreateInstance<ConversationData>();
        data.startNode = 0;
        data.nodes = new List<ChatNode>
        {
            Line("คุณเชื่อชีวิตหลังความตายไหม",         mine: true,  next: 1),
            Line("บางคนก็เชื่อนะ แต่บางคนก็ไม่",          mine: true,  next: 2),
            Line("ใครครับเนี่ย?",                         mine: false, next: 3),
            Line("ส่งผิดคนรึเปล่า",                       mine: false, next: 4),
            Line("ถ้าคุณอยากรอด ข้อแรก ห้ามไปไกลกว่านี้", mine: true,  next: -1),
        };

        AssetDatabase.CreateAsset(data, ChatAssetPath);
        report.AppendLine($"  + Created {ChatAssetPath} (edit it in the Conversation Graph)");
        return data;
    }

    internal static ChatNode Line(string text, bool mine, int next) => new ChatNode
    {
        message      = text,
        isPlayer     = mine,
        scriptedLine = mine,
        nextNode     = next,
        replies      = new List<ReplyOption>(),
    };

    // ─────────────────────────── Chat screen ───────────────────────────

    /// <summary>
    /// A full-screen copy of the phone's ChatPanel. Copying the real panel (rather than building
    /// a look-alike) keeps the bubbles, header, colours and fonts identical to the in-game chat,
    /// and it never touches the real phone, so the chat history and phone input are left alone.
    ///
    /// Earlier versions of this menu made an "Ending Phone" (a fake phone canvas, then a driver
    /// for the real phone). If one is still there, steps pointing at it are moved to the chat
    /// screen and it is deleted.
    /// </summary>
    private static EndingChat EnsureChatScreen(Transform root, ConversationData chatData, StringBuilder report)
    {
        const string name = "Ending Chat Screen";

        var existing = root.Find(name);
        EndingChat chat = existing != null ? existing.GetComponent<EndingChat>() : null;
        if (chat == null) chat = BuildChatScreen(root, name, chatData, report);

        RemoveOldPhone(root, report);
        return chat;
    }

    internal static EndingChat BuildChatScreen(Transform root, string name, ConversationData chatData,
                                              StringBuilder report)
    {
        var chatUi = Object.FindAnyObjectByType<ChatUIController>(FindObjectsInactive.Include);
        if (chatUi == null)
        {
            report.AppendLine("  ! No ChatUIController in the scene - cannot copy the ChatPanel");
            return null;
        }

        var ui = new SerializedObject(chatUi);
        Object Ref(string field) => ui.FindProperty(field)?.objectReferenceValue;

        var content = Ref("contentRoot") as Transform;
        var scroll  = Ref("scrollRect") as ScrollRect;
        var header  = Ref("headerNameText") as TMP_Text;
        var avatar  = Ref("headerAvatarImage") as Image;
        var reply   = Ref("replyRoot") as Transform;

        Transform panel = content != null ? FindNamedAncestor(content, "ChatPanel") : null;
        if (panel == null || panel.name != "ChatPanel")
        {
            report.AppendLine("  ! Could not find the 'ChatPanel' above ChatUIController.contentRoot - nothing copied");
            return null;
        }

        // Same proportions as the phone: scale by height against the phone canvas's own size,
        // so on a wide monitor the chat gets wider while bubbles and text stay the phone's size.
        var phoneCanvas = panel.GetComponentInParent<Canvas>(true);
        Vector2 refSize = phoneCanvas != null ? ((RectTransform)phoneCanvas.rootCanvas.transform).rect.size : Vector2.zero;
        if (refSize.x < 10f || refSize.y < 10f) refSize = new Vector2(1080f, 1920f);

        var canvasGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Build Bus 1 Ending Cutscene");
        canvasGo.transform.SetParent(root, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;   // under the cutscene's black fade (500), so fading in reveals it

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = refSize;
        scaler.matchWidthOrHeight  = 1f;

        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(canvasGo.transform, false);
        black.GetComponent<Image>().color = Color.black;
        black.GetComponent<Image>().raycastTarget = false;
        Stretch((RectTransform)black.transform);

        var copy = Object.Instantiate(panel.gameObject, canvasGo.transform, false);
        copy.name = "ChatPanel";
        copy.SetActive(true);
        Stretch((RectTransform)copy.transform);

        Transform Map(Transform src)
        {
            if (src == null) return null;
            if (src == panel) return copy.transform;
            return copy.transform.Find(AnimationUtility.CalculateTransformPath(src, panel));
        }

        StripBehaviour(copy, header != null ? Map(header.transform) : null);

        var copyContent = Map(content);
        if (copyContent != null)
            for (int i = copyContent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(copyContent.GetChild(i).gameObject);

        if (reply  != null) { var r = Map(FindNamedAncestor(reply, "ReplyPanel")); if (r != null) r.gameObject.SetActive(false); }
        if (avatar != null) { var a = Map(avatar.transform); if (a != null) a.gameObject.SetActive(false); }

        var chat = canvasGo.AddComponent<EndingChat>();
        var so   = new SerializedObject(chat);
        so.FindProperty("conversation").objectReferenceValue       = chatData;
        so.FindProperty("contentRoot").objectReferenceValue        = copyContent;
        so.FindProperty("scrollRect").objectReferenceValue         = scroll != null ? Map(scroll.transform)?.GetComponent<ScrollRect>() : null;
        so.FindProperty("headerLabel").objectReferenceValue        = header != null ? Map(header.transform)?.GetComponent<TMP_Text>() : null;
        so.FindProperty("leftBubblePrefab").objectReferenceValue   = Ref("leftBubblePrefab");
        so.FindProperty("rightBubblePrefab").objectReferenceValue  = Ref("rightBubblePrefab");
        so.FindProperty("typingBubblePrefab").objectReferenceValue = Ref("typingBubblePrefab");
        so.ApplyModifiedPropertiesWithoutUndo();

        canvasGo.SetActive(false);
        report.AppendLine($"  + Created '{name}' - a full-screen copy of the phone's ChatPanel (inactive)");
        return chat;
    }

    /// <summary>
    /// Leaves only the look: UI graphics, layout, TMP and localization stay; game scripts (the
    /// ChatUIController and anything else that would react to the real phone) go. The header
    /// also loses any LocalizeStringEvent, because EndingChat sets its text itself.
    /// </summary>
    private static void StripBehaviour(GameObject copy, Transform header)
    {
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;

            string ns = mb.GetType().Namespace ?? "";
            bool look = ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") || mb is LocalizedFontAsset;
            bool headerText = header != null && mb.transform == header
                              && mb is UnityEngine.Localization.Components.LocalizeStringEvent;

            if (!look || headerText) Object.DestroyImmediate(mb);
        }
    }

    /// <summary>Earlier versions of this menu made an "Ending Phone" object - "Shot 8 Phone" replaces it.</summary>
    private static void RemoveOldPhone(Transform root, StringBuilder report)
    {
        var old = new List<GameObject>();
        foreach (Transform child in root)
            if (child.name.StartsWith("Ending Phone")) old.Add(child.gameObject);

        foreach (var go in old) Undo.DestroyObjectImmediate(go);
        if (old.Count > 0) report.AppendLine("  ~ Removed the old 'Ending Phone'");
    }

    // ─────────────────────────── Shot 8 phone ───────────────────────────

    private static EndingPhone EnsurePhone(Transform root, StringBuilder report)
    {
        const string name = "Shot 8 Phone";

        var existing = root.Find(name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 1 Ending Cutscene");
            go.transform.SetParent(root, false);
            report.AppendLine($"  + Created '{name}'");
        }

        var phone = go.GetComponent<EndingPhone>();
        if (phone == null) phone = Undo.AddComponent<EndingPhone>(go);

        var system = Object.FindAnyObjectByType<PhoneSystem.PhoneSystem>(FindObjectsInactive.Include);
        if (system == null)
        {
            report.AppendLine("  ! No PhoneSystem in the scene - set up Shot 8 Phone by hand");
            return phone;
        }

        var phoneObject = new SerializedObject(system).FindProperty("phoneObject").objectReferenceValue as GameObject;
        Transform phoneRoot = phoneObject != null ? phoneObject.transform : null;

        Renderer screen = phoneRoot == null ? null
            : phoneRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == PhoneScreenQuadBuilder.QuadName);
        if (screen == null)
            report.AppendLine("  ! No ScreenQuad under the phone - the screen will not glow in shot 8");

        // "New chat" on the phone screen: the real ChatPanel with the chat system paused and the
        // bubbles, reply panel and avatar hidden - nothing is deleted, Hide() puts it all back.
        var chatUi = new SerializedObject(system).FindProperty("chatUI").objectReferenceValue as ChatUIController;
        if (chatUi == null) chatUi = Object.FindAnyObjectByType<ChatUIController>(FindObjectsInactive.Include);
        SerializedObject ui = chatUi != null ? new SerializedObject(chatUi) : null;
        Object Ref(string field) => ui?.FindProperty(field)?.objectReferenceValue;

        var hide = new List<Object>();
        if (Ref("contentRoot") is Transform content) hide.Add(content.gameObject);
        if (Ref("replyRoot") is Transform reply) hide.Add(FindNamedAncestor(reply, "ReplyPanel").gameObject);
        if (Ref("headerAvatarImage") is Image avatar) hide.Add(avatar.gameObject);

        var so = new SerializedObject(phone);
        so.FindProperty("phoneSystem").objectReferenceValue    = system;
        so.FindProperty("phoneRoot").objectReferenceValue      = phoneRoot;
        so.FindProperty("screenRenderer").objectReferenceValue = screen;
        so.FindProperty("headerName").objectReferenceValue     = Ref("headerNameText");
        SetArray(so.FindProperty("pauseWhileShowing"), chatUi != null ? new Object[] { chatUi } : new Object[0]);
        SetArray(so.FindProperty("hideWhileShowing"), hide.ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();

        if (chatUi == null) report.AppendLine("  ! No ChatUIController - shot 8 will show the phone's own chat as-is");

        return phone;
    }

    /// <summary>The nearest ancestor (or self) with this name - falls back to the given transform.</summary>
    private static Transform FindNamedAncestor(Transform t, string name)
    {
        for (var c = t; c != null; c = c.parent)
            if (c.name == name) return c;
        return t;
    }

    internal static void SetArray(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
