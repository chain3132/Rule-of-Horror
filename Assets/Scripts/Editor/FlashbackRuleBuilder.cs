using System.Collections.Generic;
using System.Linq;
using Flashbacks;
using RuleSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Rule of Horror ▸ Flashback ▸ Create Rule Flashbacks (After Rule 1–5)
///
/// Builds one flashback per rule (plays while the eyes are closed after the rule is passed),
/// using the Shots list on <see cref="Flashback"/> instead of a Timeline:
///   - Stage with Thep (Inw prefab), Paeng (ePangSuay model) and a Flashback Camera
///   - one shot per subtitle line, with the clips that exist today already assigned
///   - a point for every camera / actor position, laid out around the pavilion bench,
///     to be moved in the Scene view
///
/// Clips that are not made yet use a stand-in clip; the shot label says which clip goes there.
/// Safe to re-run: flashbacks that already exist (by name) are skipped. Save the scene afterwards.
/// </summary>
internal static class FlashbackRuleBuilder
{
    private const string ThepPrefab = "Assets/Models_Jedi/Animations/NPCs/Inw/Prefabs/Inw.prefab";
    private const string PaengModel = "Assets/Models_Jedi/Animations/Ghost/ePang/Models/ePangSuay.fbx";

    // Extra Paeng clips (headache / run) live here. Same Blender rig (metarig.003) as ePangSuay, but it
    // imports as Generic - Generic clips cannot drive the Humanoid ePangSuay actor, so PreparePaengClips
    // switches it to Humanoid with ePangSuay's avatar (identical bone mapping, no retarget surprises)
    private const string PaengClipsModel = "Assets/Pang/Pang.fbx";

    private const string Thep  = "Thep";
    private const string Paeng = "Paeng";

    // ─────────────────────────── story ───────────────────────────

    private class Act
    {
        public string who;
        public string clip;          // null = keep the previous shot's clip
        public float  speed = 1f;
        public bool   loop  = true;
        public float  move;          // metres to walk/run forward during the shot (0 = stand)
        public bool   hide;
        public string todo;          // the clip that should replace the stand-in
        public string was;           // stand-in used by older builds - Update Clips swaps it for clip
    }

    private class Shot
    {
        public string line;
        public Act[]  acts;
    }

    private static Act A(string who, string clip, float move = 0f, float speed = 1f, string todo = null,
                         bool loop = true, string was = null) =>
        new Act { who = who, clip = clip, move = move, speed = speed, todo = todo, loop = loop, was = was };

    private static Act Hide(string who) => new Act { who = who, hide = true };

    private static Shot S(string line, params Act[] acts) => new Shot { line = line, acts = acts };

