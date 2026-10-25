using System.Collections.Generic;
using System.IO;
using System.Text;
using Enum;
using Rule6;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Step   = Rule6.EndingCutscene.Step;
using Action = Rule6.EndingCutscene.StepAction;

/// <summary>
/// Builds the in-game ending cutscene for bus 3 (Death) from the storyboard, in the open scene.
///
///   SHOT 1  we walk in - lights flicker, blood (the bus does this already) - door slams
///   SHOT 2  we sit; one by one the shadow passengers turn to look at us
///   SHOT 3  headlights of another vehicle rush at us from ahead, brighter and brighter - long horn
///   SHOT 4  blinding white, the camera shakes hard and tilts - screech, brakes
///   SHOT 5  cut to black, white cracks across the screen - CRASH, then silence
///
/// What it creates:
///   - "Ending Cutscene 3 Death" under the Rule 6 root, wired into Rule6.deathCutscene
///   - under Bus 3 ▸ Cutscene Markers: "Shadow 1..5" (where the shadow passengers sit - put the
///     models inside them; the cutscene turns these), the "Oncoming Headlights" spot light and
///     the two points it travels between
///   - "Crash Cracks": a full-screen overlay using a generated crack texture
///     (Assets/Texture/Rule6/CrashCracks.png - replace the PNG with drawn art any time)
///
/// The shadow models and their turn animation do not exist yet: the cutscene turns the whole
/// "Shadow N" object to face us, which works with any placeholder put inside it.
/// </summary>
public static class Rule6DeathCutsceneBuilder
{
    private const string CutsceneName = "Ending Cutscene 3 Death";
    private const string CracksPath   = "Assets/Texture/Rule6/CrashCracks.png";
    private const int    ShadowCount  = 5;
    private const float  GlareFullIntensity = 60f;   // LightRamp target = HeadlightGlare.fullIntensity

    [MenuItem("Rule of Horror/Rule 6/Build Bus 3 Ending Cutscene", false, 22)]
    private static void Build()
    {
        var report = new StringBuilder("[Rule 6] Bus 3 ending cutscene");
        report.AppendLine();

        var rule = Object.FindAnyObjectByType<RuleSystem.Rule.Rule6>(FindObjectsInactive.Include);
        if (rule == null)
        {
            EditorUtility.DisplayDialog("Bus 3 Ending Cutscene",
                "No Rule6 in the open scene. Run Rule of Horror ▸ Rule 6 ▸ Build Bus Stop first.", "OK");
            return;
        }

        var ruleSo = new SerializedObject(rule);
        var bus    = FindBus(ruleSo, Rule6BusKind.Death);
        if (bus == null)
        {
            EditorUtility.DisplayDialog("Bus 3 Ending Cutscene", "No bus with Kind = Death in Rule6.buses.", "OK");
            return;
        }
        if (bus.Seat == null)
            report.AppendLine("  ! Bus 3 has no Seat / Sit Pose yet - the player cannot board it. Set the seat first");

        Transform root = rule.transform.Find(CutsceneName);
        if (root == null)
        {
            var go = new GameObject(CutsceneName);
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
            go.transform.SetParent(rule.transform, false);
            root = go.transform;
            report.AppendLine($"  + Created '{CutsceneName}'");
        }

        var cutscene = root.GetComponent<EndingCutscene>();
        if (cutscene == null) cutscene = Undo.AddComponent<EndingCutscene>(root.gameObject);
        if (cutscene.EditorCinematicCamera == null)
            cutscene.EditorCinematicCamera = Rule6EndingCutsceneBuilder.FindCinematicCamera();

        Transform group   = EnsureGroup(bus.transform);
        var shadows       = EnsureShadows(group, bus, report);
        var lights        = EnsureHeadlights(group, bus.transform, DriveDirection(ruleSo, bus.transform), report);
        var cracks        = EnsureCracks(root, report);

        var steps = cutscene.EditorSteps;
        if (steps.Count == 0)
        {
            FillSteps(steps, bus.transform, shadows, lights, cracks);
            report.AppendLine($"  + Filled {steps.Count} steps from the storyboard");
        }
        else if (EditorUtility.DisplayDialog("Bus 3 Ending Cutscene",
                     $"'{CutsceneName}' already has {steps.Count} steps.\n\n" +
                     "Replace them with the current storyboard steps? FMOD event paths already typed " +
                     "into the Sound steps are carried over in order.",
                     "Replace", "Keep mine"))
        {
            Undo.RecordObject(cutscene, "Rebuild Bus 3 Ending Steps");

            var sounds = new List<string>();
            foreach (var s in steps)
                if (s != null && s.action == Action.Sound) sounds.Add(s.fmodEvent);

            steps.Clear();
            FillSteps(steps, bus.transform, shadows, lights, cracks);

            int next = 0;
            foreach (var s in steps)
                if (s.action == Action.Sound && next < sounds.Count) s.fmodEvent = sounds[next++];

            report.AppendLine($"  + Replaced the steps ({steps.Count}), kept {sounds.Count} FMOD path(s)");
        }
        else
        {
            report.AppendLine("  - Steps already exist - left untouched");
        }
        EditorUtility.SetDirty(cutscene);

        var slot = ruleSo.FindProperty("deathCutscene");
        if (slot.objectReferenceValue == null)
        {
            slot.objectReferenceValue = cutscene;
            ruleSo.ApplyModifiedProperties();
            report.AppendLine("  + Rule6.deathCutscene wired");
        }

        report.AppendLine();
        report.AppendLine("Still to do by hand:");
        report.AppendLine("  - Drag Bus 3 ▸ Cutscene Markers ▸ Shadow 1-5 onto the seats, arrow pointing the way they sit");
        report.AppendLine("    then put each shadow model inside its Shadow object (the cutscene turns the Shadow object)");
        report.AppendLine("  - 'Headlights Far' / 'Headlights Near' sit on the road ahead of the bus - check they are in front of the windscreen");
        report.AppendLine("  - Fill the FMOD event paths on the Sound steps (door, creaks, horn, screech, crash)");
        report.AppendLine("  - Put something in Rule6 ▸ Death Ending Object (credits / back to menu)");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;

        Debug.Log(report.ToString(), root.gameObject);
    }

