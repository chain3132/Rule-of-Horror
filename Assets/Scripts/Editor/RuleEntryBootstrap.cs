using System.IO;
using ScriptableObject;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the RuleEntryData assets for rules 1-5 from the GDD
/// (Rule_of_Horror_Phone_Rules_1-5).
///
/// The rule text is copied from the document verbatim, including the "defiant answer" case
/// where the player only gets partial information — several rules word that case differently
/// rather than just dropping the last line, so the two arrays are not subsets of each other.
///
/// Safe to re-run: existing assets are updated in place instead of duplicated. Note that
/// re-running overwrites text edited directly on the assets, so edits meant to stick belong
/// in this script.
///
/// The rule text itself stays in Thai — it is game content shown to the player, not editor UI.
/// </summary>
public static class RuleEntryBootstrap
{
    private const string FolderPath = "Assets/Scriptable/Rules";

    [MenuItem("Rule of Horror/Phone/Generate Rule Data 1-5 From GDD", false, 80)]
    private static void Generate()
    {
        EnsureFolder();

        Build(1, "อย่าไปไกลกว่านี้", 19, 0, 19, 40,
            "ห้ามไปไกลกว่านี้",
            slots: 2,
            cooperative: new[]
            {
                "อยู่ใกล้ศาลาไว้ ห้ามออกไปไกล",
                "ทำตามกฎ แล้วรอจนถึงเวลา",
            },
            defiant: new[]
            {
                "อยู่ใกล้ศาลาไว้ ห้ามออกไปไกล",
            },
            hands: new[]
            {
                Hint("WASD", "เดิน"),
                Hint("E",    "เก็บของ"),
                Hint("Tab",  "หยิบ / เก็บโทรศัพท์"),
                Hint("2",    "ไฟฉาย"),
            });

        Build(2, "ไฟดับ", 20, 0, 20, 40,
            "ไฟดับแล้ว ลุกไปเปิดไฟให้ติด เปิดวิทยุก่อนกลับ แล้วนั่งที่เดิมจนกว่าวิทยุจะดับลง",
            slots: 3,
            cooperative: new[]
            {
                "เปิดไฟ แล้วเปิดวิทยุ",
                "กลับมานั่งที่เดิม รอจนวิทยุดับ",
                "ถ้าไฟไม่ติด ต้องไปซ่อมเอง",
            },
            defiant: new[]
            {
                "เปิดไฟ แล้วเปิดวิทยุ",
                "กลับมานั่งที่เดิม รอจนวิทยุดับ",
            },
            hands: new[]
            {
                Hint("E",     "เปิดไฟ / เปิดวิทยุ / เปิดตู้ไฟ"),
                Hint("Mouse", "ลากคันโยกในตู้ไฟ"),
                Hint("2",     "ไฟฉาย"),
            });

        Build(3, "จ้องตัวเลข", 21, 0, 21, 40,
            "หากระดาษตัวเลข 4 แผ่น แล้วจ้องให้มันหายไป เรียงจากเลขน้อยไปมาก",
            slots: 3,
            cooperative: new[]
            {
                "จ้องทีละแผ่น จากน้อยไปมาก ให้ครบ 4 แผ่น",
                "ลืมตาไว้ ห้ามหลับตาเด็ดขาด",
                "ถ้ามีอะไรมองอยู่ ห้ามขยับ",
            },
            defiant: new[]
            {
                "จ้องทีละแผ่น จากน้อยไปมาก ให้ครบ 4 แผ่น",
            },
            hands: new[]
            {
                Hint("คลิกซ้าย ค้าง", "จ้องกระดาษ"),
                Hint("ปล่อยทุกปุ่ม",  "อยู่นิ่ง"),
            });

        Build(4, "เซ่นไหว้", 22, 0, 22, 40,
            "หาตุ๊กตา แล้วเอาไปวางไว้บนศาล",
            slots: 5,
            cooperative: new[]
            {
                "ตามหาตุ๊กตาจากเสียง ยิ่งใกล้ยิ่งชัด",
                "ต้องวางให้ครบ 6 ตัว",
                "ศาลไม่ได้อยู่ที่เดิมตลอด",
                "ถ้าคนที่แขวนคอหายไป อย่าให้มันได้ยินเสียงหายใจ",
                "ถ้ามันหาเจอ อย่าหนี จ้องมันไว้",
            },
            defiant: new[]
            {
                "ตุ๊กตาต้องกลับไปอยู่บนศาล",
            },
            hands: new[]
            {
                Hint("E",            "เก็บ / วางตุ๊กตา"),
                Hint("คลิกขวา ค้าง", "จ้อง"),
                Hint("Q ค้าง",       "กลั้นหายใจ"),
            });

        Build(5, "สายสิญจน์", 23, 0, 23, 40,
            "เมื่อสายสิญจน์ล้อมรอบศาลา ให้จับมันไว้แล้วเดินไปตามทาง จนกว่าระฆังจะเรียกให้กลับมา",
            slots: 4,
            cooperative: new[]
            {
                "ได้ยินเสียงฉิ่ง ให้หยุดเดิน เงียบแล้วค่อยเดินต่อ",
                "นับระฆัง ครั้งที่ 3 ให้กลับไปนั่งที่ศาลา",
                "อย่าปล่อยมือนานเกินไป",
                "อย่าสนใจเสียงข้างหลัง",
            },
            defiant: new[]
            {
                "อย่าปล่อยสายสิญจน์",
            },
            hands: new[]
            {
                Hint("E",     "จับ / ปล่อยสายสิญจน์"),
                Hint("W",     "เดินตามสาย"),
                Hint("Space", "สวดมนต์"),
            });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RuleEntryBootstrap] Created/updated rule data 1-5 in {FolderPath}\n" +
                  "Next: drop all five into RuleBookController → rules, then tick recordsRule on the " +
                  "chat nodes where the rule gets written into the phone");
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static RuleKeyHint Hint(string key, string action) => new RuleKeyHint { key = key, action = action };

    private static void Build(int number, string title, int sh, int sm, int eh, int em,
                              string summary, int slots, string[] cooperative, string[] defiant, RuleKeyHint[] hands)
    {
        string path  = $"{FolderPath}/RuleEntry_{number}.asset";
        var    asset = AssetDatabase.LoadAssetAtPath<RuleEntryData>(path);
        bool   isNew = asset == null;

        if (isNew) asset = UnityEngine.ScriptableObject.CreateInstance<RuleEntryData>();

        asset.ruleNumber          = number;
        asset.title               = title;
        asset.summary             = summary;
        asset.startHour           = sh;
        asset.startMinute         = sm;
        asset.endHour             = eh;
        asset.endMinute           = em;
        asset.factSlots           = slots;
        asset.factsIfCooperative  = cooperative;
        asset.factsIfDefiant      = defiant;
        asset.hands               = hands;

        if (isNew) AssetDatabase.CreateAsset(asset, path);
        else       EditorUtility.SetDirty(asset);
    }

    private static void EnsureFolder()
    {
        if (Directory.Exists(FolderPath)) return;
        Directory.CreateDirectory(FolderPath);
        AssetDatabase.Refresh();
    }
}
