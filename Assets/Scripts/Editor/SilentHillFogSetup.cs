using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up Silent Hill style fog: real distance fog for the far field, and a particle material
/// that reads as damp air instead of a lit white sheet.
///
/// What was wrong, and why it looked like a billboard rather than weather:
///
///   - RenderSettings.fog was OFF. Silent Hill fog is distance fog, not particles: everything
///     past a short radius dissolves into one flat value. With it off, the far field stayed
///     perfectly sharp and perfectly black, so the particle cards had a visible top edge and
///     visible gaps between the trees. There was nothing for them to blend into.
///
///   - M_Fog_Particle used Color Mode "Additive" (_COLORADDSUBDIFF_ON) with a pure white base
///     colour. Every overlapping card ADDED to the one behind it, so a few layers saturated to
///     white, which is exactly the solid wall in the screenshot. Multiply lets layers build up
///     density instead of brightness.
///
///   - Smoothness 0.58 with a grey specular colour. Fog has no specular; the highlight is what
///     made the sheet look like a surface with a facing direction.
///
///   - Soft particle fade ended at 1m, so cards still cut hard lines across the guardrail and
///     tree trunks they intersected.
///
///   - _CameraFadeParams was (0, Infinity), a broken pack from far == near. With _FADING_ON
///     live, a card drifting through the near plane washed the whole screen flat.
///
/// Re-runnable. Density and colour are starting points meant to be tuned in the Lighting window
/// with the game view open: fog is a look-at-it setting, not a computed one.
/// </summary>
public static class SilentHillFogSetup
{
    private const string FogMaterialPath = "Assets/Particle/M_Fog_Particle.mat";

    // Shadow distance in the PC URP asset is 50m. Exp2 reaches ~95% opacity at 1.73/density,
    // so 0.035 puts the vanishing point right where shadows stop being drawn. Nothing the
    // player can see is left unlit and unfogged at the same time.
    private const float FogDensity = 0.035f;

    // Soft particles have to survive a whole tree trunk passing through a card, not just a kerb
    private const float SoftNear = 0f;
    private const float SoftFar  = 6f;

    // A card that drifts into the camera fades out instead of filling the frame with flat grey
    private const float CameraFadeNear = 1f;
    private const float CameraFadeFar  = 3f;