    // ─────────────────────────── Steps ───────────────────────────

    private struct Headlights
    {
        public Light light;
        public Transform far, near;
    }

    private static void FillSteps(List<Step> steps, Transform bus, Transform[] shadows, Headlights h, GameObject cracks)
    {
        var white = Color.white;

        steps.Add(new Step { note = "SHOT 1 · door slams shut (put the FMOD event)", action = Action.Sound, soundAt = bus });
        steps.Add(new Step { note = "SHOT 1", action = Action.Wait, seconds = 0.4f });

        steps.Add(new Step { note = "SHOT 2 · look down the aisle", action = Action.PlayerLook, lookTarget = null, seconds = 0.8f });
        steps.Add(new Step { note = "SHOT 2 · creaks, many at once (put the FMOD event)", action = Action.Sound, soundAt = bus });
        steps.Add(new Step { note = "SHOT 2 · the shadow passengers turn to look at us, one by one", action = Action.TurnToward,
                             turners = shadows, seconds = 1.6f, stagger = 0.35f, yawOnly = true });
        steps.Add(new Step { note = "SHOT 2", action = Action.Wait, seconds = 1.2f });

        // The drive must outlast the whole crash: if it reached Depart first the bus would stop dead
        // on the road. Crash lands ~7 s in, ~60% of the way along the eased path.
        steps.Add(new Step { note = "SHOT 3 · the bus pulls out", action = Action.BusDrive, seconds = 9f, waitToFinish = false });
        steps.Add(new Step { note = "SHOT 3 · a moment of driving before anything shows", action = Action.Wait, seconds = 1.5f });
        steps.Add(new Step { note = "SHOT 3 · long horn (put the FMOD event)", action = Action.Sound, soundAt = h.near });
        steps.Add(new Step { note = "SHOT 3 · headlights rush at us from ahead", action = Action.MoveTo,
                             mover = h.light != null ? h.light.transform : null, moveTo = h.near, seconds = 3.5f, waitToFinish = false });
        // HeadlightGlare reads this light's intensity, so this one ramp drives the lamps on screen
        // and the white wash too - no separate Fade here, or the white arrives before the lamps.
        steps.Add(new Step { note = "SHOT 3 · ...brighter and brighter (lamps + glare follow this light)", action = Action.LightRamp,
                             light = h.light, lightIntensity = GlareFullIntensity, seconds = 3.5f });

        steps.Add(new Step { note = "SHOT 4 · screech + brakes (put the FMOD event)", action = Action.Sound });
        steps.Add(new Step { note = "SHOT 4 · the camera shakes hard and tilts", action = Action.Shake,
                             seconds = 2.2f, shakeAmplitude = 7f, shakeTilt = 14f, shakeHoldAtEnd = true, waitToFinish = false });
        // Not fully white yet - the shake has to stay visible through it
        steps.Add(new Step { note = "SHOT 4 · very bright, shake still visible", action = Action.Fade,
                             fade = EndingCutscene.FadeDirection.ToBlack, fadeColor = white, fadeAlpha = 0.5f, seconds = 0.3f });
        steps.Add(new Step { note = "SHOT 4", action = Action.Wait, seconds = 1.4f });
        steps.Add(new Step { note = "SHOT 4 · blinding white just before the impact", action = Action.Fade,
                             fade = EndingCutscene.FadeDirection.ToBlack, fadeColor = white, fadeAlpha = 0.95f, seconds = 0.4f });

        steps.Add(new Step { note = "SHOT 5 · cut to black", action = Action.Fade,
                             fade = EndingCutscene.FadeDirection.ToBlack, fadeColor = Color.black, fadeAlpha = 1f, seconds = 0f });
        steps.Add(new Step { note = "SHOT 5 · headlights (and their glare) off behind the black", action = Action.SetActive,
                             target = h.light != null ? h.light.gameObject : null, active = false });
        steps.Add(new Step { note = "SHOT 5 · cracks across the screen", action = Action.SetActive, target = cracks, active = true });
        steps.Add(new Step { note = "SHOT 5 · CRASH, then silence (put the FMOD event)", action = Action.Sound });
        steps.Add(new Step { note = "SHOT 5", action = Action.Wait, seconds = 3f });
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

    private static Transform EnsureGroup(Transform bus)
    {
        var group = bus.Find("Cutscene Markers");
        if (group != null) return group;

        var go = new GameObject("Cutscene Markers");
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
        go.transform.SetParent(bus, false);
        return go.transform;
    }

    /// <summary>
    /// Seats ahead of ours, alternating sides of the aisle, facing forward - so turning to look
    /// at us means turning round. Guessed from the player's seat; they need dragging onto seats.
    /// </summary>
    private static Transform[] EnsureShadows(Transform group, Rule6Bus bus, StringBuilder report)
    {
        Transform busT  = bus.transform;
        Transform sit   = bus.SitPose;
        Vector3   local = sit != null ? busT.InverseTransformPoint(sit.position) : new Vector3(0.6f, 0.5f, 0f);

        var result = new Transform[ShadowCount];
        for (int i = 0; i < ShadowCount; i++)
        {
            string name = $"Shadow {i + 1}";
            var existing = group.Find(name);
            if (existing != null) { result[i] = existing; continue; }

            float x = (i % 2 == 0 ? 1f : -1f) * Mathf.Abs(local.x);
            float z = local.z + 1.1f * (i / 2 + 1);

            var go = new GameObject(name, typeof(CutsceneMarker));
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(busT.TransformPoint(new Vector3(x, local.y, z)),
                                                Quaternion.LookRotation(busT.forward, Vector3.up));

            var so = new SerializedObject(go.GetComponent<CutsceneMarker>());
            so.FindProperty("height").floatValue = 1.3f;   // seated
            so.FindProperty("color").colorValue  = new Color(0.3f, 0.3f, 0.35f, 0.95f);
            so.ApplyModifiedPropertiesWithoutUndo();

            result[i] = go.transform;
        }

        report.AppendLine($"  + Shadow 1-{ShadowCount} under Bus 3 ▸ Cutscene Markers (guessed seats)");
        return result;
    }

    /// <summary>
    /// A spot light on the road ahead, pointing back through the windscreen. It is a child of the
    /// bus, so "ahead" stays ahead while the bus drives; the cutscene slides it from Far to Near
    /// while ramping it up, which reads as a vehicle closing in head-on.
    /// </summary>
    private static Headlights EnsureHeadlights(Transform group, Transform bus, Vector3 ahead, StringBuilder report)
    {
        Vector3 front = bus.position + ahead * 5f + Vector3.up * 1.3f;

        var h = new Headlights
        {
            far  = Point(group, "Headlights Far",  front + ahead * 40f, -ahead),
            near = Point(group, "Headlights Near", front + ahead * 3f,  -ahead),
        };

        // The first version placed these along bus.forward, which is not always the way the bus
        // drives - then the lamps came from behind and were never seen
        bool moved = false;
        moved |= KeepAhead(h.far,  bus, ahead, front + ahead * 40f);
        moved |= KeepAhead(h.near, bus, ahead, front + ahead * 3f);
        if (moved)
            report.AppendLine("  ! Headlights Far / Near were behind the bus (opposite the drive direction) - moved ahead of it");

        Light light;
        var existing = group.Find("Oncoming Headlights");
        if (existing != null)
        {
            light = existing.GetComponent<Light>();
        }
        else
        {
            var go = new GameObject("Oncoming Headlights");
            Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
            go.transform.SetParent(group, false);

            light = go.AddComponent<Light>();
            light.type      = LightType.Spot;
            light.color     = new Color(1f, 0.95f, 0.85f);
            light.range     = 70f;
            light.spotAngle = 80f;
            light.intensity = 0f;
            light.shadows   = LightShadows.None;
            light.enabled   = false;   // the LightRamp step switches it on
            report.AppendLine("  + 'Oncoming Headlights' spot light (off until shot 3)");
        }

        // MoveTo starts from wherever the light is, so park it on Far
        Undo.RecordObject(light.transform, "Build Bus 3 Ending Cutscene");
        light.transform.SetPositionAndRotation(h.far.position, h.far.rotation);

        EnsureGlare(light, report);

        h.light = light;
        return h;
    }

    /// <summary>The way the bus leaves: Stop -> Depart of Rule6, flattened. Falls back to bus.forward.</summary>
    private static Vector3 DriveDirection(SerializedObject ruleSo, Transform bus)
    {
        var stop   = ruleSo.FindProperty("stopPoint").objectReferenceValue   as Transform;
        var depart = ruleSo.FindProperty("departPoint").objectReferenceValue as Transform;

        Vector3 dir = stop != null && depart != null ? depart.position - stop.position : bus.forward;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
    }

    private static bool KeepAhead(Transform point, Transform bus, Vector3 ahead, Vector3 fallback)
    {
        if (Vector3.Dot(point.position - bus.position, ahead) > 0f) return false;

        Undo.RecordObject(point, "Build Bus 3 Ending Cutscene");
        point.SetPositionAndRotation(fallback, Quaternion.LookRotation(-ahead, Vector3.up));
        return true;
    }

    /// <summary>
    /// The spot light only lights the inside of the bus - from a seat the lamps themselves are never
    /// seen. HeadlightGlare draws them (and the white wash) on screen from these two lamp points.
    /// </summary>
    private static void EnsureGlare(Light light, StringBuilder report)
    {
        Transform left  = Lamp(light.transform, "Lamp L", -0.75f);
        Transform right = Lamp(light.transform, "Lamp R",  0.75f);

        var glare = light.GetComponent<HeadlightGlare>();
        if (glare == null)
        {
            glare = Undo.AddComponent<HeadlightGlare>(light.gameObject);
            report.AppendLine("  + HeadlightGlare on 'Oncoming Headlights' (two lamps + glare drawn on screen)");
        }

        var so = new SerializedObject(glare);
        SetLamps(so.FindProperty("lamps"), left, right);
        so.FindProperty("fullIntensity").floatValue = GlareFullIntensity;
        so.ApplyModifiedProperties();
    }

    private static Transform Lamp(Transform parent, string name, float x)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        return go.transform;
    }

