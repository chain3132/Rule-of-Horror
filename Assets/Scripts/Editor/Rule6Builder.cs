using System.Collections.Generic;
using System.Text;
using Enum;
using InputSystem;
using Manager;
using RuleSystem;
using Rule6;
using ScriptableObject;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scaffolds Rule 6 (the bus stop) in the open scene, then wires every reference it can find.
///
/// Rule 6 is four buses that pull up one at a time, each one an ending. The code is generic -
/// every bus is the same component with different Inspector values - which means the setup work
/// is almost entirely dragging the same eight references four times. That is what this does.
///
/// What it builds:
///
///   Rule 6                       Rule6 component, registered in RuleManager.rules
///     Route/Approach             where a bus appears, off past the bend
///     Route/Stop                 where it halts in front of the pavilion
///     Route/Depart               where it disappears
///     Bus 1 Trap / 2 Escape / 3 Death / 4 Loop
///       Seat                     press E beside it to sit = choosing that bus
///       Sit Pose                 where the player's FEET land when seated
///       Interior Light           only bus 3 needs it wired (the flicker)
///       Head Light L / R         only bus 4 needs them wired (forced off)
///     Paeng                      stands in front of the pavilion, points at bus 2
///
/// Re-runnable: it looks objects up by name under the Rule 6 root and reuses whatever is already
/// there, so running it again after hand-tuning positions does not reset them.
///
/// What it deliberately does NOT do:
///
///   - Place the route on the actual road. It can only guess from the SittingPoint in the scene,
///     and a bus floating through a tree is more obvious than a bus that never arrives, so the
///     points are deliberately left as plain empties to be dragged.
///   - Touch anything the bus model already carries: the destination texture, the seats, the
///     passengers, the blood, the soaked paintwork. Those are art, and the code that used to
///     set them has been removed.
///   - Create the FMOD events or the four ending screens. Both are reported as leftovers.
/// </summary>
public static class Rule6Builder
{
    private const string RootName       = "Rule 6";
    private const string IntroBusName   = "Intro Bus";
    private const string RuleEntryPath  = "Assets/Scriptable/Rules/RuleEntry_6.asset";

    // A Thai single-decker is about 10m x 2.5m x 3m. Every offset below is in that box, measured
    // from the bus origin at its centre, so the holders land near the right part of a real mesh.
    private const float BusLength = 10f;
    private const float BusWidth  = 2.5f;

    // A window seat a quarter of the way back, on the kerb side (traffic drives on the left
    // here, so that is the side the player walks in from). Floor height, not ground height.
    private static readonly Vector3 SeatOffset     = new Vector3(-BusWidth * 0.3f, 0.9f, BusLength * 0.25f);

    // Roughly a seat cushion below the seat marker. SitRoutine puts the character's ROOT (its
    // feet) at the pose it is given, so a pose on the cushion leaves the player hovering by
    // exactly this much - the single most likely thing to look wrong on a first Play.
    private const float SeatCushionHeight = 0.45f;

    // Sideways step from the sit pose into the aisle. The seat is on the kerb side, so the aisle
    // is the other way (+X in bus space). Standing up ON the seat leaves the character capsule
    // inside the seat mesh, and the player cannot move at all - so every bus the player stands
    // up in needs this, and the fifth decimal of it is eyeballed against the real model.
    private const float AisleStep = 0.9f;

    // Roof height of the passenger cabin box, from the bus origin at road level.
    private const float CabinHeight = 3.2f;
    private static readonly Vector3 InteriorOffset = new Vector3(0f, 2.2f, 0f);
    private static readonly Vector3 HeadLightLeft  = new Vector3(-0.9f, 0.9f, BusLength * 0.5f);
    private static readonly Vector3 HeadLightRight = new Vector3(0.9f, 0.9f, BusLength * 0.5f);

    // How far up and down the road a bus is spawned / removed. Far enough that the headlights
    // sweep in from somewhere rather than blinking into existence in front of the player.
    private const float RouteDistance = 45f;

