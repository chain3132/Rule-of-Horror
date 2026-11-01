using System.Text;
using Book;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

/// <summary>
/// Sets up the magic-text videos on the TryNewBook pages.
///
/// Add Note Surface: select a page bone (Page1 ... Page7) in the Hierarchy and run it. It adds a
/// quad under that bone with the Book Ink material and a BookNoteSurface, and puts a
/// BookVideoController on the book root if there is none yet.
///
/// Link Note Videos: finds BookVideos/NoteNN/NoteNN_TH and NoteNN_EN for every BookNoteSurface in
/// the open scene and assigns them. Safe to run again after adding or renaming videos.
/// </summary>
public static class BookNoteSetup
{
    private const string MaterialPath = "Assets/NoteBook/BookInk.mat";
    private const string ShaderName   = "Rule of Horror/Book Ink";
    private const string VideoRoot    = "Assets/BookVideos";

    // ─────────────────────────── Add Note Surface ───────────────────────────

    [MenuItem("Rule of Horror/Book/Add Note Surface To Selected Page", false, 60)]
    private static void AddSurface()
    {
        Transform page = PageOf(Selection.activeTransform);
        if (page == null)
        {
            EditorUtility.DisplayDialog("Book",
                "Select a page bone of the book first (Page1 ... Page7 under Armature).", "OK");
            return;
        }

        var material = GetOrCreateMaterial();
        if (material == null) return;

        // Book root = the object with the Animator (the FBX instance), else the top of the hierarchy
        var animator = page.GetComponentInParent<Animator>(true);
        Transform root = animator != null ? animator.transform : page.root;

        if (root.GetComponent<BookVideoController>() == null)
            Undo.AddComponent<BookVideoController>(root.gameObject);

        int pageNumber = BookNoteSurface.ParsePageNumber(page.name);

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = $"Note {pageNumber:00} Surface";
        Undo.RegisterCreatedObjectUndo(go, "Add Note Surface");
        go.transform.SetParent(page, false);

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial    = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows    = false;

        var surface = go.AddComponent<BookNoteSurface>();
        var so = new SerializedObject(surface);
        so.FindProperty("noteNumber").intValue = pageNumber;
        so.ApplyModifiedPropertiesWithoutUndo();

        var report = new StringBuilder();
        Link(surface, report);
        surface.ApplyLayout();

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        Debug.Log($"[Book] Added '{go.name}' under '{page.name}' (spread {surface.Spread}).\n" +
                  report +
                  "Next: move/rotate the quad onto the page surface in the Scene view. The yellow frame is " +
                  "Page Size, the cyan frame is where the ink lands, the cyan arrow must point AWAY from the " +
                  "paper (towards the reader). Lift it ~0.5 mm off the paper. Set Side to Back if this note " +
                  "is on the back of the page.", go);
    }

    [MenuItem("Rule of Horror/Book/Add Note Surface To Selected Page", true)]
    private static bool AddSurfaceValidate() => PageOf(Selection.activeTransform) != null;

    // ─────────────────────────── Page Turner ───────────────────────────

    [MenuItem("Rule of Horror/Book/Add Page Turner (A-D)", false, 62)]
    private static void AddPageTurner()
    {
        var books = Object.FindObjectsByType<BookVideoController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (books.Length == 0)
        {
            EditorUtility.DisplayDialog("Book",
                "No BookVideoController in the open scene. Run 'Add Note Surface To Selected Page' first.", "OK");
            return;
        }

        foreach (var book in books)
        {
            var turner = book.GetComponent<BookPageTurner>();
            if (turner == null)
            {
                turner = Undo.AddComponent<BookPageTurner>(book.gameObject);
                EditorSceneManager.MarkSceneDirty(book.gameObject.scene);
                Debug.Log($"[Book] Added BookPageTurner to '{book.name}'. Save the scene.", turner);
            }
            else
            {
                Debug.Log($"[Book] '{book.name}' already has BookPageTurner - nothing to do.", turner);
            }

            Selection.activeObject = turner;
        }
    }

    // ─────────────────────────── Link Note Videos ───────────────────────────

    [MenuItem("Rule of Horror/Book/Link Note Videos", false, 61)]
    private static void LinkAll()
    {
        var surfaces = Object.FindObjectsByType<BookNoteSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (surfaces.Length == 0)
        {
            EditorUtility.DisplayDialog("Book",
                "No BookNoteSurface in the open scene. Run 'Add Note Surface To Selected Page' first.", "OK");
            return;
        }

        var report = new StringBuilder();
        int linked = 0;

        foreach (var surface in surfaces)
        {
            if (Link(surface, report)) linked++;
            surface.ApplyLayout();
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        }

        Debug.Log($"[Book] Linked videos for {linked}/{surfaces.Length} note(s). Save the scene.\n{report}");
    }

    /// <summary>Finds NoteNN_TH / NoteNN_EN anywhere under BookVideos (any video format).</summary>
    private static bool Link(BookNoteSurface surface, StringBuilder report)
    {
        int n = surface.NoteNumber;
        VideoClip thai    = FindClip($"Note{n:00}_TH");
        VideoClip english = FindClip($"Note{n:00}_EN");

        var so = new SerializedObject(surface);
        if (thai    != null) so.FindProperty("clipThai").objectReferenceValue    = thai;
        if (english != null) so.FindProperty("clipEnglish").objectReferenceValue = english;
        so.ApplyModifiedProperties();

        report.AppendLine($"  Note {n:00}: TH {(thai != null ? "OK" : "MISSING")}, " +
                          $"EN {(english != null ? "OK" : "MISSING")}");

        if (thai != null)    WarnImport(thai, report);
        if (english != null) WarnImport(english, report);

        return thai != null || english != null;
    }

    private static VideoClip FindClip(string fileName)
    {
        if (!AssetDatabase.IsValidFolder(VideoRoot)) return null;

        foreach (string guid in AssetDatabase.FindAssets($"{fileName} t:VideoClip", new[] { VideoRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == fileName)
                return AssetDatabase.LoadAssetAtPath<VideoClip>(path);
        }
        return null;
    }

    /// <summary>The videos have no sound; importing audio only makes the build bigger.</summary>
    private static void WarnImport(VideoClip clip, StringBuilder report)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        if (AssetImporter.GetAtPath(path) is VideoClipImporter importer && importer.importAudio)
            report.AppendLine($"    tip: untick Import Audio on {System.IO.Path.GetFileName(path)}");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    /// <summary>The selected PageN bone, or the PageN above the selection (e.g. a selected surface).</summary>
    private static Transform PageOf(Transform t)
    {
        for (; t != null; t = t.parent)
            if (BookNoteSurface.ParsePageNumber(t.name) > 0) return t;
        return null;
    }

    private static Material GetOrCreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("Book",
                $"Shader '{ShaderName}' not found. Check that Assets/Shaders/BookInk.shader compiled.", "OK");
            return null;
        }

        material = new Material(shader) { name = "BookInk" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }
}
