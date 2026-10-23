using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// Sets up Thai / English for the whole game on top of the Unity Localization package.
///
/// How text is keyed: by its SOURCE text (see L10n). Chat nodes, rule entries, scene hints and
/// DialogueLines keep their plain string fields; the key is computed from the string itself, so
/// no existing data had to change type. This tool fills the String Tables with one row per source
/// string, and the runtime looks the source up at the few places text reaches the screen.
///
/// Three steps, kept apart on purpose:
///
///   1. Setup Tables and Translations   assets only - settings, locales, tables, the Thai import,
///                                      the font table. Safe to re-run at any time.
///   2. Create Thai Font                makes a TMP font asset from a .ttf/.otf you drop in, and
///                                      points every font role's Thai column at it.
///   3. Localize Scenes and Prefabs     opens the build scenes and every prefab, adds the font
///                                      swapper to each label, binds static labels to their table
///                                      entry, and builds the two language menus. Asks first.
///
/// Re-running never overwrites a translation: imports only fill EMPTY cells, so anything edited in
/// Window > Asset Management > Localization Tables survives.
/// </summary>
public static class LocalizationSetup
{
    // ─────────────────────────── Paths ───────────────────────────

    private const string Root         = "Assets/Localization";
    private const string TablesDir    = Root + "/Tables";
    private const string LocalesDir   = Root + "/Locales";
    private const string TsvPath      = Root + "/Source/Translations.tsv";
    private const string SettingsPath = Root + "/LocalizationSettings.asset";
    private const string FontsTable   = "Fonts";
    private const string ThaiFontDir  = "Assets/Fonts/Thai";

    // LiberationSans lives in a Resources folder, and Addressables cannot load an asset from there
    // (it fails as "Failed to load sub-asset"). The font table points at a copy instead.
    private const string LiberationSource = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string LiberationCopy   = "Assets/Fonts/Localized/LiberationSans SDF.asset";

    private static readonly string[] LocaleCodes = { L10n.EnglishCode, L10n.ThaiCode };

    /// <summary>
    /// A font role = one English font. Every label gets the role of the font it uses today, and the
    /// Thai column of that role decides what it turns into. Three roles rather than one so a second
    /// Thai font (say, a handwritten one for the typewriter labels) is one cell in the table.
    /// </summary>
    private static readonly (string key, string englishPath)[] FontRoles =
    {
        ("Font/Body",       LiberationCopy),
        ("Font/Typewriter", "Assets/Fonts/SpecialElite-Regular SDF.asset"),
        ("Font/Inter",      "Assets/Fonts/Inter/Inter-VariableFont_opsz,wght SDF.asset"),
    };

    /// <summary>Strings the language menus themselves need. (source, english, thai, context)</summary>
    private static readonly (string src, string en, string th, string ctx)[] BuiltInRows =
    {
        ("Language", "Language", "ภาษา",        "UI/Language menu title"),
        ("Resume",   "Resume",   "กลับเข้าเกม", "UI/Language menu button"),
    };

    private class Row
    {
        public string source, en, th, context, table;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    #region 1. Tables
    // ═════════════════════════════════════════════════════════════════════════════

    [MenuItem("Rule of Horror/Localization/1. Setup Tables and Translations", false, 1)]
    private static void SetupTables()
    {
        var report = new StringBuilder("[Localization] Setup").AppendLine();

        EnsureFolder(TablesDir);
        EnsureFolder(LocalesDir);

        var settings = EnsureSettings(report);
        var locales  = EnsureLocales(report);
        EnsureStartupSelectors(settings, report);
        EnsureAddressablesBuildWithPlayer(report);

        var collections = new Dictionary<string, StringTableCollection>();
        foreach (var name in L10n.TableNames)
            collections[name] = EnsureStringCollection(name, locales);

        // ── gather rows: the import file, the menus, live assets, and what play-testing collected ──
        var rows = new List<Row>();
        rows.AddRange(ReadTsv(report));
        rows.AddRange(BuiltInRows.Select(r => new Row { source = r.src, en = r.en, th = r.th, context = r.ctx, table = "UI" }));
        rows.AddRange(HarvestAssets());
        rows.AddRange(ReadMissingLog(report));
        rows.RemoveAll(r => string.IsNullOrWhiteSpace(r.source));

        int newKeys = 0, filledEn = 0, filledTh = 0;
        foreach (var row in rows)
        {
            var collection = collections[row.table];
            string key = L10n.KeyFor(row.source);

            var shared = collection.SharedData;
            var entry  = shared.GetEntry(key);
            if (entry == null)
            {
                entry = shared.AddKey(key);
                newKeys++;
            }

            if (!string.IsNullOrEmpty(row.context) && !entry.Metadata.HasMetadata<Comment>())
                entry.Metadata.AddMetadata(new Comment { CommentText = row.context });

            if (Fill(collection, L10n.EnglishCode, key, row.en)) filledEn++;
            if (Fill(collection, L10n.ThaiCode,    key, row.th)) filledTh++;
        }

        foreach (var collection in collections.Values) MarkModified(collection);

        report.AppendLine($"  - {rows.Count} source strings: {newKeys} new keys, " +
                          $"{filledEn} English and {filledTh} Thai cells filled (existing cells were left alone)");

        EnsureFontTable(locales, overwriteThai: false, report);

        AssetDatabase.SaveAssets();

        report.AppendLine();
        report.AppendLine(GapReport());
        report.AppendLine();
        report.AppendLine("  Next: 2. Create Thai Font (needs your .ttf in " + ThaiFontDir + "), " +
                          "then 3. Localize Scenes and Prefabs");
        Debug.Log(report.ToString());
    }

