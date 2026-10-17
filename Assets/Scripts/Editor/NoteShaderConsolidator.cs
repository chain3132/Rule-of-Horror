using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Points Note1..Note9 materials at the Note0 shader and removes the nine duplicate graphs.
///
/// The problem this solves, measured from Editor.log:
///
///   Shader Graphs/Note0 .. Note9 took 1569 seconds to compile, 47.6% of all shader compile time
///   in the build. They are ten copies of one graph. Nine of them are pure duplicated work, which
///   is ~43% of the build's shader cost for nothing.
///
/// Why there were ten: the graph samples its texture straight into a Sample Texture 2D node
/// instead of through an exposed Texture2D property. Shader Graph then emits a hidden property
/// whose name carries the node's GUID, so the texture cannot be swapped per material - the only
/// way to give each note its own image was to duplicate the whole graph.
///
/// Why no graph edit is needed anyway: the nine copies were made by duplicating the FILE, which
/// keeps the internal node ids. Only the asset GUID in the .meta changed. So all ten graphs emit
/// the SAME hidden property names:
///
///   _SampleTexture2D_218d38749c2a4d42abc8cbe523d6bf6f_Texture_1_Texture2D   (the note image)
///   _SampleTexture2D_e2695611125843b39146df2ecd6c26bc_Texture_1_Texture2D   (the burn mask)
///
/// Material values are stored by property name, so reassigning the shader carries every texture,
/// colour and float across untouched. This tool still snapshots and re-applies every value rather
/// than trusting that, and refuses to migrate a material that would lose a non-empty value.
///
/// Exposing the texture properly in Note0 is still the tidier end state, but it is not required
/// for this consolidation and can be done later without redoing any of this.
/// </summary>
public static class NoteShaderConsolidator
{
    private const string GraphFolder    = "Assets/Models/BurningNote/Dissovle";
    private const string MaterialFolder = "Assets/Models/BurningNote/Material";

    private const string Keeper        = "Note0";
    private const int    FirstDuplicate = 1;
    private const int    LastDuplicate  = 9;

    private static string GraphPath(string name)    => $"{GraphFolder}/{name}.shadergraph";
    private static string MaterialPath(string name) => $"{MaterialFolder}/{name}.mat";

    // ─────────────────────────── Menu ───────────────────────────

    [MenuItem("Rule of Horror/Build/Consolidate Note Shaders", false, 40)]
    private static void Consolidate()
    {
        var report = new StringBuilder("[Note] Shader consolidation");
        report.AppendLine();

        var keeper = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath(Keeper));
        if (keeper == null)
        {
            Debug.LogError($"[Note] No shader at {GraphPath(Keeper)} - nothing to consolidate");
            return;
        }

        HashSet<string> keeperProps = PropertyNamesOf(keeper);
        report.AppendLine($"  Target shader '{keeper.name}' exposes {keeperProps.Count} properties");

        var migrated = new List<string>();
        var skipped  = new List<string>();

        for (int i = FirstDuplicate; i <= LastDuplicate; i++)
        {
            string name = $"Note{i}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name));

            if (material == null)
            {
                report.AppendLine($"  ! {name}.mat not found - skipped");
                skipped.Add(name);
                continue;
            }

            if (material.shader == keeper)
            {
                report.AppendLine($"  - {name}.mat already uses {Keeper}");
                migrated.Add(name);
                continue;
            }

            if (Migrate(material, keeper, keeperProps, report))
                migrated.Add(name);
            else
                skipped.Add(name);
        }

        AssetDatabase.SaveAssets();

        report.AppendLine();
        report.AppendLine($"  {migrated.Count} migrated, {skipped.Count} skipped");

        if (skipped.Count == 0)
            OfferDeletion(report);
        else
            report.AppendLine("  Duplicate graphs kept - some materials did not migrate, so deleting " +
                              "them now would break those materials");

