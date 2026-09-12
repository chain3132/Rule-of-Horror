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
    /// สร้างไฟล์ build สำหรับ "ให้เพื่อนเทส Rule 4 โดยเฉพาะ"
    ///
    /// ปัญหาเดิม: Rule4DevSkip ครอบด้วย #if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// ไฟล์ build ปกติจึงไม่มีโค้ดส่วนนั้นอยู่เลย กด F4 ไปก็ไม่มีอะไรเกิดขึ้น
    ///
    /// เมนูนี้จะใส่ define RULE4_TEST_BUILD ให้ชั่วคราวก่อน build แล้วถอดคืนทันทีที่เสร็จ
    /// (ถอดใน finally ด้วย ต่อให้ build ล้มหรือถูกยกเลิกกลางคัน define ก็ไม่ค้าง)
    ///
    /// ผลลัพธ์: ไฟล์ build ที่กด F4 ข้ามเข้า Rule 4 ได้ + มี HUD บอกปุ่มบนหน้าจอ
    /// ส่วนไฟล์ build ที่ส่งจริงให้ build ด้วยปุ่มปกติใน Build Settings เหมือนเดิม ปุ่มลัดจะหายไปเอง
    /// </summary>
    public static class Rule4TestBuild
    {
        private const string Define      = "RULE4_TEST_BUILD";
        private const string ExeName     = "RuleOfHorror_Rule4Test.exe";
        private const string PrefsKeyDir = "Rule4TestBuild.LastDir";

        [MenuItem("Rule of Horror/Build ไฟล์เทส Rule 4 (Windows x64)", false, 0)]
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

                    // Development เพื่อให้มี Player.log เก็บ Debug.Log ไว้ให้เพื่อนส่งกลับมาได้
                    // (อยู่ที่ %USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log)
                    options          = BuildOptions.Development
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                Report(report, dir);
            }
            finally
            {
                // ต้องคืนค่าเสมอ ไม่งั้น define ค้างแล้วไฟล์ build ที่ส่งจริงจะมีปุ่มโกงติดไปด้วย
                PlayerSettings.SetScriptingDefineSymbols(target, original);
            }
        }

        [MenuItem("Rule of Horror/เปิดโฟลเดอร์ Player.log ของเครื่องนี้", false, 20)]
        public static void OpenLogFolder()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", PlayerSettings.companyName, PlayerSettings.productName);

            if (Directory.Exists(path)) EditorUtility.RevealInFinder(path);
            else EditorUtility.DisplayDialog("ยังไม่มีโฟลเดอร์",
                     $"ยังไม่เคยรันไฟล์ build บนเครื่องนี้\n\n{path}", "ตกลง");
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
                Debug.Log($"[Rule4TestBuild] build เสร็จแล้ว ({summary.totalSize / (1024UL * 1024UL)} MB) → {dir}");

                if (EditorUtility.DisplayDialog("Build เสร็จแล้ว",
                        $"ไฟล์อยู่ที่:\n{dir}\n\n" +
                        "บอกเพื่อนว่า:\n" +
                        "  • กด F4 = ข้ามเข้า Rule 4 ทันที\n" +
                        "  • กด F5 = โชว์ป้ายชี้ตำแหน่งตุ๊กตา/ผี\n" +
                        "  • มุมซ้ายบนมีข้อความบอกปุ่มอยู่แล้ว",
                        "เปิดโฟลเดอร์", "ปิด"))
                {
                    EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
                }
            }
            else
            {
                Debug.LogError($"[Rule4TestBuild] build ไม่สำเร็จ: {summary.result} " +
                               $"(error {summary.totalErrors}) — ดูรายละเอียดใน Console");
            }
        }
    }
}