    private static LocalizationSettings EnsureSettings(StringBuilder report)
    {
        var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings != null) return settings;

        var guids = AssetDatabase.FindAssets("t:LocalizationSettings", new[] { "Assets" });
        if (guids.Length > 0)
        {
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        else
        {
            // Fully qualified: this project has a namespace called ScriptableObject, and inside
            // the global namespace it wins over UnityEngine.ScriptableObject
            settings = UnityEngine.ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            report.AppendLine("  - Created " + SettingsPath);
        }

        LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        return settings;
    }

    private static List<Locale> EnsureLocales(StringBuilder report)
    {
        var result = new List<Locale>();

        foreach (var code in LocaleCodes)
        {
            var locale = LocalizationEditorSettings.GetLocales().FirstOrDefault(l => l.Identifier.Code == code);

            if (locale == null)
            {
                locale = Locale.CreateLocale(code);
                AssetDatabase.CreateAsset(locale, $"{LocalesDir}/{locale.LocaleName}.asset");
                LocalizationEditorSettings.AddLocale(locale);   // also makes it Addressable with the Locale label
                report.AppendLine($"  - Added locale {locale.LocaleName}");
            }

            result.Add(locale);
        }

        return result;
    }

    /// <summary>
    /// Start-up language: the one the player picked last time, otherwise English.
    ///
    /// The Windows language is deliberately NOT consulted. On a Thai Windows it would start the game
    /// in Thai - including on a machine where the Thai font has not been added yet, which renders
    /// every line as boxes. English first, and the TH button is one click away on the title screen.
    /// PlayerPrefLocaleSelector also SAVES the choice whenever the locale changes, which is the
    /// whole of "remember the language".
    /// </summary>
    private static void EnsureStartupSelectors(LocalizationSettings settings, StringBuilder report)
    {
        var selectors = settings.GetStartupLocaleSelectors();
        var others    = selectors.Where(s => !(s is PlayerPrefLocaleSelector) &&
                                             !(s is SystemLocaleSelector) &&
                                             !(s is SpecificLocaleSelector)).ToList();

        var player   = selectors.OfType<PlayerPrefLocaleSelector>().FirstOrDefault() ?? new PlayerPrefLocaleSelector();
        var fallback = selectors.OfType<SpecificLocaleSelector>().FirstOrDefault()   ?? new SpecificLocaleSelector();
        fallback.LocaleId = new LocaleIdentifier(L10n.EnglishCode);

        selectors.Clear();
        selectors.Add(player);
        selectors.AddRange(others);
        selectors.Add(fallback);

        EditorUtility.SetDirty(settings);
        report.AppendLine("  - Start-up language: the player's last choice, otherwise English");
    }

    /// <summary>
    /// Addressables 2.x defaults to NOT building with the player. Left like that, File ▸ Build would
    /// ship a game whose tables were never built: every string falls back to its source and the
    /// Thai button does nothing. Building with the player makes the normal build button enough.
    /// </summary>
    private static void EnsureAddressablesBuildWithPlayer(StringBuilder report)
    {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null) return;

        if (settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
        {
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(settings);
            report.AppendLine("  - Addressables now build with the player (the normal Build button includes the tables)");
        }
    }

    private static StringTableCollection EnsureStringCollection(string name, List<Locale> locales)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(name)
                      ?? LocalizationEditorSettings.CreateStringTableCollection(name, TablesDir);

        foreach (var locale in locales)
            if (collection.GetTable(locale.Identifier) == null)
                collection.AddNewTable(locale.Identifier);

        // Preloaded = loaded with the locale, so L10n.T's synchronous GetTable never has to wait
        foreach (var table in collection.StringTables)
            if (!LocalizationEditorSettings.GetPreloadTableFlag(table))
                LocalizationEditorSettings.SetPreloadTableFlag(table, true);

        return collection;
    }

    /// <summary>Writes a value only into an empty cell. Returns true if it wrote.</summary>
    private static bool Fill(StringTableCollection collection, string code, string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;

        var table = collection.GetTable(new LocaleIdentifier(code)) as StringTable;
        if (table == null) return false;

        var entry = table.GetEntry(key);
        if (entry == null)
        {
            table.AddEntry(key, value);
            EditorUtility.SetDirty(table);
            return true;
        }

        if (!string.IsNullOrEmpty(entry.Value)) return false;

        entry.Value = value;
        EditorUtility.SetDirty(table);
        return true;
    }

