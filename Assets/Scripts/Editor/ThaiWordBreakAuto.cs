using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// Inserts the Thai word breaks (U+200B) by itself, so a Thai cell edited by hand can't reach the
/// game without them.
///
/// TextMeshPro only wraps at whitespace or U+200B. Thai has no spaces, so a cell typed in the
/// Localization Tables window without breaks is one giant "word": TMP force-breaks it at whatever
/// letter hits the edge ("ขึ้" | "นมา") and the vowels / tone marks on that letter hang past the
/// bubble. The menu item fixes it, but it is easy to forget after every edit.
///
/// Runs on the cells that have no break at all (cheap check, PowerShell only launches when one
/// exists):
///   - before entering Play mode
///   - before every player build - ahead of the Addressables build that packs the string tables
/// </summary>
[InitializeOnLoad]
internal static class ThaiWordBreakAuto
{
    static ThaiWordBreakAuto()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
            LocalizationSetup.AutoInsertThaiWordBreaks();
    }
}

internal sealed class ThaiWordBreakBeforeBuild : IPreprocessBuildWithReport
{
    // Addressables builds the content (string tables included) in its own pre-build step -
    // the tables must already be fixed by then
    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report) => LocalizationSetup.AutoInsertThaiWordBreaks();
}
