using System.Collections.Generic;
using System.IO;
using InputSystem;
using Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the paper-toss minigame (a box of crumpled paper, a bin, and the player's hand) into the
/// open scene, fully wired, in one click.
///
/// Why this exists: the minigame needs three world objects wired to each other (box → paper ball
/// prefab → bin) plus a component on the player that has to know about the camera and the hand
/// anchor. Wiring that by hand every time guarantees forgetting one, and the symptom is "pressing
/// E does nothing", which is painful to diagnose.
///
/// What it creates is plain boxes and cylinders, enough to play with right away — swap in real
/// models later by replacing the meshes under Visual and keeping the components.
///
/// Safe to re-run: nothing that already exists is rebuilt, only the missing wiring is filled in.
/// </summary>
public static class TrashMinigameBuilder
{
    private const string FolderPath    = "Assets/Prefab/Minigame";
    private const string BallPrefab    = FolderPath + "/PaperBall.prefab";
    private const string CardboardMat  = FolderPath + "/Mat_Cardboard.mat";
    private const string PaperMat      = FolderPath + "/Mat_PaperBall.mat";
    private const string BinMat        = FolderPath + "/Mat_TrashBin.mat";

    private const string RootName = "TrashMinigame";
    private const string UndoName = "Build Paper Toss Minigame";

    /// <summary>An ordinary person's eye height in metres — the yardstick for how much larger
    /// than life this scene is built.</summary>
    private const float HumanEyeHeight = 1.7f;

    [MenuItem("Rule of Horror/Minigame/Build Paper Toss Minigame", false, 70)]
    private static void Build()
    {
        var player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        float scale = ResolveWorldScale(player);

        EnsureFolder();

        Material cardboard = GetOrCreateMaterial(CardboardMat, new Color(0.46f, 0.31f, 0.18f), 0.1f);
        Material paper     = GetOrCreateMaterial(PaperMat,     new Color(0.86f, 0.85f, 0.80f), 0.05f);
        Material metal     = GetOrCreateMaterial(BinMat,       new Color(0.24f, 0.25f, 0.27f), 0.45f);

        GameObject ballPrefab = GetOrCreateBallPrefab(paper, scale);

        Transform root = GetOrCreateRoot(player, scale);
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, UndoName);

        BoxParts box = BuildBox(root, cardboard, paper, scale);
        TrashBin bin = BuildBin(root, metal, scale);

        WireBox(box, ballPrefab, bin);

        bool thrower = WireThrower(player, scale);

        EditorSceneManager.MarkAllScenesDirty();
        Selection.activeGameObject = root.gameObject;

        Debug.Log($"[PaperToss] Minigame placed at {root.position} (scene scale ×{scale:0.##})\n" +
                  (thrower
                      ? "PaperBallThrower is on the player"
                      : "You still need to add PaperBallThrower to the player (no player found in the scene)") +
                  "\nPress Play → walk up to the box → look at it and press E → hold left click to aim, release to throw" +
                  "\nDrag TrashMinigame wherever you like (the box and the bin are both under it)", root);
    }

    // ─────────────────────────── scene scale ───────────────────────────

    /// <summary>
    /// How much larger than life this scene is, read from the player's standing camera height.
    ///
    /// This project's character has the camera at 2.55 m rather than a real person's ~1.7 m, so a
    /// life-sized box would look like a toy, and shots would miss because the bin is smaller than
    /// the eye expects it to be.
    /// </summary>
    private static float ResolveWorldScale(PlayerController player)
    {
        if (player == null) return 1.5f;

        var so   = new SerializedObject(player);
        var prop = so.FindProperty("standingCameraHeight");
        if (prop == null || prop.floatValue <= 0.1f) return 1.5f;

        return Mathf.Clamp(prop.floatValue / HumanEyeHeight, 1f, 2.5f);
    }

    // ─────────────────────────── Asset ───────────────────────────

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(FolderPath)) return;

        if (!AssetDatabase.IsValidFolder("Assets/Prefab")) AssetDatabase.CreateFolder("Assets", "Prefab");
        AssetDatabase.CreateFolder("Assets/Prefab", "Minigame");
    }

    private static Material GetOrCreateMaterial(string path, Color color, float smoothness)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };

        if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))      mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static GameObject GetOrCreateBallPrefab(Material paper, float scale)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefab);
        if (existing != null) return existing;

        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        temp.name = "PaperBall";

        // Squashed slightly so it reads as a crumpled wad rather than a ball (SphereCollider uses the largest axis)
        temp.transform.localScale = new Vector3(1f, 0.88f, 1.06f) * (0.13f * scale);
        temp.GetComponent<Renderer>().sharedMaterial = paper;

        var rb = temp.AddComponent<Rigidbody>();
        rb.mass                   = 0.08f;   // crumpled paper is light; ball-weight would not travel
        rb.linearDamping          = 0.25f;
        rb.angularDamping         = 0.6f;
        rb.interpolation          = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var audio = temp.AddComponent<AudioSource>();
        audio.playOnAwake  = false;
        audio.spatialBlend = 1f;   // 3D — the landing sound has to come from where it lands
        audio.minDistance  = 1f;
        audio.maxDistance  = 14f;

        temp.AddComponent<PaperBall>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, BallPrefab);
        Object.DestroyImmediate(temp);

        return prefab;
    }

    // ─────────────────────────── scene ───────────────────────────

    private static Transform GetOrCreateRoot(PlayerController player, float scale)
    {
        var existing = GameObject.Find(RootName);
        if (existing != null) return existing.transform;

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, UndoName);

        root.transform.position = ResolvePlacement(player, scale);
        if (player != null) root.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);

        return root.transform;
    }

    /// <summary>Places it in front of the player and drops it onto the ground with a raycast
    /// (move it afterwards as you like).</summary>
    private static Vector3 ResolvePlacement(PlayerController player, float scale)
    {
        if (player == null) return Vector3.zero;

        Vector3 guess = player.transform.position + player.transform.forward * (2f * scale);

        if (Physics.Raycast(guess + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 12f))
            return hit.point;

        return guess;
    }

    /// <summary>The pieces of the box that still have to be wired together afterwards.</summary>
    private struct BoxParts
    {
        public PaperBallBox     box;
        public GameObject       visual;
        public Transform        spawn;
        public List<GameObject> inBox;
    }

    private static BoxParts BuildBox(Transform root, Material cardboard, Material paper, float scale)
    {
        Transform boxRoot = GetOrCreate(root, "PaperBox", out bool created);
        if (created) boxRoot.localPosition = Vector3.zero;

        var box = boxRoot.GetComponent<PaperBallBox>();
        if (box == null) box = Undo.AddComponent<PaperBallBox>(boxRoot.gameObject);

        Transform visual = GetOrCreate(boxRoot, "Visual", out bool visualCreated);
        if (visualCreated) visual.localPosition = Vector3.zero;

        float w = 0.62f * scale;   // width / depth
        float h = 0.42f * scale;   // height
        float t = 0.03f * scale;   // wall thickness

        if (visual.childCount == 0)
        {
            Panel(visual, "Floor", cardboard, new Vector3(0f, t * 0.5f, 0f),      new Vector3(w, t, w));
            Panel(visual, "Wall_N", cardboard, new Vector3(0f, h * 0.5f,  w / 2f), new Vector3(w, h, t));
            Panel(visual, "Wall_S", cardboard, new Vector3(0f, h * 0.5f, -w / 2f), new Vector3(w, h, t));
            Panel(visual, "Wall_E", cardboard, new Vector3( w / 2f, h * 0.5f, 0f), new Vector3(t, h, w));
            Panel(visual, "Wall_W", cardboard, new Vector3(-w / 2f, h * 0.5f, 0f), new Vector3(t, h, w));
        }

        Transform decor = GetOrCreate(visual, "BallsInBox", out bool decorCreated);
        if (decorCreated) decor.localPosition = Vector3.zero;

        var inBox = new List<GameObject>();
        if (decor.childCount == 0)
        {
            // Six wads in two loose layers, deliberately not aligned — decoration only, no colliders
            Vector3[] spots =
            {
                new Vector3(-0.16f, 0.10f, -0.14f), new Vector3( 0.15f, 0.11f, -0.16f),
                new Vector3(-0.14f, 0.11f,  0.16f), new Vector3( 0.16f, 0.10f,  0.15f),
                new Vector3(-0.02f, 0.26f, -0.02f), new Vector3( 0.04f, 0.27f,  0.10f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = $"Ball_{i + 1}";
                ball.transform.SetParent(decor, false);
                ball.transform.localPosition = spots[i] * scale;
                ball.transform.localRotation = Random.rotation;
                ball.transform.localScale    = new Vector3(1f, 0.88f, 1.06f) * (0.14f * scale);
                ball.GetComponent<Renderer>().sharedMaterial = paper;

                // The wads in the box are scenery; with colliders a thrown ball would bounce off them for no reason
                Object.DestroyImmediate(ball.GetComponent<Collider>());

                inBox.Add(ball);
            }
        }
        else
        {
            foreach (Transform child in decor) inBox.Add(child.gameObject);
        }

        Transform spawn = GetOrCreate(boxRoot, "SpawnPoint", out bool spawnCreated);
        if (spawnCreated) spawn.localPosition = new Vector3(0f, h + 0.12f * scale, 0f);

        return new BoxParts
        {
            box    = box,
            visual = visual.gameObject,
            spawn  = spawn,
            inBox  = inBox,
        };
    }

    private static TrashBin BuildBin(Transform root, Material metal, float scale)
    {
        Transform binRoot = GetOrCreate(root, "TrashBin", out bool created);
        if (created) binRoot.localPosition = new Vector3(0f, 0f, 3f * scale);

        var bin = binRoot.GetComponent<TrashBin>();
        if (bin == null) bin = Undo.AddComponent<TrashBin>(binRoot.gameObject);

        float radius = 0.3f  * scale;
        float height = 0.6f  * scale;
        float thick  = 0.02f * scale;

        Transform visual = GetOrCreate(binRoot, "Visual", out bool visualCreated);
        if (visualCreated) visual.localPosition = Vector3.zero;

        if (visual.childCount == 0)
        {
            const int segments = 14;
            float     width    = 2f * Mathf.PI * radius / segments * 1.08f;   // slight overlap hides the seams

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var   panel = Panel(visual, $"Side_{i + 1}", metal,
                                    new Vector3(Mathf.Sin(angle) * radius, height * 0.5f, Mathf.Cos(angle) * radius),
                                    new Vector3(width, height, thick));

                panel.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
            }

            var bottom = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bottom.name = "Bottom";
            bottom.transform.SetParent(visual, false);
            bottom.transform.localPosition = new Vector3(0f, thick, 0f);
            bottom.transform.localScale    = new Vector3(radius * 2f, thick, radius * 2f);
            bottom.GetComponent<Renderer>().sharedMaterial = metal;
        }

        // The scoring zone is a trigger collider on the bin itself (the walls are the children's own
        // solid colliders, not triggers), so a ball that bounces off the rim does not count — only one
        // that actually makes it inside
        var zone = binRoot.GetComponent<BoxCollider>();
        if (zone == null) zone = Undo.AddComponent<BoxCollider>(binRoot.gameObject);

        zone.isTrigger = true;
        zone.center    = new Vector3(0f, height * 0.35f, 0f);
        zone.size      = new Vector3(radius * 1.4f, height * 0.6f, radius * 1.4f);

        return bin;
    }

    // ─────────────────────────── wiring ───────────────────────────

    private static void WireBox(BoxParts parts, GameObject ballPrefab, TrashBin bin)
    {
        var so = new SerializedObject(parts.box);

        SetObject(so, "ballPrefab", ballPrefab != null ? ballPrefab.GetComponent<PaperBall>() : null);
        SetObject(so, "spawnPoint", parts.spawn);
        SetObject(so, "visualRoot", parts.visual);

        var list = so.FindProperty("ballsInBox");
        if (list != null && parts.inBox != null && list.arraySize == 0)
        {
            list.arraySize = parts.inBox.Count;
            for (int i = 0; i < parts.inBox.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = parts.inBox[i];
        }

        // The bin is part of the minigame, so it disappears along with the box on entering Tension
        // (take it out of the Also Hide list if you would rather it stayed)
        var hide = so.FindProperty("alsoHide");
        if (hide != null && bin != null && hide.arraySize == 0)
        {
            hide.arraySize = 1;
            hide.GetArrayElementAtIndex(0).objectReferenceValue = bin.gameObject;
        }

        so.ApplyModifiedProperties();
    }

    private static bool WireThrower(PlayerController player, float scale)
    {
        if (player == null) return false;

        var thrower = player.GetComponent<PaperBallThrower>();
        if (thrower == null) thrower = Undo.AddComponent<PaperBallThrower>(player.gameObject);

        var cam   = player.GetComponentInChildren<Camera>(true);
        var input = Object.FindAnyObjectByType<InputHandler>(FindObjectsInactive.Include);

        Vector3 handOffset = new Vector3(0.3f, -0.26f, 0.5f) * scale;

        Transform hand = null;
        if (cam != null)
        {
            hand = GetOrCreate(cam.transform, "PaperBallHand", out bool created);
            if (created) hand.localPosition = handOffset;
        }

        var so = new SerializedObject(thrower);

        SetObject(so, "inputHandler", input);
        SetObject(so, "playerCamera", cam);
        SetObject(so, "handAnchor",   hand);

        var offset = so.FindProperty("handOffset");
        if (offset != null) offset.vector3Value = handOffset;

        // Throw strength has to scale with the scene — gravity does not scale along with the world
        SetFloat(so, "minThrowSpeed",  4.5f * scale);
        SetFloat(so, "maxThrowSpeed", 11f   * scale);
        SetFloat(so, "ballReach",      2.2f * scale);

        so.ApplyModifiedProperties();
        return true;
    }

    // ─────────────────────────── Utility ───────────────────────────

    private static Transform GetOrCreate(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(name);
        if (existing != null) { created = false; return existing; }

        var child = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(child, UndoName);
        child.transform.SetParent(parent, false);

        created = true;
        return child.transform;
    }

    private static Transform Panel(Transform parent, string name, Material mat, Vector3 position, Vector3 size)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = position;
        panel.transform.localScale    = size;
        panel.GetComponent<Renderer>().sharedMaterial = mat;

        return panel.transform;
    }

    private static void SetObject(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null || value == null) return;

        prop.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedObject so, string field, float value)
    {
        var prop = so.FindProperty(field);
        if (prop != null) prop.floatValue = value;
    }
}