    [MenuItem("Rule of Horror/Rule 6/Build Bus Stop", false, 10)]
    private static void Build()
    {
        var report = new StringBuilder("[Rule 6] Bus stop setup");
        report.AppendLine();

        Transform root = EnsureRoot(report);
        var rule = root.GetComponent<RuleSystem.Rule.Rule6>();

        Transform anchor  = FindPavilion(report);
        Transform[] route = BuildRoute(root, anchor, report);
        List<Rule6Bus> buses = BuildBuses(root, route[1], report);
        PaengBusPointer paeng = BuildPaeng(root, anchor, route[1], report);

        WireRule(rule, route, buses, paeng, anchor, report);
        RegisterWithRuleManager(rule, report);

        RuleEntryData entry = EnsureRuleEntry(report);
        RegisterWithRuleBook(entry, report);

        EnsureDevSkip(root, rule, report);
        EnsurePermanentDeath(root, report);
        EnsureIntroBus(root, route, report);

        ReportLeftovers(report);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;

        Debug.Log(report.ToString(), root.gameObject);
    }

    // ─────────────────────────── Root ───────────────────────────

    private static Transform EnsureRoot(StringBuilder report)
    {
        var existing = GameObject.Find(RootName);

        if (existing != null && existing.GetComponent<RuleSystem.Rule.Rule6>() != null)
        {
            report.AppendLine($"  - Reusing the existing '{RootName}' object");
            return existing.transform;
        }

        var go = existing ?? new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Build Rule 6");

        var rule = go.GetComponent<RuleSystem.Rule.Rule6>() ?? go.AddComponent<RuleSystem.Rule.Rule6>();

        // The window has to sit after Rule 5 (23:00-23:40). The clock counts hours linearly past
        // 24 on purpose - see TimeManager.Format - so 24:00 really is "midnight, same night" and
        // still compares as later than everything before it. The phone shows 00:00.
        var so = new SerializedObject(rule);
        so.FindProperty("startHour").intValue   = 24;
        so.FindProperty("startMinute").intValue = 0;
        so.FindProperty("endHour").intValue     = 24;
        so.FindProperty("endMinute").intValue   = 40;
        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine($"  - Created '{RootName}' with the Rule6 component, window 24:00 - 24:40 " +
                          "(shown as 00:00 - 00:40 on the phone)");
        return go.transform;
    }

    /// <summary>
    /// The bench in the scene, which is the one fixed point every rule is measured from.
    ///
    /// Used for two things: the seat the player is put back on in the trap and loop endings, and
    /// a starting guess for where the road is. Nothing breaks when there is no SittingPoint - the
    /// route just lands at the origin of the Rule 6 object instead.
    /// </summary>
    private static Transform FindPavilion(StringBuilder report)
    {
        var sit = Object.FindAnyObjectByType<SittingPoint>(FindObjectsInactive.Include);

        if (sit == null)
        {
            report.AppendLine("  ! No SittingPoint in the scene - the route and Paeng are placed at " +
                              "the Rule 6 object instead, and Pavilion Seat is left empty");
            return null;
        }

        report.AppendLine($"  - Measuring from the SittingPoint '{sit.name}'");
        return sit.transform;
    }

    // ─────────────────────────── Route ───────────────────────────

    /// <summary>Returns { approach, stop, depart }.</summary>
    private static Transform[] BuildRoute(Transform root, Transform anchor, StringBuilder report)
    {
        Transform routeRoot = EnsureChild(root, "Route");

        // The bench faces the road, so its forward is a usable first guess for where to put the
        // bus, and its right is a usable guess for which way the road runs
        Vector3    stopPos = anchor != null ? anchor.position + anchor.forward * 8f : root.position;
        Quaternion stopRot = anchor != null ? Quaternion.LookRotation(anchor.right, Vector3.up)
                                            : root.rotation;

        Transform stop     = EnsureChild(routeRoot, "Stop",     stopPos, stopRot);
        Transform approach = EnsureChild(routeRoot, "Approach", stopPos - stop.forward * RouteDistance, stopRot);
        Transform depart   = EnsureChild(routeRoot, "Depart",   stopPos + stop.forward * RouteDistance, stopRot);

        report.AppendLine($"  - Route: Approach -> Stop -> Depart, {RouteDistance:0}m each side, " +
                          "all facing the way the bus drives");
        report.AppendLine("  ! Drag Stop onto the road in front of the pavilion and rotate it so the " +
                          "bus's LEFT side (the door) faces the kerb. Approach and Depart only need to " +
                          "be out of sight");

        return new[] { approach, stop, depart };
    }

