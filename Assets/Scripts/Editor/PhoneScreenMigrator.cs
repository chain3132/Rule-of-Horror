using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Moves the phone screen onto a new phone model in one command, without the image zooming
/// or stretching.
///
/// Why the image zooms when the model changes: the RenderTexture is applied through the UVs of
/// the screen submesh, and a new model maps those UVs over a different range (on some models the
/// screen only covers 0.3–0.5 of UV space), so the image is blown up into that patch.
/// Tiling/Offset cannot really fix it because the UVs are not perfectly planar either.
/// This tool therefore never touches UVs — it lays a flat quad with UV 0..1 over the screen
/// instead (see PhoneScreenQuadBuilder).
///
/// Three things have to agree for the image to come out undistorted, and this handles all three:
///   1. Quad aspect = the model's real screen aspect (measured from the submesh, not from the
///      bounds of the whole phone)
///   2. RenderTexture aspect = the same, otherwise the image is stretched when applied
///   3. CanvasScaler Reference Resolution = exactly the RenderTexture size → the UI draws 1:1
///      (this is the one that causes "text too big / edges cut off" when it is wrong)
///
/// Usage: select the new phone's GameObject in the Hierarchy, then run this menu item.
/// </summary>
public static class PhoneScreenMigrator
{
    [MenuItem("Rule of Horror/Phone/Migrate Screen To New Phone (select it first)", false, 59)]
    private static void Migrate()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            Debug.LogError("[PhoneMigrate] Nothing selected — select the new phone's GameObject " +
                           "(Phone_New under the player, for example) in the Hierarchy first");
            return;
        }

        if (EditorUtility.IsPersistent(root))
        {
            Debug.LogError("[PhoneMigrate] The selection is a project asset, not a scene object — " +
                           "select the one in the open scene's Hierarchy", root);
            return;
        }

        if (!ScreenSubmeshAnalyzer.TryMeasureScreen(root, out var face)) return;   // reports its own error

        var report = new StringBuilder();
        report.AppendLine($"[PhoneMigrate] Screen moved to '{root.name}'");
        report.AppendLine($"  Real screen {face.worldSize[face.widthAxis]:0.####} × " +
                          $"{face.worldSize[face.heightAxis]:0.####} m (aspect {face.Aspect:0.0000})");

        // ── 1. Quad ──────────────────────────────────────────────────────────
        var quad = PhoneScreenQuadBuilder.BuildFor(root);
        if (quad == null) return;
        report.AppendLine($"  • Quad placed on '{quad.parent.name}' (flat UV 0..1, no distortion)");

        // ── 2. RenderTexture ─────────────────────────────────────────────────
        int targetWidth  = face.SuggestedRtWidth;
        int targetHeight = ScreenSubmeshAnalyzer.ReferenceHeight;

        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(PhoneScreenQuadBuilder.RenderTexPath);
        if (rt == null)
        {
            Debug.LogError($"[PhoneMigrate] No RenderTexture at {PhoneScreenQuadBuilder.RenderTexPath} — " +
                           "cannot continue", root);
            return;
        }

        if (rt.width != targetWidth || rt.height != targetHeight)
        {
            report.AppendLine($"  • RenderTexture {rt.width} × {rt.height} → {targetWidth} × {targetHeight}");

            Undo.RecordObject(rt, "Resize Phone Screen RenderTexture");

            // Release first, otherwise the old GPU buffer stays alive at the previous size
            rt.Release();
            rt.width  = targetWidth;
            rt.height = targetHeight;

            EditorUtility.SetDirty(rt);
            AssetDatabase.SaveAssets();
        }
        else
        {
            report.AppendLine($"  • RenderTexture {rt.width} × {rt.height} already matches");
        }

        // ── 3. Canvas ────────────────────────────────────────────────────────
        // Reference Resolution = exactly the RT size → scaleFactor lands on 1, so the UI you edit
        // in the Canvas is the same size as the UI on the phone, with no extra scaling on top
        int canvasFixed = 0;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                FindObjectsSortMode.None))
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera) continue;
            if (canvas.worldCamera == null || canvas.worldCamera.targetTexture != rt) continue;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) continue;

            var wanted = new Vector2(targetWidth, targetHeight);
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                scaler.referenceResolution == wanted &&
                scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand)
                continue;

            Undo.RecordObject(scaler, "Set Phone Screen CanvasScaler");
            report.AppendLine($"  • CanvasScaler '{canvas.name}' Reference Resolution " +
                              $"{scaler.referenceResolution.x:0} × {scaler.referenceResolution.y:0} → " +
                              $"{targetWidth} × {targetHeight} (Expand)");

            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = wanted;
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;

            EditorUtility.SetDirty(scaler);
            canvasFixed++;
        }

        if (canvasFixed == 0)
            report.AppendLine("  • No Canvas renders into this RenderTexture's camera — check that the " +
                              "Canvas is set to Screen Space - Camera with PhoneCamera assigned");

        // ── 4. PhoneSystem ───────────────────────────────────────────────────
        var phone = Object.FindAnyObjectByType<PhoneSystem.PhoneSystem>();
        if (phone == null)
        {
            report.AppendLine("  • No PhoneSystem in the scene — assign Phone Object by hand later");
        }
        else
        {
            var so    = new SerializedObject(phone);
            var prop  = so.FindProperty("phoneObject");
            var oldGo = prop != null ? prop.objectReferenceValue as GameObject : null;

            if (prop == null)
            {
                report.AppendLine("  • PhoneSystem no longer has a phoneObject field (renamed?) — skipped");
            }
            else if (oldGo == root)
            {
                report.AppendLine("  • PhoneSystem already points at this phone");
            }
            else
            {
                prop.objectReferenceValue = root;
                so.ApplyModifiedProperties();

                report.AppendLine($"  • PhoneSystem.Phone Object: " +
                                  $"'{(oldGo != null ? oldGo.name : "empty")}' → '{root.name}'");

                if (oldGo != null)
                    report.AppendLine($"    The old phone '{oldGo.name}' is still in the scene — disable or " +
                                      "delete it so its ScreenQuad does not stay in the player's hand");
            }
        }

        // ── 5. Warn about a doubled image ────────────────────────────────────
        // If the screen submesh's material still holds the RT there is a second copy of the image
        // behind the quad. Normally it is hidden, but the edges do not line up and at a glancing
        // angle you can see both layers.
        var mats = face.renderer.sharedMaterials;
        int first = face.submesh >= 0 ? face.submesh : 0;                 // -1 = the whole object is the screen
        int last  = face.submesh >= 0 ? face.submesh + 1 : mats.Length;

        for (int i = first; i < last && i < mats.Length; i++)
        {
            var mat = mats[i];
            if (mat == null) continue;

            bool holdsRt = (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == rt) ||
                           (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == rt);

            if (holdsRt)
                report.AppendLine($"  ⚠ The screen material '{mat.name}' still holds the RenderTexture — " +
                                  "set its Base Map to None (black) or there is a second image behind the quad");
        }

        report.Append("  If the image is sideways or mirrored, use Phone ▸ Screen Quad — Rotate 90° / Flip");

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = quad.gameObject;
        Debug.Log(report.ToString(), quad.gameObject);
    }
}