    [MenuItem("Rule of Horror/Scene/Setup Silent Hill Fog", false, 20)]
    private static void Setup()
    {
        var report = new StringBuilder("[Fog] Silent Hill setup");
        report.AppendLine();

        Color fog = SetupSceneFog(report);
        SetupFogMaterial(report, fog);
        ReportParticleSystems(report);

        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    // ─────────────────────────── Distance fog ───────────────────────────

    /// <summary>Returns the fog colour so the particle tint can be matched to it.</summary>
    private static Color SetupSceneFog(StringBuilder report)
    {
        // Derived from the scene's own ambient rather than hardcoded, so relighting the night
        // does not leave the fog reading as a different time of day than everything else.
        Color ambient = RenderSettings.ambientSkyColor;
        Color fog     = new Color(
            Mathf.Max(ambient.r * 0.62f, 0.10f),
            Mathf.Max(ambient.g * 0.62f, 0.11f),
            Mathf.Max(ambient.b * 0.62f, 0.13f),
            1f);

        RenderSettings.fog = true;

        // Exp2 rather than Exponential: it holds near objects almost clear and then closes in
        // hard, which is the "walking into a wall of nothing" falloff. Linear has a visible
        // start plane that slides across the ground as the player walks.
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor   = fog;

        report.AppendLine($"  - Scene fog ON, Exp2, density {FogDensity:0.000} (~{1.73f / FogDensity:0}m visibility)");
        report.AppendLine($"  - Fog colour #{ColorUtility.ToHtmlStringRGB(fog)}, taken from the scene ambient");

        // URP never fogs the skybox, so the horizon stays whatever the skybox draws there
        if (RenderSettings.skybox != null)
        {
            report.AppendLine("  ! The skybox is NOT affected by fog in URP. If a horizon line shows " +
                              "through the haze, set the camera Background Type to Solid Color and " +
                              "use the fog colour above");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return fog;
    }

    // ─────────────────────────── Particle material ───────────────────────────

    private static void SetupFogMaterial(StringBuilder report, Color fog)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
        if (mat == null)
        {
            report.AppendLine($"  ! No material at {FogMaterialPath} - skipped");
            return;
        }

        Undo.RecordObject(mat, "Setup Silent Hill Fog");

        // ── layers build density, not brightness ──
        mat.SetFloat("_ColorMode", 0f);                  // Multiply
        mat.DisableKeyword("_COLORADDSUBDIFF_ON");
        mat.DisableKeyword("_COLOROVERLAY_ON");
        mat.DisableKeyword("_COLORCOLOR_ON");
        mat.SetColor("_BaseColorAddSubDiff", Color.clear);

        // Lifted well above the fog colour so a lamp still picks the near cards out of the haze,
        // but nowhere near white: white is what made four cards stack into a solid sheet
        Color tint = Color.Lerp(fog, Color.white, 0.38f);
        tint.a = 1f;
        mat.SetColor("_BaseColor", tint);
        mat.SetColor("_Color", tint);

        // ── fog has no surface, so it has no highlight ──
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_Glossiness", 0f);
        mat.SetColor("_SpecColor", Color.black);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

        // ── soft particles ──
        mat.SetFloat("_SoftParticlesEnabled", 1f);
        mat.SetFloat("_SoftParticlesNearFadeDistance", SoftNear);
        mat.SetFloat("_SoftParticlesFarFadeDistance", SoftFar);
        mat.SetVector("_SoftParticleFadeParams",
            new Vector4(SoftNear, 1f / Mathf.Max(0.0001f, SoftFar - SoftNear), 0f, 0f));
        mat.EnableKeyword("_SOFTPARTICLES_ON");

        // ── camera fade (the pack was (0, Infinity), which disabled the effect it claimed) ──
        mat.SetFloat("_CameraFadingEnabled", 1f);
        mat.SetFloat("_CameraNearFadeDistance", CameraFadeNear);
        mat.SetFloat("_CameraFarFadeDistance", CameraFadeFar);
        mat.SetVector("_CameraFadeParams",
            new Vector4(CameraFadeNear, 1f / Mathf.Max(0.0001f, CameraFadeFar - CameraFadeNear), 0f, 0f));
        mat.EnableKeyword("_FADING_ON");

        EditorUtility.SetDirty(mat);

        report.AppendLine("  - Colour Mode Additive -> Multiply (layers now thicken instead of blowing out)");
        report.AppendLine($"  - Base colour white -> #{ColorUtility.ToHtmlStringRGB(tint)}");
        report.AppendLine("  - Smoothness 0.58 -> 0, specular off");
        report.AppendLine($"  - Soft particle fade {SoftNear:0}m - {SoftFar:0}m");
        report.AppendLine($"  - Camera fade repaired: {CameraFadeNear:0}m - {CameraFadeFar:0}m");
    }

    // ─────────────────────────── Particle systems ───────────────────────────

    /// <summary>
    /// Mostly measures rather than edits. Card size, count and placement are art decisions, so the
    /// tool reports what the current setup implies and leaves the judgement to the view.
    /// </summary>
    private static void ReportParticleSystems(StringBuilder report)
    {
        var systems = Object.FindObjectsByType<ParticleSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var mat   = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
        int found = 0;

        foreach (var ps in systems)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.sharedMaterial != mat) continue;

            found++;

            Undo.RecordObject(renderer, "Setup Silent Hill Fog");

            // By Distance stops cards popping in front of each other as the player turns, which
            // is the single most billboard-revealing artifact in a drifting fog bank
            renderer.sortMode          = ParticleSystemSortMode.Distance;
            renderer.alignment         = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            EditorUtility.SetDirty(renderer);

            var main = ps.main;
            report.AppendLine($"  - '{ps.name}': max {main.maxParticles}, " +
                              $"size {main.startSize.constantMin:0.#}-{main.startSize.constantMax:0.#}, " +
                              $"life {main.startLifetime.constantMin:0.#}-{main.startLifetime.constantMax:0.#}s, " +
                              $"start alpha {main.startColor.colorMin.a:0.00}-{main.startColor.colorMax.a:0.00} " +
                              "-> sorted by distance, view aligned");
        }

        if (found == 0)
        {
            report.AppendLine("  ! No particle system in the open scene uses M_Fog_Particle");
            return;
        }

        report.AppendLine("  - Aim for FEW, LARGE, FAINT cards: size 40-90, start alpha 0.06-0.14, " +
                          "30-60 particles total. Many faint overlaps read as depth; few strong " +
                          "ones read as sprites");
        report.AppendLine("  - Keep them NEAR the player (within ~25m) and low to the ground. The far " +
                          "field is distance fog now, so a card out at the treeline only " +
                          "re-introduces the visible edge");
    }
}