    // ─────────────────────────── Buses ───────────────────────────

    private static List<Rule6Bus> BuildBuses(Transform root, Transform stop, StringBuilder report)
    {
        var buses = new List<Rule6Bus>
        {
            BuildBus(root, stop, "Bus 1 Trap",   Rule6BusKind.Trap,   report),
            BuildBus(root, stop, "Bus 2 Escape", Rule6BusKind.Escape, report),
            BuildBus(root, stop, "Bus 3 Death",  Rule6BusKind.Death,  report),
            BuildBus(root, stop, "Bus 4 Loop",   Rule6BusKind.Loop,   report),
        };

        return buses;
    }

    private static Rule6Bus BuildBus(Transform root, Transform stop, string name,
                                     Rule6BusKind kind, StringBuilder report)
    {
        // Parked at the stop so the holders can be eyeballed against the bus mesh in the Scene
        // view. Rule6 moves the bus to Approach itself before it drives in, so where it starts
        // is only ever a convenience for whoever is dressing it.
        Transform bus = EnsureChild(root, name, stop.position, stop.rotation);

        // The seat the player sits down on. The bus has no door, so walking aboard needs no key
        // press, but SITTING is the decision - and that is an E press, like the pavilion bench.
        // Walking past the seat has to stay harmless, or the player cannot read the interior.
        // Positioned at a window seat about a quarter back from the front, which is where the
        // design puts the free seat on bus 1 - move it onto the real seat in the model.
        Transform seat = EnsureChild(bus, "Seat", bus.TransformPoint(SeatOffset), bus.rotation);

        // Where the player's feet go. A guess one cushion below the seat marker - the gizmo on
        // the Rule6Bus component draws the seated body from here so the rest can be eyeballed.
        Transform sitPose = EnsureChild(bus, "Sit Pose",
            seat.position - Vector3.up * SeatCushionHeight, seat.rotation);

        // Where the player stands up to. Only bus 4 and the Intro Bus ever need it, but all four
        // get one so nobody has to work out which bus was supposed to have it.
        Transform standPose = EnsureChild(bus, "Stand Pose",
            sitPose.position + bus.right * AisleStep, seat.rotation);
        Transform cabin = EnsureCabin(bus);

        Transform interior  = EnsureChild(bus, "Interior Light", bus.TransformPoint(InteriorOffset), bus.rotation);
        Transform headLeft  = EnsureChild(bus, "Head Light L",   bus.TransformPoint(HeadLightLeft),  bus.rotation);
        Transform headRight = EnsureChild(bus, "Head Light R",   bus.TransformPoint(HeadLightRight), bus.rotation);

        Light interiorLight = EnsureLight(interior,  LightType.Point);
        Light headL         = EnsureLight(headLeft,  LightType.Spot);
        Light headR         = EnsureLight(headRight, LightType.Spot);

        var component = bus.GetComponent<Rule6Bus>() ?? Undo.AddComponent<Rule6Bus>(bus.gameObject);
        var so = new SerializedObject(component);

        so.FindProperty("kind").enumValueIndex          = (int)kind;
        so.FindProperty("seat").objectReferenceValue    = seat;
        so.FindProperty("sitPose").objectReferenceValue   = sitPose;
        so.FindProperty("standPose").objectReferenceValue = standPose;
        so.FindProperty("cabinBounds").objectReferenceValue = cabin;

        SetArray(so.FindProperty("headLights"), new Object[] { headL, headR });

        ApplyLook(so, kind, interiorLight, headL, headR, report);

        so.ApplyModifiedPropertiesWithoutUndo();
        return component;
    }