    private static readonly Dictionary<int, Shot[]> Story = new Dictionary<int, Shot[]>
    {
        [1] = new[]
        {
            S("ฉันกับเขาลองเดินออกไปแล้ว แต่ไปได้ไม่ไกล",
              A(Thep, "Inw_Walk", move: 5f), A(Paeng, "ePang_Walk", move: 5f)),
            S("ฉันเวียนหัวมาก เหมือนหัวจะระเบิด",
              A(Thep, "Inw_WalkHeadache", move: 1.5f),
              A(Paeng, "ePang_HeadAche_2H", was: "ePang_LookOutIdle")),
            S("เราเลยกลับมารอที่ศาลา แล้วข้อความแปลกๆ ก็ส่งมาอีก",
              A(Thep, "Inw_WalkHeadache", move: -6f),
              A(Paeng, "ePang_WalkHeadAche", move: -6f, was: "ePang_Walk")),
            S("ถ้าคุณอ่านอยู่ ไม่ต้องลองนะ ที่นี่ไม่มีทางออก",
              Hide(Thep), Hide(Paeng)),
        },
        [2] = new[]
        {
            S("ไฟดับ เขาไปซ่อมตู้ไฟให้ แล้วบอกว่า \"ไม่ต้องกลัว เรื่องง่ายๆ\"",
              A(Thep, "Inw_FixElectricBox"), A(Paeng, "ePang_LookOutIdle")),
            S("ฉันว่าเขาเป็นคนดีนะ",
              A(Paeng, "ePang_Nod")),
            S("คืนนี้คงไม่แย่เท่าไหร่ ถ้ามีเพื่อนอยู่ด้วย",
              A(Paeng, "ePang_IdleSit")),
        },
        [3] = new[]
        {
            S("กฎข้อนี้เหนื่อยกว่าที่คิด เราต้องช่วยกันหาตัวเลขให้ครบ",
              A(Thep, "Inw_SleepySit"), A(Paeng, "ePang_IdleSit")),
            S("เขาเกือบหลับไปหลายครั้ง ฉันเลยต้องคอยปลุกเขาตลอด"),
            S("\"อย่าหลับนะ\" ฉันพูดกับเขาไม่รู้กี่ครั้ง",
              A(Paeng, "ePang_LookOut", loop: false)),
            S("เขาบอกว่าถ้าไม่ได้ฉันช่วยไว้ เขาคงไม่รอด",
              A(Paeng, "ePang_Nod")),
            S("ดีจัง อย่างน้อยฉันก็ช่วยใครสักคนได้",
              A(Paeng, "ePang_IdleSit")),
        },
        [4] = new[]
        {
            S("วันนี้เขาดูแปลกไป",
              A(Thep, "Inw_Walk", speed: 0f, todo: "Thep standing idle"),
              A(Paeng, "ePang_LookOutIdle")),
            S("ระหว่างที่เราช่วยกันหาตุ๊กตา เขาบอกให้ฉันวิ่งตามหลัง แล้วเขาจะคอยดูทางให้",
              A(Thep, "Inw_Walk", move: 8f, speed: 1.6f, todo: "Thep run"),
              A(Paeng, "ePang_RunWave", move: 6f, was: "ePang_Walk")),
            S("แต่เขากลับวิ่งเร็วขึ้นเรื่อย ๆ",
              A(Thep, null, move: 10f), A(Paeng, null, move: 6f)),
            S("ฉันพยายามเรียกเขา \"รอด้วย!\" แต่เขาไม่เคยหันกลับมาเลย",
              A(Thep, null, move: 10f), A(Paeng, null, move: 5f)),
            S("ฉันไม่รู้ว่าเขาไม่ได้ยิน หรือว่าเขาไม่อยากรอฉันกันแน่",
              Hide(Thep), A(Paeng, "ePang_LookOutIdle")),
            S("หวังว่าฉันคงคิดมากไปเอง"),
        },
        [5] = new[]
        {
            S("เขาเอาแต่ดูโทรศัพท์ตั้งแต่เราผ่านกฎข้อที่แล้ว",
              A(Thep, "Inw_Walk", speed: 0f, todo: "Thep standing, holding phone in one hand"),
              A(Paeng, "ePang_LookOutIdle")),
            S("ฉันถามว่าใครส่งข้อความมา เขาก็บอกว่าไม่มีอะไร",
              A(Paeng, "ePang_LookOut", loop: false)),
            S("ตอนนี้เขาบอกให้ฉันเดินนำ ส่วนเขาจะคอยระวังด้านหลังให้",
              A(Paeng, "ePang_Walk", move: 4f), A(Thep, "Inw_Walk", move: 4f)),
            S("เขาย้ำว่า \"ไม่ว่าจะได้ยินอะไร อย่าหันไปข้างหลัง\"",
              A(Paeng, null, move: 4f), A(Thep, null, move: 4f)),
            S("ฉันไม่รู้ทำไม แต่ฉันเริ่มกลัวเขามากกว่าสิ่งที่อยู่ในศาลานี้แล้ว",
              A(Paeng, null, move: 4f), A(Thep, null, move: 3f)),
            S("อย่างน้อยฉันก็หวังว่าเขาจะไม่ทิ้งฉันไปจริง ๆ",
              A(Paeng, "ePang_LookOutIdle"), Hide(Thep)),
        },
    };

    // ─────────────────────────── build ───────────────────────────

