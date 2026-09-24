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
    /// สร้างไฟล์ build สำหรับ "ให้เพื่อนเทส Rule 5 โดยเฉพาะ" — วิธีเดียวกับ Rule4TestBuild
    ///
    /// Rule5DevSkip ครอบด้วย #if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE5_TEST_BUILD
    /// ไฟล์ build ปกติจึงไม่มีโค้ดส่วนนั้น กดปุ่มลัดไปก็ไม่มีอะไรเกิดขึ้น
    /// เมนูนี้ใส่ define RULE5_TEST_BUILD ให้ชั่วคราวก่อน build แล้วถอดคืนใน finally
    /// (ถอดใน finally ด้วย ต่อให้ build ล้มหรือถูกยกเลิกกลางคัน define ก็ไม่ค้าง)
    /// </summary>
    public static class Rule5TestBuild
    {
        private const string Define      = "RULE5_TEST_BUILD";
        private const string ExeName     = "RuleOfHorror_Rule5Test.exe";
        private const string PrefsKeyDir = "Rule5TestBuild.LastDir";

        [MenuItem("Rule of Horror/Build ไฟล์เทส Rule 5 (Windows x64)", false, 1)]
        public static void BuildWindows64()
        {
            string[] scenes = EditorBuildSettings.scenes
                                                 .Where(s => s.enabled)
                                                 .Select(s => s.path)
                                                 .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Build ไม่ได้",
                    "ยังไม่มี scene ที่เปิดใช้งานใน Build Settings", "ตกลง");
                return;
            }

            string lastDir = EditorPrefs.GetString(PrefsKeyDir, "");
            string dir     = EditorUtility.SaveFolderPanel("เลือกโฟลเดอร์ที่จะวางไฟล์ build", lastDir, "");
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

                    // Development เพื่อให้มี Player.log เก็บ Debug.Log ไว้ให้เพื่อนส่งกลับมาได้
                    options          = BuildOptions.Development
                };

                Report(BuildPipeline.BuildPlayer(options), dir);
            }
            finally
            {
                // ต้องคืนค่าเสมอ ไม่งั้น define ค้างแล้วไฟล์ build ที่ส่งจริงจะมีปุ่มโกงติดไปด้วย
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
                Debug.LogError($"[Rule5TestBuild] build ไม่สำเร็จ: {summary.result} " +
                               $"(error {summary.totalErrors}) — ดูรายละเอียดใน Console");
                return;
            }

            Debug.Log($"[Rule5TestBuild] build เสร็จแล้ว ({summary.totalSize / (1024UL * 1024UL)} MB) → {dir}");

            if (EditorUtility.DisplayDialog("Build เสร็จแล้ว",
                    $"ไฟล์อยู่ที่:\n{dir}\n\n" +
                    "บอกเพื่อนว่า:\n" +
                    "  • F6  = ข้ามเข้า Rule 5 ทันที\n" +
                    "  • F7  = ป้ายชี้จุดจับสาย / ผี\n" +
                    "  • F8  = ระฆังดัง +1\n" +
                    "  • F9  = เปิด/ปิดเสียงฉิ่ง\n" +
                    "  • F10 = วาร์ปไปจุดจับสาย\n" +
                    "  • มุมซ้ายบนมีสถานะกฎบอกอยู่แล้ว",
                    "เปิดโฟลเดอร์", "ปิด"))
            {
                EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
            }
        }
    }
}