    /// <summary>
    /// The three behaviours from the design doc that cannot be baked into a model.
    ///
    /// Everything else a player reads off a bus - the destination texture, the seats, the
    /// passengers, the blood, the soaked paintwork - comes with the mesh. What is left here is
    /// only what moves: bus 3's interior flicker, bus 4's dead headlights, and bus 4's silence.
    ///
    /// Those three ARE the puzzle for anyone who did not read Paeng's story, so getting them
    /// wrong does not break anything - it just makes the rule unsolvable, which is exactly the
    /// kind of bug that cannot be seen by Play-testing any single bus.
    /// </summary>
    private static void ApplyLook(SerializedObject so, Rule6BusKind kind,
                                  Light interior, Light headL, Light headR, StringBuilder report)
    {
        var flicker = so.FindProperty("flickerInterior");
        var headOff = so.FindProperty("headLightsOff");
        var engine  = so.FindProperty("engineEvent");
        var lights  = so.FindProperty("interiorLights");

        switch (kind)
        {
            case Rule6BusKind.Trap:
                flicker.boolValue  = false;
                headOff.boolValue  = false;
                engine.stringValue = "event:/Rule6/BusEngine";

                report.AppendLine("  - Bus 1 Trap: nothing to drive. The model carries the warm bright " +
                                  "interior, the village sign, the full seats and the man on his phone. " +
                                  "Put Seat on the ONE free seat - the seat that has no room for Paeng " +
                                  "is the whole trap");
                break;

            case Rule6BusKind.Escape:
                flicker.boolValue  = false;
                headOff.boolValue  = false;
                engine.stringValue = "event:/Rule6/BusEngine";

                report.AppendLine("  - Bus 2 Escape: nothing to drive. The model carries the dim light, " +
                                  "the blank sign and the empty seats. Paeng points at this one");
                break;

            case Rule6BusKind.Death:
                flicker.boolValue  = true;                      // ไฟในติดๆ ดับๆ
                headOff.boolValue  = false;
                engine.stringValue = "event:/Rule6/BusEngine";

                // The flicker is the only thing on this bus the code owns, so this is the one
                // bus whose interior lights MUST be in the array or the tell is simply missing
                SetArray(lights, new Object[] { interior });

                report.AppendLine("  - Bus 3 Death: interior flicker ON, wired to the Interior Light child. " +
                                  "Swap that for the lights inside the model if it has its own, " +
                                  "otherwise this bus looks identical to bus 1 from outside");
                break;

            default:
                flicker.boolValue  = false;
                headOff.boolValue  = true;                      // ไฟหน้าดับ
                engine.stringValue = "";                        // เครื่องยนต์ไม่มีเสียง

                report.AppendLine("  - Bus 4 Loop: headlights forced off and the engine event left BLANK " +
                                  "on purpose - silence is its tell. The model carries the ศาลา sign, " +
                                  "the soaked paintwork and the single passenger in the player's shirt");
                break;
        }

        interior.shadows = LightShadows.None;   // an interior bulb shadow-casting through seats is not worth a frame
        headL.shadows    = LightShadows.Soft;
        headR.shadows    = LightShadows.Soft;
    }

    // ─────────────────────────── Paeng ───────────────────────────

    private static PaengBusPointer BuildPaeng(Transform root, Transform anchor, Transform stop,
                                              StringBuilder report)
    {
        // In front of the pavilion, between the bench and the road, so she is in frame from the
        // seat without standing in the way of the walk to the bus door
        Vector3 pos = anchor != null ? anchor.position + anchor.forward * 2.5f : root.position;

        Transform paeng = EnsureChild(root, "Paeng", pos,
            anchor != null ? Quaternion.LookRotation(-anchor.forward, Vector3.up) : root.rotation);

        var component = paeng.GetComponent<PaengBusPointer>()
                     ?? Undo.AddComponent<PaengBusPointer>(paeng.gameObject);

        var so = new SerializedObject(component);
        if (anchor != null) so.FindProperty("idleFaceTarget").objectReferenceValue = anchor;
        so.ApplyModifiedPropertiesWithoutUndo();

        paeng.gameObject.SetActive(false);   // Rule6 calls Appear() when the rule starts

        report.AppendLine("  - Paeng: standing in front of the pavilion, facing the bench while she " +
                          "waits. Drop her model in as a child and fill in either the Animator " +
                          "triggers or a Pointing Visual - without one of the two, the only clue in " +
                          "the rule is invisible");

        return component;
    }