    [MenuItem("Rule of Horror/Flashback/Create Rule Flashbacks (After Rule 1-5)")]
    private static void Build()
    {
        PreparePaengClips();

        var rules = Object.FindObjectsByType<RuleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var seat  = Object.FindAnyObjectByType<SittingPoint>(FindObjectsInactive.Include);
        Vector3 anchor = seat != null ? seat.transform.position
                       : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot
                       : Vector3.zero;

        var thepPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThepPrefab);
        var paengModel = AssetDatabase.LoadAssetAtPath<GameObject>(PaengModel);
        if (thepPrefab == null) Debug.LogWarning($"[Flashback] {ThepPrefab} not found - Thep will be missing");
        if (paengModel == null) Debug.LogWarning($"[Flashback] {PaengModel} not found - Paeng will be missing");

        var created = new List<GameObject>();
        var missingClips = new HashSet<string>();

        foreach (var pair in Story)
        {
            string name = $"Flashback - After Rule {pair.Key}";
            if (GameObject.Find(name) != null || FindInactive(name) != null)
            {
                Debug.Log($"[Flashback] '{name}' already exists - skipped");
                continue;
            }

            var rule = rules.FirstOrDefault(r => r.GetType().Name == $"Rule{pair.Key}");
            if (rule == null) Debug.LogWarning($"[Flashback] Rule{pair.Key} not found in the scene - set Rule by hand on '{name}'");

            var root = BuildOne(name, rule, pair.Value, anchor, thepPrefab, paengModel, missingClips);
            created.Add(root);
        }

        if (created.Count == 0) return;

        EditorSceneManager.MarkSceneDirty(created[0].scene);
        Selection.objects = created.Cast<Object>().ToArray();

        foreach (var clip in missingClips)
            Debug.LogWarning($"[Flashback] Clip '{clip}' not found - that slot is empty");