    private static void SetLamps(SerializedProperty list, params Transform[] lamps)
    {
        list.arraySize = lamps.Length;
        for (int i = 0; i < lamps.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
    }

    private static Transform Point(Transform group, string name, Vector3 position, Vector3 facing)
    {
        var existing = group.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
        return go.transform;
    }

    // ─────────────────────────── Cracks ───────────────────────────

    private static GameObject EnsureCracks(Transform root, StringBuilder report)
    {
        const string name = "Crash Cracks";
        var existing = root.Find(name);
        if (existing != null) return existing.gameObject;

        var sprite = EnsureCrackSprite(report);

        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Undo.RegisterCreatedObjectUndo(go, "Build Bus 3 Ending Cutscene");
        go.transform.SetParent(root, false);
        go.layer = LayerMask.NameToLayer("UI");

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;   // above the cutscene's black (500)

        var img = new GameObject("Cracks", typeof(RectTransform), typeof(Image));
        img.transform.SetParent(go.transform, false);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var image = img.GetComponent<Image>();
        image.sprite        = sprite;
        image.raycastTarget = false;

        go.SetActive(false);
        report.AppendLine($"  + Created '{name}' overlay (inactive)");
        return go;
    }

    /// <summary>
    /// White cracks radiating from just off the middle of the screen, with a gap in the centre
    /// like the storyboard. Saved as a PNG so it can be swapped for drawn art without code.
    /// </summary>
    private static Sprite EnsureCrackSprite(StringBuilder report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(CracksPath);
        if (existing != null) return existing;

        const int w = 1024, h = 576;
        var alpha = new float[w * h];
        var rng   = new System.Random(6);

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Stamp(float cx, float cy, float r)
        {
            int x0 = Mathf.Max(0, (int)(cx - r - 1)), x1 = Mathf.Min(w - 1, (int)(cx + r + 1));
            int y0 = Mathf.Max(0, (int)(cy - r - 1)), y1 = Mathf.Min(h - 1, (int)(cy + r + 1));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Clamp01(r - d + 0.5f);
                int i = y * w + x;
                if (a > alpha[i]) alpha[i] = a;
            }
        }

        void Line(Vector2 a, Vector2 b, float r)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b));
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, n == 0 ? 0f : i / (float)n);
                Stamp(p.x, p.y, r);
            }
        }

        void Crack(Vector2 p, float angle, float width, int segments, bool canBranch)
        {
            for (int s = 0; s < segments; s++)
            {
                angle += Rand(-0.35f, 0.35f);
                Vector2 next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Rand(35f, 75f);
                Line(p, next, width);
                p = next;

                if (p.x < -20 || p.x > w + 20 || p.y < -20 || p.y > h + 20) return;

                if (canBranch && rng.NextDouble() < 0.3)
                    Crack(p, angle + (rng.NextDouble() < 0.5 ? -1f : 1f) * Rand(0.5f, 1f), width * 0.6f,
                          rng.Next(2, 4), false);
            }
        }

        var center = new Vector2(w * 0.5f, h * 0.52f);
        const int main = 7;
        for (int i = 0; i < main; i++)
        {
            float angle = i / (float)main * Mathf.PI * 2f + Rand(-0.25f, 0.25f);
            Vector2 start = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Rand(55f, 95f);
            Crack(start, angle, 1.6f, 14, true);
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px  = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(alpha[i] * 235f));
        tex.SetPixels32(px);
        tex.Apply();

        string dir = Path.GetDirectoryName(CracksPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));

        File.WriteAllBytes(CracksPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(CracksPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(CracksPath);
        importer.textureType         = TextureImporterType.Sprite;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled       = false;
        importer.SaveAndReimport();

        report.AppendLine($"  + Generated {CracksPath} (replace with drawn art any time)");
        return AssetDatabase.LoadAssetAtPath<Sprite>(CracksPath);
    }
}