    // ─────────────────────────── Wiring ───────────────────────────

    private static void WireRule(RuleSystem.Rule.Rule6 rule, Transform[] route, List<Rule6Bus> buses,
                                 PaengBusPointer paeng, Transform anchor, StringBuilder report)
    {
        var so = new SerializedObject(rule);

        so.FindProperty("approachPoint").objectReferenceValue = route[0];
        so.FindProperty("stopPoint").objectReferenceValue     = route[1];
        so.FindProperty("departPoint").objectReferenceValue   = route[2];
        so.FindProperty("paeng").objectReferenceValue         = paeng;
        if (anchor != null) so.FindProperty("pavilionSeat").objectReferenceValue = anchor;

        var busArray = new Object[buses.Count];
        for (int i = 0; i < buses.Count; i++) busArray[i] = buses[i];
        SetArray(so.FindProperty("buses"), busArray);

        // The scene holds exactly one of each of these, so there is nothing to choose between
        WireFirstInScene<InputHandler>(so, "inputHandler", report);
        WireFirstInScene<RuleManager>(so, "ruleManager", report);
        WireFirstInScene<Rule1>(so, "rule1", report);

        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine("  - Rule6 wired: route, 4 buses in order, Paeng, pavilion seat");
    }

    private static void WireFirstInScene<T>(SerializedObject so, string field, StringBuilder report)
        where T : Component
    {
        var prop = so.FindProperty(field);
        if (prop == null || prop.objectReferenceValue != null) return;

        var found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (found == null)
        {
            report.AppendLine($"  ! No {typeof(T).Name} in the scene - '{field}' left empty");
            return;
        }

        prop.objectReferenceValue = found;
    }

    private static void RegisterWithRuleManager(RuleSystem.Rule.Rule6 rule, StringBuilder report)
    {
        var manager = Object.FindAnyObjectByType<RuleManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            report.AppendLine("  ! No RuleManager in the scene - Rule 6 will never start on its own");
            return;
        }

