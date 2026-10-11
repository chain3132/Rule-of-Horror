using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rule5.EditorTools
{
    /// <summary>
    /// Makes a build meant purely for playtesting Rule 5 — same approach as Rule4TestBuild.
    ///
    /// Rule5DevSkip is wrapped in #if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE5_TEST_BUILD,
    /// so a normal build does not contain that code and the shortcut keys do nothing.
    /// This menu item adds the RULE5_TEST_BUILD define just for the build and removes it in
    /// a finally block, so the define cannot stick around if the build fails or is cancelled.
    /// </summary>
    public static class Rule5TestBuild
    {
        private const string Define      = "RULE5_TEST_BUILD";
        private const string ExeName     = "RuleOfHorror_Rule5Test.exe";
        private const string PrefsKeyDir = "Rule5TestBuild.LastDir";

        [MenuItem("Rule of Horror/Build Rule 5 Test Build (Windows x64)", false, 1)]
        public static void BuildWindows64()
        {
            string[] scenes = EditorBuildSettings.scenes
                                                 .Where(s => s.enabled)
                                                 .Select(s => s.path)
                                                 .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Cannot Build",
                    "No scene is enabled in Build Settings.", "OK");
                return;
            }

            string lastDir = EditorPrefs.GetString(PrefsKeyDir, "");
            string dir     = EditorUtility.SaveFolderPanel("Choose a folder for the build", lastDir, "");
            if (string.IsNullOrEmpty(dir)) return;

            EditorPrefs.SetString(PrefsKeyDir, dir);

            var target   = NamedBuildTarget.Standalone;
            var original = PlayerSettings.GetScriptingDefineSymbols(target);

            try
            {
                PlayerSettings.SetScriptingDefineSymbols(target, WithDefine(original, Define));

                var options = new BuildPlayerOptions
                {
                    scenes           = scenes,
                    locationPathName = Path.Combine(dir, ExeName),
                    target           = BuildTarget.StandaloneWindows64,
                    targetGroup      = BuildTargetGroup.Standalone,

                    // Development so the build writes Player.log, which keeps the Debug.Log
                    // output a tester can send back
                    options          = BuildOptions.Development
                };

                Report(BuildPipeline.BuildPlayer(options), dir);
            }
            finally
            {
                // Always restore, otherwise the define sticks and a real release build
                // ships with the cheat keys in it
                PlayerSettings.SetScriptingDefineSymbols(target, original);
            }
        }

        private static string WithDefine(string defines, string add)
        {
            var list = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!list.Contains(add)) list.Add(add);
            return string.Join(";", list);
        }

        private static void Report(BuildReport report, string dir)
        {
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Rule5TestBuild] Build failed: {summary.result} " +
                               $"({summary.totalErrors} error(s)) — see the Console for details");
                return;
            }

            Debug.Log($"[Rule5TestBuild] Build finished ({summary.totalSize / (1024UL * 1024UL)} MB) → {dir}");

            if (EditorUtility.DisplayDialog("Build Finished",
                    $"The build is at:\n{dir}\n\n" +
                    "Tell the tester:\n" +
                    "  • F6  = skip straight into Rule 5\n" +
                    "  • F7  = markers on the thread grab point and the ghost\n" +
                    "  • F8  = ring the bell once more\n" +
                    "  • F9  = toggle the chime sound\n" +
                    "  • F10 = teleport to the thread grab point\n" +
                    "  • The top-left corner already shows the rule status",
                    "Open Folder", "Close"))
            {
                EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
            }
        }
    }
}