    private static void MarkModified(LocalizationTableCollection collection)
    {
        EditorUtility.SetDirty(collection);
        EditorUtility.SetDirty(collection.SharedData);
        foreach (var table in collection.Tables)
            if (table.asset != null) EditorUtility.SetDirty(table.asset);

        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);
    }

    // ── row sources ──

    private static IEnumerable<Row> ReadTsv(StringBuilder report)
    {
        var rows = new List<Row>();
        if (!File.Exists(TsvPath))
        {
            report.AppendLine("  ! " + TsvPath + " is missing - only live assets were imported");
            return rows;
        }

        foreach (var line in File.ReadAllLines(TsvPath, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#') continue;

            var cols = line.Split('\t');
            if (cols.Length < 3) continue;

            string context = cols.Length > 3 ? Unescape(cols[3]) : "";
            rows.Add(new Row
            {
                source  = Unescape(cols[0]),
                en      = Unescape(cols[1]),
                th      = Unescape(cols[2]),
                context = context,
                table   = TableFor(context),
            });
        }

        report.AppendLine($"  - Read {rows.Count} rows from {TsvPath}");
        return rows;
    }

    /// <summary>
    /// Every chat line and rule entry that exists right now, so content written after the import
    /// file still gets a row (with an empty Thai cell, which the gap report then lists).
    /// </summary>
    private static IEnumerable<Row> HarvestAssets()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:ConversationData", new[] { "Assets" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableObject.ConversationData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data.nodes == null) continue;

            for (int i = 0; i < data.nodes.Count; i++)
            {
                var node = data.nodes[i];
                if (node == null) continue;

                yield return SourceRow(node.message, $"Chat/{data.name}#{i}", "Chat");

                if (node.replies == null) continue;
                foreach (var reply in node.replies)
                    if (reply != null) yield return SourceRow(reply.replyText, $"Chat/{data.name}/reply", "Chat");
            }
        }

        foreach (var guid in AssetDatabase.FindAssets("t:RuleEntryData", new[] { "Assets" }))
        {
            var rule = AssetDatabase.LoadAssetAtPath<ScriptableObject.RuleEntryData>(AssetDatabase.GUIDToAssetPath(guid));
            if (rule == null) continue;

            yield return SourceRow(rule.title,   $"Rules/{rule.name}/title",   "Rules");
            yield return SourceRow(rule.summary, $"Rules/{rule.name}/summary", "Rules");

            if (rule.factsIfCooperative != null)
                foreach (var f in rule.factsIfCooperative) yield return SourceRow(f, $"Rules/{rule.name}/fact", "Rules");
            if (rule.factsIfDefiant != null)
                foreach (var f in rule.factsIfDefiant) yield return SourceRow(f, $"Rules/{rule.name}/fact", "Rules");
            if (rule.hands != null)
                foreach (var h in rule.hands) if (h != null) yield return SourceRow(h.action, $"Rules/{rule.name}/action", "Rules");
        }
    }

    /// <summary>Strings the game showed while play-testing that had no row - see L10n.ReportMissing.</summary>
    private static IEnumerable<Row> ReadMissingLog(StringBuilder report)
    {
        var rows = new List<Row>();
        string path = L10n.MissingLogPath;
        if (!File.Exists(path)) return rows;

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Distinct())
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(SourceRow(Unescape(line), "Collected while play-testing", "UI"));
        }

        File.Delete(path);
        report.AppendLine($"  - Added {rows.Count} strings collected while play-testing");
        return rows;
    }

    /// <summary>A row known only by its source: the source fills its own language's column.</summary>
    private static Row SourceRow(string source, string context, string table)
    {
        if (string.IsNullOrWhiteSpace(source)) return new Row { source = "", table = table };

        bool thai = source.Any(c => c >= '฀' && c <= '๿');
        return new Row
        {
            source  = source,
            en      = thai ? "" : source,
            th      = thai ? source : "",
            context = context,
            table   = table,
        };
    }

    private static string TableFor(string context)
    {
        if (context.StartsWith("Chat/"))  return "Chat";
        if (context.StartsWith("Rules/")) return "Rules";
        if (context.StartsWith("Line/"))  return "Dialogue";
        return "UI";
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════════
    #region Fonts
    // ═════════════════════════════════════════════════════════════════════════════

    private static void EnsureFontTable(List<Locale> locales, bool overwriteThai, StringBuilder report)
    {
        EnsureFolder(Path.GetDirectoryName(LiberationCopy).Replace('\\', '/'));
        if (!File.Exists(LiberationCopy) && File.Exists(LiberationSource))
        {
            AssetDatabase.CopyAsset(LiberationSource, LiberationCopy);
            AssetDatabase.ImportAsset(LiberationCopy);
        }

        var collection = LocalizationEditorSettings.GetAssetTableCollection(FontsTable)
                      ?? LocalizationEditorSettings.CreateAssetTableCollection(FontsTable, TablesDir);

        foreach (var locale in locales)
            if (collection.GetTable(locale.Identifier) == null)
                collection.AddNewTable(locale.Identifier);

        foreach (var table in collection.AssetTables)
            if (!LocalizationEditorSettings.GetPreloadTableFlag(table))
                LocalizationEditorSettings.SetPreloadTableFlag(table, true);

        var enTable = collection.GetTable(new LocaleIdentifier(L10n.EnglishCode)) as AssetTable;
        var thTable = collection.GetTable(new LocaleIdentifier(L10n.ThaiCode))    as AssetTable;
        var thai    = FindThaiFont();

        foreach (var (key, englishPath) in FontRoles)
        {
            if (collection.SharedData.GetEntry(key) == null) collection.SharedData.AddKey(key);

            var english = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(englishPath);
            if (english != null && enTable != null && IsEmpty(enTable, key))
                collection.AddAssetToTable(enTable, key, english);   // also makes it Addressable

            if (thai != null && thTable != null && (overwriteThai || IsEmpty(thTable, key)))
                collection.AddAssetToTable(thTable, key, thai);
        }

        MarkModified(collection);

        report.AppendLine(thai != null
            ? $"  - Font table: every role uses '{thai.name}' in Thai"
            : "  ! No Thai font yet - Thai text will show as boxes until you run 2. Create Thai Font");
    }

    private static bool IsEmpty(AssetTable table, string key)
    {
        var entry = table.GetEntry(key);
        return entry == null || string.IsNullOrEmpty(entry.Guid);
    }

    private static TMP_FontAsset FindThaiFont()
    {
        if (!AssetDatabase.IsValidFolder(ThaiFontDir)) return null;

        return AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { ThaiFontDir })
                            .Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g)))
                            .FirstOrDefault(f => f != null);
    }

    /// <summary>
    /// Builds a dynamic, multi-atlas TMP font asset from the Thai .ttf/.otf you put in Assets/Fonts/Thai.
    ///
    /// Dynamic so glyphs are rasterised as they appear (Thai plus every Latin key label), and
    /// multi-atlas so a long chat never runs the atlas out of space. Font features stay on: Thai
    /// stacks vowels and tone marks above and below the consonant, and TMP only positions those
    /// correctly from the font's GPOS mark tables - with them off, marks collide.
    ///
    /// Pick a modern font with real GPOS mark positioning (Sarabun, Noto Sans/Serif Thai, Kanit,
    /// Prompt, Mitr, Pridi, Charm...). Older "PSK" fonts rely on private-use glyph swaps that TMP
    /// does not do, and their tone marks land on top of upper vowels.
    /// </summary>
    [MenuItem("Rule of Horror/Localization/2. Create Thai Font", false, 2)]
    private static void CreateThaiFont()
    {
        EnsureFolder(ThaiFontDir);

        var source = Selection.activeObject as Font;
        if (source == null)
        {
            source = AssetDatabase.FindAssets("t:Font", new[] { ThaiFontDir })
                                  .Select(g => AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(g)))
                                  .FirstOrDefault(f => f != null);
        }

        if (source == null)
        {
            EditorUtility.DisplayDialog("Create Thai Font",
                "Put a Thai .ttf or .otf into " + ThaiFontDir + " (or select one in the Project window) and run this again.\n\n" +
                "Pick one with proper mark positioning - Sarabun, Noto Sans Thai, Kanit, Prompt, Mitr, Pridi or Charm all work.",
                "OK");
            return;
        }

        if (TMP_Settings.instance == null)
        {
            EditorUtility.DisplayDialog("Create Thai Font",
                "TextMesh Pro essential resources are missing. Import them from Window ▸ TextMeshPro first.", "OK");
            return;
        }

        string path = $"{ThaiFontDir}/{source.name} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        TMP_FontAsset font = existing;

        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                 AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            if (font == null)
            {
                Debug.LogError($"[Localization] TMP could not create a font asset from '{source.name}'");
                return;
            }

            font.name = source.name + " SDF";
            AssetDatabase.CreateAsset(font, path);

            // Sub-assets: every atlas texture AND the material. Without the material call the
            // material only exists in memory and anything loading the asset fresh loses it.
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas == null) continue;
                atlas.name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }

            font.material.name        = font.name + " Material";
            font.material.mainTexture = font.atlasTexture;
            AssetDatabase.AddObjectToAsset(font.material, font);
        }

        font.getFontFeatures = true;
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();

        bool hasMaterial = AssetDatabase.LoadAllAssetsAtPath(path).Any(o => o is Material);
        if (!hasMaterial) Debug.LogWarning($"[Localization] '{path}' was saved without its material - delete it and run again");

        var report = new StringBuilder("[Localization] Thai font").AppendLine();
        report.AppendLine($"  - {(existing == null ? "Created" : "Reused")} {path} (dynamic, multi-atlas, font features on)");

        var locales = LocaleCodes.Select(c => LocalizationEditorSettings.GetLocales().FirstOrDefault(l => l.Identifier.Code == c))
                                 .Where(l => l != null).ToList();
        if (locales.Count < 2)
        {
            report.AppendLine("  ! Run 1. Setup Tables and Translations first - there are no locales yet");
        }
        else
        {
            EnsureFontTable(locales, overwriteThai: true, report);
            AssetDatabase.SaveAssets();
        }

        Selection.activeObject = font;
        Debug.Log(report.ToString(), font);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════════
    #region 3. Scenes and prefabs
    // ═════════════════════════════════════════════════════════════════════════════

    [MenuItem("Rule of Horror/Localization/3. Localize Scenes and Prefabs", false, 3)]
    private static void LocalizeScenes()
    {
        if (LocalizationEditorSettings.GetAssetTableCollection(FontsTable) == null)
        {
            EditorUtility.DisplayDialog("Localize Scenes", "Run 1. Setup Tables and Translations first.", "OK");
            return;
        }

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToList();
        // .prefab only - FindAssets("t:Prefab") also returns model files, which cannot be edited
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefab" })
                                   .Select(AssetDatabase.GUIDToAssetPath)
                                   .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                                   .ToList();
        var processed = new HashSet<string>(prefabs);

        if (!EditorUtility.DisplayDialog("Localize Scenes and Prefabs",
                $"This opens and SAVES {scenes.Count} build scenes and up to {prefabs.Count} prefabs:\n\n" +
                "  • every TextMeshPro label gets a font swapper (English font ↔ Thai font)\n" +
                "  • labels whose text never changes get bound to their table row\n" +
                "  • the gameplay scene gets an Esc menu with the language buttons\n" +
                "  • the main menu gets TH / EN buttons in the top-right corner\n\n" +
                "Commit or back up first if you want an easy way back. Save open scenes now?",
                "Do it", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var setup  = EditorSceneManager.GetSceneManagerSetup();
        var report = new StringBuilder("[Localization] Scenes and prefabs").AppendLine();
        var unbound = new List<string>();

        try
        {
            int prefabFonts = 0, prefabLabels = 0, prefabCount = 0;
            for (int i = 0; i < prefabs.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Localize prefabs", prefabs[i], (float)i / prefabs.Count);

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabs[i]);
                    var (fonts, labels) = LocalizeHierarchy(root.GetComponentsInChildren<TMP_Text>(true),
                                                            root.GetComponentsInChildren<MonoBehaviour>(true),
                                                            prefabs[i], processed, unbound);
                    if (fonts + labels > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabs[i]);
                        prefabCount++;
                    }

                    prefabFonts  += fonts;
                    prefabLabels += labels;
                }
                catch (Exception e)
                {
                    // One prefab with a missing script must not stop the other few hundred
                    report.AppendLine($"  ! Skipped {prefabs[i]}: {e.Message}");
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }
            report.AppendLine($"  - Prefabs: {prefabCount} changed, {prefabFonts} font swappers, {prefabLabels} labels bound");

            foreach (var path in scenes)
            {
                EditorUtility.DisplayProgressBar("Localize scenes", path, 1f);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                var texts     = FindInScene<TMP_Text>(scene);
                var behaviors = FindInScene<MonoBehaviour>(scene);
                var (fonts, labels) = LocalizeHierarchy(texts, behaviors, path, processed, unbound);
                report.AppendLine($"  - {Path.GetFileName(path)}: {fonts} font swappers, {labels} labels bound");

                if (FindInScene<CutsceneManager>(scene).Length > 0) BuildInGameMenu(scene, report);
                if (FindInScene<MainMenuManager>(scene).Length > 0) BuildMainMenuSwitcher(scene, report);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        if (unbound.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"  Labels left as they are ({unbound.Count}) - text set by a script, a number/placeholder, " +
                              "or text with no table row. Script-driven ones are translated by their script:");
            foreach (var u in unbound.Take(60)) report.AppendLine("    " + u);
            if (unbound.Count > 60) report.AppendLine($"    … and {unbound.Count - 60} more");
        }

        Debug.Log(report.ToString());
    }

    private static T[] FindInScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

    /// <summary>
    /// Font swappers on every label using one of the three English fonts, and a table binding on
    /// labels whose text is fixed. Returns (font swappers added, labels bound).
    /// </summary>
    private static (int fonts, int labels) LocalizeHierarchy(IEnumerable<TMP_Text> texts,
                                                             IEnumerable<MonoBehaviour> behaviours,
                                                             string where, HashSet<string> processedPrefabs,
                                                             List<string> unbound)
    {
        var roleByFontName = FontRoles.ToDictionary(r => Path.GetFileNameWithoutExtension(r.englishPath), r => r.key);
        var scriptDriven   = ReferencedByScripts(behaviours);
        int fonts = 0, labels = 0;

        foreach (var text in texts)
        {
            if (text == null) continue;

            // A label that comes from a prefab this tool also processes gets its components there.
            // Adding them here too would stack a second copy on top as an instance override.
            if (PrefabUtility.IsPartOfPrefabInstance(text))
            {
                var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(text);
                string source = original != null ? AssetDatabase.GetAssetPath(original) : null;
                if (source != null && processedPrefabs.Contains(source)) continue;
            }

            // ── font ──
            if (text.font != null && roleByFontName.TryGetValue(text.font.name, out var role) &&
                text.GetComponent<LocalizedFontAsset>() == null)
            {
                var swapper = text.gameObject.AddComponent<LocalizedFontAsset>();
                swapper.AssetReference.SetReference(FontsTable, role);
                EditorUtility.SetDirty(swapper);
                fonts++;
            }

            // ── fixed text ──
            if (text.GetComponent<LocalizeStringEvent>() != null) continue;

            string current = text.text;
            if (string.IsNullOrWhiteSpace(current)) continue;

            if (scriptDriven.Contains(text))
            {
                unbound.Add($"{where} :: {text.name} :: \"{Short(current)}\" (set by a script)");
                continue;
            }

            string table = FindTableWithKey(L10n.KeyFor(current));
            if (table == null)
            {
                unbound.Add($"{where} :: {text.name} :: \"{Short(current)}\"");
                continue;
            }

            Bind(text, table, L10n.KeyFor(current));
            labels++;
        }

        return (fonts, labels);
    }

    /// <summary>
    /// Every label some script holds a serialized reference to. Those labels are
    /// written by that script at runtime (chat bubbles, clock, rule book), and a table binding on
    /// them would fight it - the script translates them itself through L10n.
    /// </summary>
    private static HashSet<Object> ReferencedByScripts(IEnumerable<MonoBehaviour> behaviours)
    {
        var set = new HashSet<Object>();

        foreach (var b in behaviours)
        {
            if (b == null || b is TMP_Text || b is LocalizeStringEvent || b is LocalizedFontAsset ||
                b is UnityEngine.UI.Graphic || b is UnityEngine.UI.LayoutGroup || b is UnityEngine.UI.ContentSizeFitter ||
                b is UnityEngine.UI.Selectable || b is EventSystem || b is BaseInputModule)
                continue;

            var  so    = new SerializedObject(b);
            var  prop  = so.GetIterator();
            bool enter = true;

            while (prop.Next(enter))
            {
                enter = true;

                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var value = prop.objectReferenceValue;
                    // Only the text component itself: a script that holds the GameObject is
                    // toggling it on and off (skip prompts, hint panels), not writing its text
                    if (value is TMP_Text) set.Add(value);
                    continue;
                }

                // Strings are char arrays underneath, and the road/terrain tools in the gameplay
                // scene carry arrays of hundreds of thousands of floats - walking into those turns
                // this from a second into minutes, and they can never hold a label reference
                if (prop.propertyType == SerializedPropertyType.String) { enter = false; continue; }

                if (prop.isArray && prop.propertyType == SerializedPropertyType.Generic)
                {
                    string element = prop.arrayElementType;
                    if (!element.StartsWith("PPtr<") && (PrimitiveElements.Contains(element) || prop.arraySize > 512))
                        enter = false;
                }
            }
        }

        return set;
    }

    private static readonly HashSet<string> PrimitiveElements = new HashSet<string>
    {
        "float", "double", "int", "unsigned int", "SInt8", "UInt8", "SInt16", "UInt16", "SInt64", "UInt64",
        "bool", "char", "string", "Vector2", "Vector3", "Vector4", "Vector2f", "Vector3f", "Vector4f",
        "Quaternionf", "Quaternion", "ColorRGBA", "Color", "Matrix4x4f", "Rectf", "Keyframe",
    };

    private static string FindTableWithKey(string key)
    {
        foreach (var name in L10n.TableNames)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(name);
            if (collection != null && collection.SharedData.GetEntry(key) != null) return name;
        }
        return null;
    }

    /// <summary>
    /// LocalizeStringEvent wired through the public UnityEventTools API: a persistent call to the
    /// label's text setter, set to EditorAndRuntime so switching language in the Editor updates
    /// the label too (AddPersistentListener alone leaves it RuntimeOnly).
    /// </summary>
    private static void Bind(TMP_Text text, string table, string key)
    {
        var lse = text.gameObject.AddComponent<LocalizeStringEvent>();
        lse.StringReference = new LocalizedString(table, key);

        var setText = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), text, "set_text");
        for (int i = lse.OnUpdateString.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(lse.OnUpdateString, i);

        UnityEventTools.AddPersistentListener(lse.OnUpdateString, setText);
        lse.OnUpdateString.SetPersistentListenerState(lse.OnUpdateString.GetPersistentEventCount() - 1,
                                                      UnityEventCallState.EditorAndRuntime);
        EditorUtility.SetDirty(lse);
    }

    // ── language menus ──

    private static void BuildInGameMenu(Scene scene, StringBuilder report)
    {
        if (FindInScene<InGameLanguageMenu>(scene).Length > 0)
        {
            report.AppendLine("  - Esc language menu already in " + scene.name);
            return;
        }

        var root = new GameObject("Language Menu", typeof(RectTransform), typeof(Canvas),
                                  typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Language Menu");

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;   // above every HUD canvas, including the death screens

        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        var panel = MakeRect("Panel", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bg    = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color  = new Color(0f, 0f, 0f, 0.85f);

        var title = MakeLabel("Title", panel, "Language", 64, new Vector2(0.5f, 0.66f), new Vector2(600, 100));
        BindIfKnown(title);

        var row = MakeRect("Languages", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 120));
        var thai    = MakeButton("Thai",    row, "TH", new Vector2(-130, 0));
        var english = MakeButton("English", row, "EN", new Vector2(130, 0));

        var switcher = row.gameObject.AddComponent<LanguageSwitcher>();
        var so = new SerializedObject(switcher);
        so.FindProperty("thaiButton").objectReferenceValue    = thai;
        so.FindProperty("englishButton").objectReferenceValue = english;
        so.ApplyModifiedPropertiesWithoutUndo();

        var resume = MakeButton("Resume", panel, "Resume", Vector2.zero, new Vector2(0.5f, 0.3f), new Vector2(360, 90));
        BindIfKnown(resume.GetComponentInChildren<TMP_Text>());

        var menu = root.AddComponent<InGameLanguageMenu>();
        var mso  = new SerializedObject(menu);
        mso.FindProperty("panel").objectReferenceValue        = panel.gameObject;
        mso.FindProperty("resumeButton").objectReferenceValue = resume;
        mso.FindProperty("inputHandler").objectReferenceValue = FindInScene<InputSystem.InputHandler>(scene).FirstOrDefault();
        mso.ApplyModifiedPropertiesWithoutUndo();

        panel.gameObject.SetActive(false);
        report.AppendLine("  - Built the Esc language menu in " + scene.name + " (restyle it freely - only the references matter)");
    }

    private static void BuildMainMenuSwitcher(Scene scene, StringBuilder report)
    {
        if (FindInScene<LanguageSwitcher>(scene).Length > 0)
        {
            report.AppendLine("  - Language buttons already in " + scene.name);
            return;
        }

        var manager = FindInScene<MainMenuManager>(scene).First();
        var start   = new SerializedObject(manager).FindProperty("startButton").objectReferenceValue as Component;
        var canvas  = start != null ? start.GetComponentInParent<Canvas>() : FindInScene<Canvas>(scene).FirstOrDefault();
        if (canvas == null)
        {
            report.AppendLine("  ! No canvas in " + scene.name + " - main menu language buttons not added");
            return;
        }

        // Top-right corner: the one part of a title screen that is reliably empty
        var row = MakeRect("Language Switcher", canvas.transform, Vector2.one, Vector2.one, new Vector2(-170, -70), new Vector2(300, 90));
        var thai    = MakeButton("Thai",    row, "TH", new Vector2(-75, 0), size: new Vector2(130, 80));
        var english = MakeButton("English", row, "EN", new Vector2(75, 0),  size: new Vector2(130, 80));

        var switcher = row.gameObject.AddComponent<LanguageSwitcher>();
        var so = new SerializedObject(switcher);
        so.FindProperty("thaiButton").objectReferenceValue    = thai;
        so.FindProperty("englishButton").objectReferenceValue = english;
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(row.gameObject, "Language Switcher");
        report.AppendLine("  - Added TH / EN buttons to the top-right of " + scene.name);
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                          Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.anchoredPosition = position;
        rt.sizeDelta        = size;
        return rt;
    }

    private static TMP_Text MakeLabel(string name, Transform parent, string text, float size, Vector2 anchor, Vector2 box)
    {
        var rt  = MakeRect(name, parent, anchor, anchor, Vector2.zero, box);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.85f, 0.82f, 0.78f);
        AddFontSwapper(tmp);
        return tmp;
    }

    private static UnityEngine.UI.Button MakeButton(string name, Transform parent, string label, Vector2 position,
                                                    Vector2? anchor = null, Vector2? size = null)
    {
        var a  = anchor ?? new Vector2(0.5f, 0.5f);
        var rt = MakeRect(name, parent, a, a, position, size ?? new Vector2(220, 100));

        var image   = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(1f, 1f, 1f, 0.08f);
        var button  = rt.gameObject.AddComponent<UnityEngine.UI.Button>();

        var text = MakeLabel("Label", rt, label, 48, new Vector2(0.5f, 0.5f), rt.sizeDelta);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        return button;
    }

    private static void AddFontSwapper(TMP_Text text)
    {
        var swapper = text.gameObject.AddComponent<LocalizedFontAsset>();
        swapper.AssetReference.SetReference(FontsTable, "Font/Body");
    }

    private static void BindIfKnown(TMP_Text text)
    {
        if (text == null) return;
        string key = L10n.KeyFor(text.text);
        string table = FindTableWithKey(key);
        if (table != null) Bind(text, table, key);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════════
    #region Checks and Thai word breaks
    // ═════════════════════════════════════════════════════════════════════════════

    [MenuItem("Rule of Horror/Localization/Check Missing Translations", false, 20)]
    private static void CheckGaps() => Debug.Log("[Localization]\n" + GapReport());

    /// <summary>
    /// Every key without a value in every language. A gap is not silent in game - the source
    /// shows instead - but it is easy to ship, so it is listed rather than guessed at.
    /// </summary>
    private static string GapReport()
    {
        var gaps = new List<string>();
        int checkedCount = 0;

        foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
        {
            foreach (var key in collection.SharedData.Entries)
            {
                foreach (var table in collection.StringTables)
                {
                    checkedCount++;
                    var entry = table.GetEntry(key.Id);
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                        gaps.Add($"{collection.TableCollectionName} / {table.LocaleIdentifier.Code} / {key.Key}");
                }
            }
        }

        var fonts = LocalizationEditorSettings.GetAssetTableCollection(FontsTable);
        if (fonts != null)
        {
            foreach (var key in fonts.SharedData.Entries)
                foreach (var table in fonts.AssetTables)
                {
                    checkedCount++;
                    var entry = table.GetEntry(key.Id);
                    if (entry == null || string.IsNullOrEmpty(entry.Guid))
                        gaps.Add($"{FontsTable} / {table.LocaleIdentifier.Code} / {key.Key}");
                }
        }

        if (checkedCount == 0)
            return "  INCONCLUSIVE: no table entries found - run 1. Setup Tables and Translations first";

        if (gaps.Count == 0) return $"  COMPLETE: {checkedCount} cells checked, no gaps";

        var sb = new StringBuilder($"  GAPS ({gaps.Count} of {checkedCount} cells):");
        foreach (var g in gaps.Take(80)) sb.Append("\n    ").Append(g);
        if (gaps.Count > 80) sb.Append($"\n    … and {gaps.Count - 80} more");
        return sb.ToString();
    }

    /// <summary>
    /// Re-inserts the invisible word breaks (U+200B) into every Thai cell.
    ///
    /// Thai has no spaces between words and TextMeshPro only wraps at whitespace or U+200B, so a
    /// Thai line with no breaks wraps mid-word - and can split a tone mark from its consonant.
    /// The imported translations already carry the breaks; run this after editing Thai by hand.
    /// It uses the Windows Thai word segmenter (via PowerShell), so it is Windows-only.
    /// </summary>
    [MenuItem("Rule of Horror/Localization/Insert Thai Word Breaks", false, 21)]
    private static void InsertThaiWordBreaks()
    {
        var cells = new List<(StringTable table, StringTableEntry entry)>();
        foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
        {
            var table = collection.GetTable(new LocaleIdentifier(L10n.ThaiCode)) as StringTable;
            if (table == null) continue;

            foreach (var entry in table.Values)
                if (!string.IsNullOrEmpty(entry.Value) && entry.Value.Any(c => c >= '฀' && c <= '๿'))
                    cells.Add((table, entry));
        }

        if (cells.Count == 0)
        {
            Debug.Log("[Localization] No Thai text to segment");
            return;
        }

        string temp   = Path.Combine(Path.GetTempPath(), "rule-of-horror-l10n");
        Directory.CreateDirectory(temp);
        string script = Path.Combine(temp, "segment.ps1");
        string input  = Path.Combine(temp, "in.json");
        string output = Path.Combine(temp, "out.json");

        File.WriteAllText(script, SegmenterScript, new UTF8Encoding(true));
        File.WriteAllText(input, JsonArray(cells.Select(c => c.entry.Value)), new UTF8Encoding(false));
        if (File.Exists(output)) File.Delete(output);

        var process = Process.Start(new ProcessStartInfo("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" \"{input}\" \"{output}\"")
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardError  = true,
        });

        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!File.Exists(output))
        {
            Debug.LogError("[Localization] Thai segmentation failed (needs Windows 10/11 PowerShell):\n" + errors);
            return;
        }

        var results = ParseJsonArray(File.ReadAllText(output, Encoding.UTF8));
        if (results.Count != cells.Count)
        {
            Debug.LogError($"[Localization] Segmenter returned {results.Count} strings for {cells.Count} cells - nothing changed");
            return;
        }

        int changed = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            // Never change the words themselves - only the invisible breaks between them
            if (results[i].Replace("​", "") != cells[i].entry.Value.Replace("​", "")) continue;
            if (results[i] == cells[i].entry.Value) continue;

            cells[i].entry.Value = results[i];
            EditorUtility.SetDirty(cells[i].table);
            changed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] Thai word breaks updated in {changed} of {cells.Count} cells");
    }

    private const string SegmenterScript = @"param([string]$InPath, [string]$OutPath)
