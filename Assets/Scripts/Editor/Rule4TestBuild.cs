using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rule4.EditorTools
{
    /// <summary>
    /// Makes a build meant purely for playtesting Rule 4.
    ///
    /// The problem this solves: Rule4DevSkip is wrapped in
    /// #if UNITY_EDITOR || DEVELOPMENT_BUILD, so a normal build does not contain that code
    /// at all and pressing F4 does nothing.
    ///
    /// This menu item adds the RULE4_TEST_BUILD define just for the build, then takes it
    /// straight back out. The removal lives in a finally block so the define cannot stick
    /// around if the build fails or is cancelled halfway.
    ///
    /// Result: a build where F4 skips into Rule 4 and an on-screen HUD lists the keys.
    /// Builds meant for real players still go through the normal Build Settings button,
    /// where the cheat keys compile out by themselves.
    /// </summary>
    public static class Rule4TestBuild
    {
        private const string Define      = "RULE4_TEST_BUILD";
        private const string ExeName     = "RuleOfHorror_Rule4Test.exe";
        private const string PrefsKeyDir = "Rule4TestBuild.LastDir";

        [MenuItem("Rule of Horror/Build Rule 4 Test Build (Windows x64)", false, 0)]
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

            var target    = NamedBuildTarget.Standalone;
            var original  = PlayerSettings.GetScriptingDefineSymbols(target);

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
                    // (at %USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log)
                    options          = BuildOptions.Development
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                Report(report, dir);
            }
            finally
            {
                // Always restore, otherwise the define sticks and a real release build
                // ships with the cheat keys in it
                PlayerSettings.SetScriptingDefineSymbols(target, original);
            }
        }

        [MenuItem("Rule of Horror/Open Player.log Folder For This Machine", false, 20)]
        public static void OpenLogFolder()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", PlayerSettings.companyName, PlayerSettings.productName);

            if (Directory.Exists(path)) EditorUtility.RevealInFinder(path);
            else EditorUtility.DisplayDialog("Folder Does Not Exist Yet",
                     $"No build has been run on this machine yet.\n\n{path}", "OK");
        }

        // ─────────────────────────── helpers ───────────────────────────

        private static string WithDefine(string defines, string add)
        {
            var list = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!list.Contains(add)) list.Add(add);
            return string.Join(";", list);
        }

        private static void Report(BuildReport report, string dir)
        {
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Rule4TestBuild] Build finished ({summary.totalSize / (1024UL * 1024UL)} MB) → {dir}");

                if (EditorUtility.DisplayDialog("Build Finished",
                        $"The build is at:\n{dir}\n\n" +
                        "Tell the tester:\n" +
                        "  • F4 = skip straight into Rule 4\n" +
                        "  • F5 = show markers on the doll and the ghost\n" +
                        "  • The top-left corner already lists the keys",
                        "Open Folder", "Close"))
                {
                    EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
                }
            }
            else
            {
                Debug.LogError($"[Rule4TestBuild] Build failed: {summary.result} " +
                               $"({summary.totalErrors} error(s)) — see the Console for details");
            }
        }
    }
}
