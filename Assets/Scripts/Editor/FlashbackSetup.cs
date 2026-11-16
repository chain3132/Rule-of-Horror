using Flashbacks;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Builds the flashback pieces in the open scene:
///   - Create Flashback Screen: the full-screen old-film canvas + FlashbackPlayer (once per scene)
///   - Create Flashback Template: one flashback scene (Flashback + PlayableDirector + Stage + camera)
/// Both are safe to re-run. Save the scene afterwards.
/// </summary>
internal static class FlashbackSetup
{
    private const string Folder       = "Assets/Flashback";
    private const string MaterialPath = Folder + "/OldFilm.mat";
    private const string DustPath     = Folder + "/Textures/OldFilmDust.png";
    private const string ShaderName   = "Rule of Horror/Old Film (UI)";

    // Above the eyelid canvas (0), below PlayerDialogueUI (10) so subtitles show on top
    private const int SortingOrder = 5;

    [MenuItem("Rule of Horror/Flashback/Create Flashback Screen")]
    private static void CreateScreen()
    {
        var existing = Object.FindAnyObjectByType<FlashbackPlayer>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[Flashback] The scene already has a Flashback Screen - selected it", existing);
            return;
        }

        var material = EnsureMaterial();
        if (material == null) return;

        var root = new GameObject("Flashback Screen", typeof(RectTransform), typeof(Canvas),
                                  typeof(CanvasScaler), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Create Flashback Screen");

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        var group = root.GetComponent<CanvasGroup>();
        group.alpha          = 0f;
        group.interactable   = false;
        group.blocksRaycasts = false;

        // Film image, stretched over the whole screen
        var film = new GameObject("Film", typeof(RectTransform), typeof(RawImage));
        film.transform.SetParent(root.transform, false);
        Stretch(film.GetComponent<RectTransform>());
        var raw = film.GetComponent<RawImage>();
        raw.material      = material;
        raw.color         = Color.white;
        raw.raycastTarget = false;

        // Skip hint, bottom-right, same font as the subtitles (it carries the Thai glyphs)
        var hint = new GameObject("Skip Hint", typeof(RectTransform), typeof(TextMeshProUGUI));
        hint.transform.SetParent(root.transform, false);
        var hrt = hint.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 0f);
        hrt.anchoredPosition = new Vector2(-48f, 40f);
        hrt.sizeDelta        = new Vector2(500f, 50f);
        var label = hint.GetComponent<TextMeshProUGUI>();
        label.text          = "Space   Skip";
        label.fontSize      = 26;
        label.alignment     = TextAlignmentOptions.BottomRight;
        label.color         = new Color(1f, 1f, 1f, 0.55f);
        label.raycastTarget = false;
        var dialogueFont = FindDialogueFont();
        if (dialogueFont != null) label.font = dialogueFont;

        var player = root.AddComponent<FlashbackPlayer>();
        var so = new SerializedObject(player);
        so.FindProperty("screenGroup").objectReferenceValue = group;
        so.FindProperty("screen").objectReferenceValue      = raw;
        so.FindProperty("skipLabel").objectReferenceValue   = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Flashback] Created Flashback Screen (sorting order 5) - save the scene. " +
                  "Tune the film look on " + MaterialPath, root);
    }

    [MenuItem("Rule of Horror/Flashback/Create Flashback Template")]
    private static void CreateTemplate()
    {
        var root = new GameObject("Flashback - New", typeof(Flashback), typeof(PlayableDirector));
        Undo.RegisterCreatedObjectUndo(root, "Create Flashback");

        // Put it near where the scene view is looking, so it is easy to find
        if (SceneView.lastActiveSceneView != null)
            root.transform.position = SceneView.lastActiveSceneView.pivot;

        var director = root.GetComponent<PlayableDirector>();
        director.playOnAwake    = false;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;

        var stage = new GameObject("Stage");
        stage.transform.SetParent(root.transform, false);

        var camGo = new GameObject("Flashback Camera", typeof(Camera));
        camGo.transform.SetParent(stage.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, -3f);
        camGo.tag = "Untagged";   // never MainCamera: the game looks the player camera up by tag

        var cam = camGo.GetComponent<Camera>();
        cam.enabled       = false;   // FlashbackPlayer renders it one film frame at a time
        cam.fieldOfView   = 50f;
        cam.nearClipPlane = 0.05f;

        // The game's global Volume changes with Relax/Tension - keep it off the film; the old-film
        // shader does the whole look
        var urp = cam.GetUniversalAdditionalCameraData();
        if (urp != null) urp.renderPostProcessing = false;

        stage.SetActive(false);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Flashback] Created a flashback template. Next: set Trigger + Rule, build the scene " +
                  "under Stage, and make a Timeline on the PlayableDirector (Window > Sequencing > Timeline). " +
                  "Animate the Flashback Camera with an Animation Track - not a CinemachineCamera.", root);
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static Material EnsureMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[Flashback] Shader '{ShaderName}' not found - is Assets/Shaders/OldFilmUI.shader compiled?");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Flashback");

        material = new Material(shader) { name = "OldFilm" };

        // The dust overlay is a single non-tiling frame: mirror it at the edges when it wanders
        var importer = AssetImporter.GetAtPath(DustPath) as TextureImporter;
        if (importer != null && importer.wrapMode != TextureWrapMode.Mirror)
        {
            importer.wrapMode = TextureWrapMode.Mirror;
            importer.SaveAndReimport();
        }
        var dust = AssetDatabase.LoadAssetAtPath<Texture2D>(DustPath);
        if (dust != null) material.SetTexture("_DustTex", dust);
        else Debug.LogWarning($"[Flashback] {DustPath} missing - the film has no dust/scratch overlay");

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static TMP_FontAsset FindDialogueFont()
    {
        var dialogue = Object.FindAnyObjectByType<PlayerDialogueUI>(FindObjectsInactive.Include);
        var text = dialogue != null ? dialogue.GetComponentInChildren<TMP_Text>(true) : null;
        return text != null ? text.font : null;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