        Debug.Log(report.ToString());
    }

    // ─────────────────────────── Migration ───────────────────────────

    /// <summary>
    /// Returns false and changes nothing when a value would be dropped, so a partial migration can
    /// never silently lose a texture.
    /// </summary>
    private static bool Migrate(Material material, Shader keeper, HashSet<string> keeperProps,
                                StringBuilder report)
    {
        string name = material.name;
        List<PropertyValue> snapshot = Snapshot(material);

        // Anything the target shader does not declare is a value with nowhere to go. Empty slots
        // are fine to drop; a slot holding real data is not, so the material is left alone and
        // reported instead.
        var lost = new List<string>();
        foreach (var prop in snapshot)
            if (!keeperProps.Contains(prop.Name) && !prop.IsEmpty)
                lost.Add(prop.Name);

        if (lost.Count > 0)
        {
            report.AppendLine($"  ! {name}.mat NOT migrated - {Keeper} has no property for: " +
                              string.Join(", ", lost));
            return false;
        }

        Undo.RecordObject(material, "Consolidate Note Shaders");

        // Keywords and render queue are not property values, so they are carried by hand
        string[] keywords    = material.shaderKeywords;
        int      renderQueue = material.renderQueue;

        material.shader = keeper;

        foreach (var prop in snapshot)
            if (keeperProps.Contains(prop.Name))
                prop.ApplyTo(material);

        material.shaderKeywords = keywords;
        material.renderQueue    = renderQueue;

        // Read everything back rather than assuming the writes took
        var wrong = new List<string>();
        foreach (var prop in snapshot)
            if (keeperProps.Contains(prop.Name) && !prop.MatchesOn(material))
                wrong.Add(prop.Name);

        EditorUtility.SetDirty(material);

        if (wrong.Count > 0)
        {
            report.AppendLine($"  ! {name}.mat migrated but these did not read back: " +
                              string.Join(", ", wrong));
            return false;
        }

        report.AppendLine($"  - {name}.mat -> {Keeper} ({snapshot.Count} properties carried over)");
        return true;
    }

    // ─────────────────────────── Deletion ───────────────────────────

    /// <summary>
    /// Deleting assets cannot be undone from here, so this confirms first and only offers graphs
    /// that nothing in the project still points at.
    /// </summary>
    private static void OfferDeletion(StringBuilder report)
    {
        var orphans = new List<string>();
        HashSet<string> stillUsed = ShaderPathsUsedByMaterials();
        HashSet<string> alwaysIncluded = AlwaysIncludedShaderPaths();

        for (int i = FirstDuplicate; i <= LastDuplicate; i++)
        {
            string path = GraphPath($"Note{i}");
            if (AssetDatabase.LoadAssetAtPath<Shader>(path) == null) continue;

            if (stillUsed.Contains(path))
            {
                report.AppendLine($"  ! {path} is still used by a material - kept");
                continue;
            }

            if (alwaysIncluded.Contains(path))
            {
                report.AppendLine($"  ! {path} is in Graphics ▸ Always Included Shaders - kept");
                continue;
            }

            orphans.Add(path);
        }

        if (orphans.Count == 0)
        {
            report.AppendLine("  No duplicate graphs left to delete");
            return;
        }

        bool deleteThem = EditorUtility.DisplayDialog(
            "Delete duplicate Note shaders?",
            $"{orphans.Count} shader graphs are now unused by any material:\n\n" +
            string.Join("\n", orphans) +
            "\n\nDeleting them is what actually removes the build cost. This cannot be undone " +
            "from the Editor, though the files are still in git.",
            $"Delete {orphans.Count} graphs", "Keep them");

        if (!deleteThem)
        {
            report.AppendLine($"  {orphans.Count} unused graphs kept - run again to delete them");
            return;
        }

        foreach (string path in orphans)
        {
            if (AssetDatabase.DeleteAsset(path)) report.AppendLine($"  - Deleted {path}");
            else                                 report.AppendLine($"  ! Could not delete {path}");
        }

        AssetDatabase.Refresh();
        report.AppendLine($"  {orphans.Count} duplicate graphs removed from the build");
    }

    private static HashSet<string> ShaderPathsUsedByMaterials()
    {
        var used = new HashSet<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Material"))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material == null || material.shader == null) continue;

            string path = AssetDatabase.GetAssetPath(material.shader);
            if (!string.IsNullOrEmpty(path)) used.Add(path);
        }

        return used;
    }

    /// <summary>A shader listed here is built even with no material on it, so deleting one leaves a hole.</summary>
    private static HashSet<string> AlwaysIncludedShaderPaths()
    {
        var paths = new HashSet<string>();

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0) return paths;

        var list = new SerializedObject(assets[0]).FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return paths;

        for (int i = 0; i < list.arraySize; i++)
        {
            var shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (shader == null) continue;

            string path = AssetDatabase.GetAssetPath(shader);
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        return paths;
    }

    // ─────────────────────────── Property snapshot ───────────────────────────

    private static HashSet<string> PropertyNamesOf(Shader shader)
    {
        var names = new HashSet<string>();
        for (int i = 0; i < shader.GetPropertyCount(); i++)
            names.Add(shader.GetPropertyName(i));
        return names;
    }

    private static List<PropertyValue> Snapshot(Material material)
    {
        var values = new List<PropertyValue>();
        Shader shader = material.shader;
        if (shader == null) return values;

        for (int i = 0; i < shader.GetPropertyCount(); i++)
            values.Add(new PropertyValue(material, shader.GetPropertyName(i), shader.GetPropertyType(i)));

        return values;
    }

    private sealed class PropertyValue
    {
        public readonly string             Name;
        private readonly ShaderPropertyType _type;

        private readonly Color   _color;
        private readonly Vector4 _vector;
        private readonly float   _number;
        private readonly int     _integer;
        private readonly Texture _texture;
        private readonly Vector2 _tiling;
        private readonly Vector2 _offset;

        public PropertyValue(Material material, string name, ShaderPropertyType type)
        {
            Name  = name;
            _type = type;

            switch (type)
            {
                case ShaderPropertyType.Color:   _color   = material.GetColor(name);   break;
                case ShaderPropertyType.Vector:  _vector  = material.GetVector(name);  break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:   _number  = material.GetFloat(name);   break;
                case ShaderPropertyType.Int:     _integer = material.GetInteger(name); break;
                case ShaderPropertyType.Texture:
                    _texture = material.GetTexture(name);
                    _tiling  = material.GetTextureScale(name);
                    _offset  = material.GetTextureOffset(name);
                    break;
            }
        }

        /// <summary>Only a texture slot can be meaningfully empty; a number always carries something.</summary>
        public bool IsEmpty => _type == ShaderPropertyType.Texture && _texture == null;

        public void ApplyTo(Material material)
        {
            switch (_type)
            {
                case ShaderPropertyType.Color:   material.SetColor(Name, _color);     break;
                case ShaderPropertyType.Vector:  material.SetVector(Name, _vector);   break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:   material.SetFloat(Name, _number);    break;
                case ShaderPropertyType.Int:     material.SetInteger(Name, _integer); break;
                case ShaderPropertyType.Texture:
                    material.SetTexture(Name, _texture);
                    material.SetTextureScale(Name, _tiling);
                    material.SetTextureOffset(Name, _offset);
                    break;
            }
        }

        public bool MatchesOn(Material material)
        {
            if (!material.HasProperty(Name)) return false;

            switch (_type)
            {
                case ShaderPropertyType.Color:   return material.GetColor(Name)   == _color;
                case ShaderPropertyType.Vector:  return material.GetVector(Name)  == _vector;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:   return Mathf.Approximately(material.GetFloat(Name), _number);
                case ShaderPropertyType.Int:     return material.GetInteger(Name) == _integer;
                case ShaderPropertyType.Texture: return material.GetTexture(Name) == _texture;
                default:                         return true;
            }
        }
    }
}