        var so   = new SerializedObject(manager);
        var list = so.FindProperty("rules");

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == rule)
            {
                report.AppendLine("  - Rule 6 is already in RuleManager.rules");
                return;
            }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = rule;
        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine($"  - Added Rule 6 to RuleManager.rules (now {list.arraySize} rules). " +
                          "Note rule 1 is NOT in that list on purpose - it is triggered by reading " +
                          "the chat, not by the clock");
    }

    // ─────────────────────────── Rule book entry ───────────────────────────

    private static RuleEntryData EnsureRuleEntry(StringBuilder report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<RuleEntryData>(RuleEntryPath);
        if (existing != null)
        {
            report.AppendLine($"  - Reusing {RuleEntryPath}");
            return existing;
        }

        var entry = UnityEngine.ScriptableObject.CreateInstance<RuleEntryData>();
        entry.ruleNumber  = 6;
        entry.title       = "The Bus";
        entry.summary     = "A bus will stop in front of the pavilion. Read it before you stand up - " +
                            "you only get on one";
        entry.startHour   = 24;
        entry.startMinute = 0;
        entry.endHour     = 24;
        entry.endMinute   = 40;
        entry.factSlots   = 4;

        // Cooperative: the sender is the one handing out the facts, and this is the rule where it
        // finally lies. Every line is true and together they point straight at the trap.
        entry.factsIfCooperative = new[]
        {
            "Stay seated. A bus will come to you",
            "Get up to check a bus and it will wait as long as you like",
            "Sit back down and it moves on to the next one",
            "Take the one with people in it. An empty bus at this hour is not a bus",
        };

        // Defiant: it tells you nothing, which is the only honest thing it says all night
        entry.factsIfDefiant = new[]
        {
            "Get on a bus",
        };

        AssetDatabase.CreateAsset(entry, RuleEntryPath);

        report.AppendLine($"  - Created {RuleEntryPath}");
        report.AppendLine("  ! Read the cooperative facts before shipping: the last one is the " +
                          "sender's lie, and the whole rule depends on it sounding like help");

        return entry;
    }

    private static void RegisterWithRuleBook(RuleEntryData entry, StringBuilder report)
    {
        var book = Object.FindAnyObjectByType<RuleBookController>(FindObjectsInactive.Include);
        if (book == null)
        {
            report.AppendLine("  ! No RuleBookController in the scene - rule 6 will not appear in the " +
                              "phone's rule app");
            return;
        }

        var so   = new SerializedObject(book);
        var list = so.FindProperty("rules");

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == entry)
            {
                report.AppendLine("  - RuleEntry_6 is already in RuleBookController.rules");
                return;
            }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = entry;
        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine("  - Added RuleEntry_6 to the phone's rule app");
    }

    // ─────────────────────────── Dev skip ───────────────────────────

    private static void EnsureDevSkip(Transform root, RuleSystem.Rule.Rule6 rule, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<Rule6DevSkip>(FindObjectsInactive.Include);
        if (existing != null) return;

        var skip = Undo.AddComponent<Rule6DevSkip>(root.gameObject);

        var so = new SerializedObject(skip);
        so.FindProperty("rule6").objectReferenceValue = rule;
        so.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine("  - Added Rule6DevSkip: F11 enters the rule, Numpad 1-4 summons that bus, " +
                          "F12 skips to the next one");
    }

    /// <summary>
    /// The second-loop rule: once the player has taken bus 4, dying even once ends the run.
    ///
    /// Lives on the Rule 6 object because that is where the loop count comes from, but it is
    /// rules 1-5 that consult it - each of their game-over paths asks it before rewinding the
    /// clock. Without this component in the scene they all behave exactly as they did before,
    /// which is what makes it safe to add late.
    /// </summary>
    private static void EnsurePermanentDeath(Transform root, StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<PermanentDeath>(FindObjectsInactive.Include);
        if (existing != null)
        {
            report.AppendLine("  - PermanentDeath is already in the scene");
            return;
        }

        Undo.AddComponent<PermanentDeath>(root.gameObject);

        report.AppendLine("  - Added PermanentDeath: from the second night on, one death ends the run " +
                          "(rules 1-5 ask it before they rewind). Still needs its Death Screen");
    }

    // ─────────────────────────── Intro bus ───────────────────────────

    /// <summary>
    /// The bus the opening cutscene arrives on, and the CutsceneManager wiring for it.
    ///
    /// The opening and Rule 6's loop ending are the same scene: a bus pulls up in front of the
    /// pavilion at 18:40 and the player steps off it. That is the whole trick of the second
    /// night - it is not a new night that resembles the first one, it IS the first one again.
    /// So this bus runs the same route transforms as Rule 6's four, and both sides drive it
    /// through the same BusRide code. Move Route/Stop and the arrival moves in both scenes.
    ///
    /// A fifth object rather than reusing Bus 4: Rule6.StartRule calls Hide() on every bus in
    /// its list, and the opening needs its bus visible and occupied before any rule exists.
    /// It also wants an engine - bus 4's silence is a clue that would be spent here for nothing.
    /// </summary>
    private static void EnsureIntroBus(Transform root, Transform[] route, StringBuilder report)
    {
        Transform stop = route[1];

        Transform bus     = EnsureChild(root, IntroBusName, stop.position, stop.rotation);
        Transform seat    = EnsureChild(bus, "Seat", bus.TransformPoint(SeatOffset), bus.rotation);
        Transform sitPose = EnsureChild(bus, "Sit Pose",
            seat.position - Vector3.up * SeatCushionHeight, seat.rotation);
        Transform standPose = EnsureChild(bus, "Stand Pose",
            sitPose.position + bus.right * AisleStep, seat.rotation);
        Transform cabin = EnsureCabin(bus);

        Transform headLeft  = EnsureChild(bus, "Head Light L", bus.TransformPoint(HeadLightLeft),  bus.rotation);
        Transform headRight = EnsureChild(bus, "Head Light R", bus.TransformPoint(HeadLightRight), bus.rotation);

        Light headL = EnsureLight(headLeft,  LightType.Spot);
        Light headR = EnsureLight(headRight, LightType.Spot);
        headL.shadows = LightShadows.Soft;
        headR.shadows = LightShadows.Soft;

        var component = bus.GetComponent<Rule6Bus>() ?? Undo.AddComponent<Rule6Bus>(bus.gameObject);

        var so = new SerializedObject(component);
        so.FindProperty("seat").objectReferenceValue      = seat;
        so.FindProperty("sitPose").objectReferenceValue   = sitPose;
        so.FindProperty("standPose").objectReferenceValue = standPose;
        so.FindProperty("cabinBounds").objectReferenceValue = cabin;
        so.FindProperty("engineEvent").stringValue        = "event:/Rule6/BusEngine";
        so.FindProperty("flickerInterior").boolValue    = false;
        so.FindProperty("headLightsOff").boolValue      = false;
        SetArray(so.FindProperty("headLights"), new Object[] { headL, headR });
        so.ApplyModifiedPropertiesWithoutUndo();

        // CutsceneManager calls Appear() on it. Left off so it is not parked in front of the
        // pavilion in the Editor, where it would be the first thing anyone tries to delete.
        bus.gameObject.SetActive(false);

        report.AppendLine("  - Intro Bus: the bus the opening cutscene arrives on, on the same route " +
                          "as the rule's four. Its Kind is never read - only Rule 6 reads that");

        var cutscene = Object.FindAnyObjectByType<CutsceneManager>(FindObjectsInactive.Include);
        if (cutscene == null)
        {
            report.AppendLine("  ! No CutsceneManager in this scene - the Intro Bus is built but " +
                              "nothing drives it. Wire it by hand in the scene that has the opening");
            return;
        }

        var cso = new SerializedObject(cutscene);
        cso.FindProperty("introBus").objectReferenceValue       = component;
        cso.FindProperty("busStartPoint").objectReferenceValue  = route[0];
        cso.FindProperty("busStopPoint").objectReferenceValue   = route[1];
        cso.FindProperty("busDepartPoint").objectReferenceValue = route[2];
        WireFirstInScene<InputHandler>(cso, "inputHandler", report);
        cso.ApplyModifiedPropertiesWithoutUndo();

        report.AppendLine("  - CutsceneManager wired to the Intro Bus and to Approach / Stop / Depart");
    }

    // ─────────────────────────── Leftovers ───────────────────────────

    private static void ReportLeftovers(StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine("  Still to do by hand:");
        report.AppendLine("   1. Drag Route/Stop onto the road, door side to the kerb, then Approach " +
                          "and Depart out of sight");
        report.AppendLine("   2. Drop a bus model under each of the 4 bus objects, under Intro Bus, " +
                          "and a model under Paeng. The interior floor needs collision on all of " +
                          "them - the player walks aboard, and on the Intro Bus stands up inside " +
                          "a moving one");
        report.AppendLine("   3. Move each bus's Seat child onto the real seat in its model. On bus 1 that " +
                          "must be the ONE free seat. The yellow gizmo circle is the E range: too wide " +
                          "and the player can sit from outside the bus, too narrow and the hint never " +
                          "appears");
        report.AppendLine("   4. Move each bus's Sit Pose child until the blue gizmo's lower ball sits ON " +
                          "the bus floor, facing the way the player should face. That ball is where the " +
                          "character's FEET land - a pose left up on the cushion is what makes the " +
                          "player float while seated. Then move the Stand Pose child (green capsule) " +
                          "into the aisle until it clears the seat and the seat back - that capsule is " +
                          "the real collider the player stands up into, and one left on the seat leaves " +
                          "them stuck inside it. Bus 4 and the Intro Bus are the two that matter; they " +
                          "are the only buses the player stands up in. Those two also need their " +
                          "Cabin Bounds (orange box) scaled to the real passenger cabin - the bus " +
                          "only leaves once the player is outside that box");
        report.AppendLine("   5. Paeng needs an Animator trigger or a Pointing Visual - she is the only " +
                          "clue which bus is the way out");
        report.AppendLine("   6. Write the four Ending Object screens on Rule6 and the dialogue lines " +
                          "for each ending");
        report.AppendLine("   7. FMOD events: event:/Rule6/Background Rule 6, BusEngine (loop), " +
                          "BusArrive, BusDepart, Board, PhoneRing");
        report.AppendLine("   8. Fill the Death Screen on the PermanentDeath component - that is what " +
                          "the player sees when they die on a looped night, and it is the only thing " +
                          "that tells them the run is over rather than restarting");
        report.AppendLine("   9. Schedule the sender's rule-6 chat on FriendListController. Ton_Rule6 " +
                          "is already set for 23:40; the sender's own message pushing the full bus " +
                          "does not exist yet. Note a LOOPED night sends nothing at all - the old " +
                          "chats and rule data stay in the phone and Rule 6 starts rule 1 directly");
        report.AppendLine("  10. The opening cutscene now rides the Intro Bus in instead of walking " +
                          "the player to the bench, so its old Shots data no longer lines up: each " +
                          "shot is timed in SECONDS from the moment the bus starts moving (the old " +
                          "field counted waypoints, which are gone). Re-time the shots against " +
                          "Arrive Seconds, and move Sit Pose on the Intro Bus the same way as the " +
                          "other four. The passing-car fields are gone from Shots as well");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static Transform EnsureChild(Transform parent, string name)
        => EnsureChild(parent, name, parent.position, parent.rotation);

    /// <summary>
    /// Finds the child by name, or creates it. Position and rotation are only applied to a child
    /// that is being created, so re-running the tool never undoes hand placement.
    /// </summary>
    private static Transform EnsureChild(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        Transform found = parent.Find(name);
        if (found != null) return found;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Rule 6");

        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);

        return go.transform;
    }

    /// <summary>
    /// The passenger cabin as a box: its SCALE is its size in metres, so it is resized with the
    /// Scale tool rather than by typing numbers. No collider - it only answers "is the player
    /// still aboard", and a trigger on a moving bus would fire every zone script it touches.
    ///
    /// Size is only applied when the box is created, like every other holder here, so
    /// re-running the tool never undoes a box fitted to the real model.
    /// </summary>
    private static Transform EnsureCabin(Transform bus)
    {
        Transform cabin = bus.Find("Cabin Bounds");
        if (cabin == null)
        {
            cabin = EnsureChild(bus, "Cabin Bounds",
                bus.TransformPoint(new Vector3(0f, CabinHeight * 0.5f, 0f)), bus.rotation);
            cabin.localScale = new Vector3(BusWidth, CabinHeight, BusLength);
        }

        // Outside the "already exists" branch on purpose: boxes built before this component
        // existed are empty objects with nothing to see, and they need it just as much
        if (cabin.GetComponent<BusCabinBounds>() == null)
            Undo.AddComponent<BusCabinBounds>(cabin.gameObject);

        return cabin;
    }

    private static Light EnsureLight(Transform owner, LightType type)
    {
        var light = owner.GetComponent<Light>();
        if (light != null) return light;

        light = Undo.AddComponent<Light>(owner.gameObject);
        light.type  = type;
        light.range = type == LightType.Spot ? 30f : 6f;
        if (type == LightType.Spot) light.spotAngle = 45f;

        return light;
    }

    private static void SetArray(SerializedProperty array, Object[] values)
    {
        if (array == null) return;

        // Only fill an array that is still empty - a bus that has already been dressed by hand has
        // more entries than this tool knows about, and clobbering them is worse than doing nothing
        if (array.arraySize > 0) return;

        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