$null = [Windows.Data.Text.WordsSegmenter, Windows.Data.Text, ContentType = WindowsRuntime]
$seg  = [Windows.Data.Text.WordsSegmenter]::new('th-TH')
$zwsp = [char]0x200B
function IsThai([char]$c) { return ([int]$c -ge 0x0E00 -and [int]$c -le 0x0E7F) }
$items = Get-Content -Raw -Encoding UTF8 $InPath | ConvertFrom-Json
$out = New-Object System.Collections.Generic.List[string]
foreach ($text in $items) {
    $clean  = $text.Replace([string]$zwsp, '')
    $tokens = @($seg.GetTokens($clean) | ForEach-Object { $_ })
    $breaks = New-Object System.Collections.Generic.HashSet[int]
    for ($i = 0; $i -lt $tokens.Length - 1; $i++) {
        $a = $tokens[$i].SourceTextSegment; $b = $tokens[$i + 1].SourceTextSegment
        $end = [int]$a.StartPosition + [int]$a.Length
        if ($end -eq [int]$b.StartPosition -and $end -gt 0 -and $end -lt $clean.Length) {
            $next = [int]$clean[$end]
            if ((IsThai $clean[$end - 1]) -and (IsThai $clean[$end]) -and $next -ne 0x0E46 -and $next -ne 0x0E2F) { [void]$breaks.Add($end) }
        }
    }
    $sb = New-Object System.Text.StringBuilder
    for ($k = 0; $k -lt $clean.Length; $k++) { if ($breaks.Contains($k)) { [void]$sb.Append($zwsp) }; [void]$sb.Append($clean[$k]) }
    $out.Add($sb.ToString())
}
[System.IO.File]::WriteAllText($OutPath, (ConvertTo-Json -InputObject $out.ToArray() -Compress), (New-Object System.Text.UTF8Encoding($false)))
";

    #endregion

    // ═════════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }

            char n = s[++i];
            switch (n)
            {
                case 'n':  sb.Append('\n'); break;
                case 't':  sb.Append('\t'); break;
                case 'r':  sb.Append('\r'); break;
                case '\\': sb.Append('\\'); break;
                default:   sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    private static string Short(string s)
    {
        s = s.Replace("\n", " ");
        return s.Length > 50 ? s.Substring(0, 50) + "…" : s;
    }

    private static string JsonArray(IEnumerable<string> items)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var item in items)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"');
            foreach (char c in item)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Minimal parser for a flat JSON array of strings (what ConvertTo-Json writes).</summary>
    private static List<string> ParseJsonArray(string json)
    {
        var list = new List<string>();
        int i = json.IndexOf('[');
        if (i < 0) return list;

        while (++i < json.Length)
        {
            char c = json[i];
            if (c == ']') break;
            if (c != '"') continue;

            var sb = new StringBuilder();
            while (++i < json.Length && json[i] != '"')
            {
                if (json[i] != '\\') { sb.Append(json[i]); continue; }

                char n = json[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16)); i += 4; break;
                    default:  sb.Append(n); break;
                }
            }
            list.Add(sb.ToString());
        }
        return list;
    }

    #endregion
}