        Debug.Log($"[Flashback] Created {created.Count} rule flashbacks around the pavilion. Next: move the " +
                  "points under each Stage/Points (Scene view), replace the stand-in clips noted in the shot " +
                  "labels, then test with right-click Flashback > Test Play in Play mode. Save the scene.");
    }

    private static GameObject BuildOne(string name, RuleBase rule, Shot[] story, Vector3 anchor,
                                       GameObject thepPrefab, GameObject paengModel, HashSet<string> missingClips)
    {
        var root = new GameObject(name, typeof(Flashback));
        Undo.RegisterCreatedObjectUndo(root, "Create Rule Flashbacks");
        root.transform.position = anchor;

        var stage = new GameObject("Stage");
        stage.transform.SetParent(root.transform, false);

        var cam = CreateCamera(stage.transform);

        var points = new GameObject("Points").transform;
        points.SetParent(stage.transform, false);

        // Actors - only the ones this story uses
        var actors  = new Dictionary<string, Animator>();
        var cursor  = new Dictionary<string, Vector3>();
        bool usesThep  = story.Any(s => s.acts != null && s.acts.Any(a => a.who == Thep));
        bool usesPaeng = story.Any(s => s.acts != null && s.acts.Any(a => a.who == Paeng));
        if (usesThep  && thepPrefab != null) actors[Thep]  = CreateActor(thepPrefab, "Thep (เทพ)", stage.transform);
        if (usesPaeng && paengModel != null) actors[Paeng] = CreateActor(paengModel, "Paeng (แป้ง)", stage.transform);
        cursor[Thep]  = new Vector3(0.7f, 0f, 0f);
        cursor[Paeng] = new Vector3(-0.7f, 0f, 0f);

        var shots = new List<Flashback.FlashbackShot>();
        var placed = new HashSet<string>();

        for (int i = 0; i < story.Length; i++)
        {
            var spec  = story[i];
            int n     = i + 1;

            // Camera looks at the middle of whoever is on screen at the start of the shot
            Vector3 focus = Vector3.zero;
            int count = 0;
            foreach (var who in actors.Keys) { focus += cursor[who]; count++; }
            if (count > 0) focus /= count;

            var camPoint = CreatePoint(points, $"Shot {n} Camera", focus + new Vector3(0f, 1.6f, -4.5f), false);
            camPoint.LookAt(root.transform.TransformPoint(focus + new Vector3(0f, 1.2f, 0f)));

            var acts = new List<Flashback.ShotActor>();
            if (spec.acts != null)
            {
                foreach (var act in spec.acts)
                {
                    if (!actors.TryGetValue(act.who, out var animator)) continue;

                    if (act.hide)
                    {
                        acts.Add(new Flashback.ShotActor { actor = animator, hide = true });
                        continue;
                    }

                    var entry = new Flashback.ShotActor { actor = animator, loop = act.loop, speed = act.speed };

                    if (!placed.Contains(act.who) || act.clip != null)
                    {
                        entry.from = CreatePoint(points, $"Shot {n} {act.who} From", cursor[act.who], true);
                        placed.Add(act.who);
                    }

                    if (Mathf.Abs(act.move) > 0.01f)
                    {
                        cursor[act.who] += new Vector3(0f, 0f, act.move);
                        entry.to = CreatePoint(points, $"Shot {n} {act.who} To", cursor[act.who], true);
                    }

                    if (act.clip != null)
                    {
                        entry.clip = FindClip(act.clip);
                        if (entry.clip == null) missingClips.Add(act.clip);
                    }

                    acts.Add(entry);
                }
            }

            shots.Add(new Flashback.FlashbackShot
            {
                label      = ShotLabel(n, spec),
                duration   = ShotLength(spec.line),
                cameraFrom = camPoint,
                actors     = acts.ToArray(),
                lines      = string.IsNullOrEmpty(spec.line)
                    ? new Flashback.FlashbackLine[0]
                    : new[] { new Flashback.FlashbackLine { at = 0.4f, text = spec.line, holdDuration = -1f } },
            });
        }

        root.GetComponent<Flashback>().EditorSetup(FlashbackTrigger.AfterRuleCompleted, rule, shots.ToArray());

        // Hidden until it plays (Flashback.Awake does this too, but keep the scene view clean)
        stage.SetActive(false);
        cam.enabled = false;
        return root;
    }

    // ─────────────────────────── new clips ───────────────────────────

    /// <summary>
    /// Swap the stand-in clips in flashbacks that were built before the real clips existed.
    /// Only touches an actor slot that still holds the old stand-in - clips changed by hand are kept,
    /// and points / durations / subtitles are never touched.
    /// </summary>
    [MenuItem("Rule of Horror/Flashback/Update Clips in Rule Flashbacks")]
    private static void UpdateClips()
    {
        PreparePaengClips();

        int swapped = 0;
        foreach (var flashback in Object.FindObjectsByType<Flashback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            int rule = Story.Keys.FirstOrDefault(k => flashback.name == $"Flashback - After Rule {k}");
            if (rule == 0) continue;

            var shots = flashback.EditorShots;
            if (shots == null) continue;

            Undo.RecordObject(flashback, "Update Flashback Clips");
            var story = Story[rule];

            for (int i = 0; i < shots.Length && i < story.Length; i++)
            {
                var shot = shots[i];
                if (shot == null) continue;

                foreach (var act in story[i].acts ?? new Act[0])
                {
                    if (act.was == null || act.clip == null || shot.actors == null) continue;

                    foreach (var entry in shot.actors)
                    {
                        if (entry?.actor == null || entry.clip == null || entry.clip.name != act.was) continue;
                        if (!NameOfActorRoot(entry.actor).StartsWith(act.who)) continue;

                        var clip = FindClip(act.clip);
                        if (clip == null) { Debug.LogWarning($"[Flashback] Clip '{act.clip}' not found"); continue; }

                        entry.clip  = clip;
                        entry.speed = act.speed;
                        entry.loop  = act.loop;
                        swapped++;
                    }
                }

                if (shot.label != null && shot.label.StartsWith($"Shot {i + 1}"))
                    shot.label = ShotLabel(i + 1, story[i]);
            }

            EditorUtility.SetDirty(flashback);
        }

        if (swapped > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[Flashback] Swapped {swapped} stand-in clip(s) for the real ones - save the scene.");
    }

    /// <summary>
    /// Pang.fbx → Humanoid, copying ePangSuay's avatar. Same skeleton, so the mapping is identical
    /// to the clips ePangSuay already plays. Does nothing once it is already set.
    /// </summary>
    [MenuItem("Rule of Horror/Flashback/Make Pang.fbx Clips Humanoid")]
    private static void PreparePaengClips()
    {
        var importer = AssetImporter.GetAtPath(PaengClipsModel) as ModelImporter;
        if (importer == null) return;

        var avatar = AssetDatabase.LoadAllAssetsAtPath(PaengModel).OfType<Avatar>().FirstOrDefault();
        if (avatar == null)
        {
            Debug.LogWarning($"[Flashback] No avatar in {PaengModel} - {PaengClipsModel} stays Generic " +
                             "and its clips will not move Paeng in flashbacks");
            return;
        }

        if (importer.animationType == ModelImporterAnimationType.Human &&
            importer.avatarSetup   == ModelImporterAvatarSetup.CopyFromOther &&
            importer.sourceAvatar  == avatar) return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar  = avatar;
        importer.SaveAndReimport();
        Debug.Log($"[Flashback] {PaengClipsModel} is now Humanoid (avatar copied from ePangSuay) so its " +
                  "clips can play on Paeng");
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static string NameOfActorRoot(Animator animator)
    {
        // The actor is the prefab instance directly under Stage - its name is "Thep (เทพ)" / "Paeng (แป้ง)"
        var t = animator.transform;
        while (t.parent != null && t.parent.name != "Stage") t = t.parent;
        return t.name;
    }

    private static string ShotLabel(int n, Shot spec)
    {
        var todos = (spec.acts ?? new Act[0])
            .Where(a => !string.IsNullOrEmpty(a.todo))
            .Select(a => $"{a.who}: needs '{a.todo}'")
            .ToList();
        return todos.Count == 0 ? $"Shot {n}" : $"Shot {n} - stand-in clip ({string.Join("; ", todos)})";
    }

    /// <summary>
    /// Long enough for PlayerDialogueUI to type the line and hold it (≈0.12 s per character) plus a beat
    /// </summary>
    private static float ShotLength(string line) =>
        string.IsNullOrEmpty(line) ? 4f : Mathf.Round(Mathf.Max(4f, line.Length * 0.12f + 2f) * 2f) / 2f;

    private static Camera CreateCamera(Transform stage)
    {
        var camGo = new GameObject("Flashback Camera", typeof(Camera));
        camGo.transform.SetParent(stage, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, -4.5f);
        camGo.tag = "Untagged";

        var cam = camGo.GetComponent<Camera>();
        cam.fieldOfView   = 45f;
        cam.nearClipPlane = 0.05f;

        var urp = cam.GetUniversalAdditionalCameraData();
        if (urp != null) urp.renderPostProcessing = false;
        return cam;
    }

    private static Animator CreateActor(GameObject source, string name, Transform stage)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(source, stage);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var animator = go.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;   // the shots drive the clips directly
        animator.applyRootMotion = false;
        return animator;
    }

    /// <summary>A point relative to the flashback root; actor points are dropped onto the ground</summary>
    private static Transform CreatePoint(Transform parent, string name, Vector3 local, bool snapToGround)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;

        if (snapToGround)
        {
            Vector3 world = go.transform.position;
            if (Physics.Raycast(world + Vector3.up * 3f, Vector3.down, out var hit, 10f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                go.transform.position = new Vector3(world.x, hit.point.y, world.z);
        }
        return go.transform;
    }

    /// <summary>
    /// Finds a clip by name. Blender exports name takes "armature|action" (Pang.fbx:
    /// "metarig.003|ePang_RunWave"), so a name that ends in "|clipName" counts too
    /// </summary>
    private static AnimationClip FindClip(string clipName)
    {
        bool Matches(AnimationClip c) =>
            !c.name.StartsWith("__preview__") && (c.name == clipName || c.name.EndsWith("|" + clipName));

        // The search index may not tokenise "metarig.003|ePang_RunWave" - look in Pang.fbx directly first
        var direct = AssetDatabase.LoadAllAssetsAtPath(PaengClipsModel).OfType<AnimationClip>().FirstOrDefault(Matches);
        if (direct != null) return direct;

        AnimationClip fallback = null;
        foreach (var guid in AssetDatabase.FindAssets($"{clipName} t:AnimationClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip) || !Matches(clip)) continue;
                if (path.EndsWith(".anim")) return clip;   // prefer the extracted clip (Foot IK fixed etc.)
                fallback = clip;
            }
        }
        return fallback;
    }

    private static GameObject FindInactive(string name)
    {
        foreach (var t in Object.FindObjectsByType<Flashback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t.gameObject;
        return null;
    }
}
