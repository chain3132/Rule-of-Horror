using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// ทางเข้าเดียวของระบบสองภาษา — แปลข้อความจาก "ตัวต้นฉบับ" เป็นภาษาที่เลือกอยู่
///
/// ทำไมใช้ตัวต้นฉบับเป็นตัวตั้ง (แบบ gettext) แทนที่จะให้ทุกข้อความมี key ของตัวเอง:
/// ข้อความในเกมนี้กระจายอยู่ในที่ที่ไม่มีช่องให้ใส่ key — node แชทในแอสเซตที่แก้ผ่านหน้าต่าง
/// Conversation Graph, บทพูด DialogueLine ในซีน, hint ที่เป็น string ใน Inspector ของกฎแต่ละข้อ
/// ถ้าจะให้ทุกอันถือ key ต้องเปลี่ยนชนิดช่องทั้งโปรเจกต์ แล้วข้อมูลเก่าจะหายตอน Unity
/// deserialize — แบบนี้ข้อมูลเดิมไม่ต้องแตะเลย key คำนวณจากข้อความเอง
///
/// key = ข้อความที่ปรับช่องว่างให้เป็นแบบเดียวกันแล้ว (Normalize) → hash
/// ผลข้างเคียงที่รู้ไว้: แก้ข้อความต้นฉบับเมื่อไหร่ key เปลี่ยน คำแปลเดิมจะหลุด แล้วเกมจะ
/// แสดงต้นฉบับแทน (ไม่มีวันขึ้นคำแปลผิดอัน) — รัน Rule of Horror ▸ Localization ▸ Setup อีกรอบ
/// เพื่อเพิ่มแถวใหม่ แล้วเติมคำแปลใน Localization Tables
///
/// ภาษาที่เลือกอยู่เก็บไว้ที่แพ็กเกจ (LocalizationSettings.SelectedLocale) ไม่ได้เก็บเอง —
/// ตัวนี้แค่อ่านตามและส่งต่อ event ทุกระบบที่ฟังแพ็กเกจจึงเห็นภาษาเดียวกันเสมอ
/// และการเลือกภาษาถูกจำข้ามการเปิดเกมโดย PlayerPrefLocaleSelector ของแพ็กเกจเอง
/// </summary>
public static class L10n
{
    // ─────────────────────────── ค่าคงที่ ───────────────────────────

    /// <summary>
    /// ตารางที่ค้น เรียงตามลำดับ — แยกเป็นหมวดให้คนตรวจคำแปลหาง่าย ไม่ได้มีผลกับการค้น
    /// (key มาจากข้อความ ข้อความเดียวกันจึงได้ key เดียวกันไม่ว่าอยู่ตารางไหน)
    /// </summary>
    public static readonly string[] TableNames = { "UI", "Dialogue", "Chat", "Rules" };

    public const string ThaiCode    = "th";
    public const string EnglishCode = "en";

    // ─────────────────────────── สถานะ ───────────────────────────

    /// <summary>ภาษาเปลี่ยน — ข้อความที่ค้างอยู่บนจอต้องวาดใหม่ (ฟองแชท สมุดกฎ หน้าโฮม)</summary>
    public static event Action Changed;

    private static bool _hooked;

    // ตารางของภาษาปัจจุบัน ล้างทิ้งเมื่อเปลี่ยนภาษา — GetTable รอโหลดแบบ synchronous
    // ถึงจะเร็วเพราะ preload ไว้แล้ว ก็ไม่ควรเรียกซ้ำทุกฟองข้อความ
    private static readonly Dictionary<string, StringTable> _tables = new Dictionary<string, StringTable>();
    private static readonly HashSet<string> _missingTables = new HashSet<string>();
    private static readonly Dictionary<string, string> _keyCache = new Dictionary<string, string>();

#if UNITY_EDITOR
    private static readonly HashSet<string> _reportedMissing = new HashSet<string>();
#endif

    // ปิด Reload Domain แล้ว static ไม่ถูกล้างเอง — event ที่ค้างจากรอบ Play ก่อนจะชี้ไปหา
    // object ที่ถูกทำลายไปแล้ว
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Changed = null;
        _hooked = false;
        _tables.Clear();
        _missingTables.Clear();
        _keyCache.Clear();
#if UNITY_EDITOR
        _reportedMissing.Clear();
#endif
    }

    // ─────────────────────────── ภาษา ───────────────────────────

    /// <summary>รหัสภาษาที่เลือกอยู่ ("th" / "en") — ยังไม่มีระบบภาษา = "en"</summary>
    public static string CurrentCode
    {
        get
        {
            if (!Ready()) return EnglishCode;
            var locale = LocalizationSettings.SelectedLocale;
            return locale != null ? locale.Identifier.Code : EnglishCode;
        }
    }

    public static bool IsThai => CurrentCode == ThaiCode;

    /// <summary>เปลี่ยนภาษา — แพ็กเกจจะบันทึกลง PlayerPrefs เอง เปิดเกมรอบหน้าจะได้ภาษานี้</summary>
    public static void SetLanguage(string code)
    {
        if (!Ready()) return;

        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (locale == null)
        {
            Debug.LogWarning($"[L10n] ไม่มีภาษา '{code}' ใน Localization Settings — " +
                             "รัน Rule of Horror ▸ Localization ▸ Setup ก่อน");
            return;
        }

        LocalizationSettings.SelectedLocale = locale;
    }

    public static void Toggle() => SetLanguage(IsThai ? EnglishCode : ThaiCode);

    // ─────────────────────────── แปล ───────────────────────────

    /// <summary>
    /// แปลข้อความต้นฉบับเป็นภาษาที่เลือกอยู่ — ไม่เจอคำแปล = คืนต้นฉบับ
    ///
    /// ส่งข้อความที่ "ยังไม่ได้จัดรูป" เข้ามาเท่านั้น: ถ้ามี {0} หรือ {reward} ให้แปลก่อน
    /// แล้วค่อยแทนค่า ข้อความที่แทนค่าไปแล้วไม่มีทางตรงกับ key ไหนในตาราง
    /// </summary>
    public static string T(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Ready()) return source;

        string key = KeyFor(source);

        foreach (var name in TableNames)
        {
            var table = GetTable(name);
            if (table == null) continue;

            var entry = table.GetEntry(key);
            if (entry == null || string.IsNullOrEmpty(entry.Value)) continue;

            // ใช้ Value ตรงๆ ไม่ผ่าน GetLocalizedString — ข้อความเกมนี้มีวงเล็บปีกกาของตัวเอง
            // ({0}, {reward}) ถ้าเผลอไปติ๊ก Smart ไว้ Smart Format จะพยายามตีความแล้วพัง
            return entry.Value;
        }

        ReportMissing(source, key);
        return source;
    }

    /// <summary>แปลแล้ว string.Format — สำหรับข้อความแบบ "แต้ม {0}"</summary>
    public static string Format(string sourceFormat, params object[] args)
    {
        string format = T(sourceFormat);
        if (string.IsNullOrEmpty(format) || args == null || args.Length == 0) return format;

        try { return string.Format(format, args); }
        catch (FormatException)
        {
            // คำแปลที่เขียนวงเล็บผิด (เช่นลืม {0}) ต้องไม่ทำให้เกมล้ม — กลับไปใช้ต้นฉบับ
            Debug.LogWarning($"[L10n] คำแปลของ \"{sourceFormat}\" จัดรูปไม่ได้: \"{format}\"");
            return string.Format(sourceFormat, args);
        }
    }

    // ─────────────────────────── key ───────────────────────────

    /// <summary>
    /// ทำให้ข้อความที่ "ควรเป็นอันเดียวกัน" ได้ key เดียวกัน
    ///
    /// ข้อความที่ Unity อ่านจาก YAML กับข้อความที่ส่งมาตอนเล่นต่างกันได้เรื่องช่องว่าง (\r\n กับ
    /// \n, ช่องว่างท้ายบรรทัด, แท็บ) ถ้าใช้ตัวดิบ key จะไม่ตรงกันเพราะเรื่องที่มองไม่เห็นเลย
    /// ใช้กับ key อย่างเดียว ข้อความที่แสดงยังเป็นตัวเต็มตามเดิม
    /// </summary>
    public static string Normalize(string source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var sb = new StringBuilder(source.Length);
        bool pendingSpace = false;

        foreach (char raw in source)
        {
            char c = raw == '\r' ? '\n' : raw;

            if (c == '\u200B') continue;                         // ตัวตัดคำไทย ไม่ใช่เนื้อความ

            if (c == ' ' || c == '\t' || c == '\u00A0')
            {
                pendingSpace = true;
                continue;
            }

            if (c == '\n')
            {
                // ช่องว่างรอบบรรทัดใหม่ทิ้งหมด และบรรทัดว่างซ้อนกันยุบเหลือบรรทัดเดียว
                pendingSpace = false;
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                continue;
            }

            if (pendingSpace && sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }

        return sb.ToString().Trim('\n', ' ');
    }

    /// <summary>
    /// key ของข้อความ = ต้นคำอังกฤษให้คนอ่านออกในหน้าต่าง Tables + hash กันชนกัน
    /// เช่น "honey-dinners-getting-cold-i_3f9a1c2b" · ข้อความไทยล้วนได้ "th_xxxxxxxx"
    /// </summary>
    public static string KeyFor(string source)
    {
        if (source == null) return string.Empty;
        if (_keyCache.TryGetValue(source, out var cached)) return cached;

        string norm = Normalize(source);

        // FNV-1a 32 บิต — เขียนเอง ไม่ใช้ string.GetHashCode เพราะตัวนั้นไม่คงที่ข้ามรอบเปิดเกม
        uint hash = 2166136261;
        foreach (char c in norm)
        {
            hash ^= c;
            hash *= 16777619;
        }

        string key = $"{Slug(norm)}_{hash:x8}";
        if (_keyCache.Count < 4096) _keyCache[source] = key;
        return key;
    }

    private static string Slug(string norm)
    {
        var sb = new StringBuilder(32);
        int words = 0;
        bool inWord = false;
        bool hasThai = false;

        foreach (char raw in norm)
        {
            if (raw >= '\u0E00' && raw <= '\u0E7F') hasThai = true;

            char c = char.ToLowerInvariant(raw);
            bool alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

            if (alnum)
            {
                if (!inWord)
                {
                    if (words == 5 || sb.Length >= 28) break;
                    if (sb.Length > 0) sb.Append('-');
                    words++;
                    inWord = true;
                }
                sb.Append(c);
            }
            else if (c != '\'' && c != '’')
            {
                inWord = false;
            }
        }

        if (sb.Length == 0) return hasThai ? "th" : "x";
        return sb.ToString();
    }

    // ─────────────────────────── ภายใน ───────────────────────────

    /// <summary>
    /// ระบบภาษาพร้อมใช้ไหม — ยังไม่รัน Setup (ไม่มี Localization Settings) = ไม่พร้อม
    /// แล้วทุกอย่างคืนต้นฉบับ เกมจึงเล่นได้เหมือนก่อนมีระบบนี้ทุกอย่าง
    /// </summary>
    private static bool Ready()
    {
        if (_hooked) return true;
        if (!Application.isPlaying || !LocalizationSettings.HasSettings) return false;

        // ครั้งแรกเท่านั้น — รอให้แพ็กเกจเลือกภาษาตอนเปิดเกมเสร็จ (PlayerPrefs → ภาษาเครื่อง)
        var init = LocalizationSettings.InitializationOperation;
        if (!init.IsDone) init.WaitForCompletion();
        if (LocalizationSettings.SelectedLocale == null) return false;

        _hooked = true;

        // ถอดก่อนใส่ — event นี้อยู่บนแอสเซต Localization Settings ซึ่งค้างข้ามรอบ Play ใน Editor
        // (ตอนปิด Reload Domain) ถ้าไม่ถอด รอบที่สองจะได้ทุก event สองครั้ง
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        return true;
    }

    private static StringTable GetTable(string name)
    {
        if (_tables.TryGetValue(name, out var table)) return table;
        if (_missingTables.Contains(name)) return null;

        table = LocalizationSettings.StringDatabase.GetTable(name);
        if (table == null)
        {
            // จำไว้ว่าไม่มี ไม่งั้นจะได้ error "Could not find table" ทุกครั้งที่มีข้อความขึ้นจอ
            _missingTables.Add(name);
            return null;
        }

        _tables[name] = table;
        return table;
    }

    /// <summary>
    /// ชื่อปุ่มกับตัวเลข ("E", "TAB", "WASD", "18:40") เหมือนกันทุกภาษา ไม่ต้องรายงานว่าขาดคำแปล
    /// — ตัดสินจาก "ไม่มีตัวพิมพ์เล็กและไม่มีอักษรไทย" ซึ่งข้อความที่เป็นประโยคจริงแทบไม่มีทางเข้าเกณฑ์
    /// </summary>
    private static bool LooksLanguageNeutral(string source)
    {
        foreach (char c in source)
        {
            if (char.IsLower(c)) return false;
            if (c >= '฀' && c <= '๿') return false;
        }
        return true;
    }

    private static void OnLocaleChanged(Locale locale)
    {
        _tables.Clear();
        _missingTables.Clear();
        Changed?.Invoke();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void ReportMissing(string source, string key)
    {
#if UNITY_EDITOR
        if (LooksLanguageNeutral(source)) return;
        if (!_reportedMissing.Add(key)) return;

        string shown = source.Length > 60 ? source.Substring(0, 60) + "…" : source;
        Debug.Log($"[L10n] ยังไม่มีคำแปล: \"{shown.Replace("\n", "\\n")}\" (key {key}) — " +
                  "จดไว้แล้ว รัน Rule of Horror ▸ Localization ▸ 1. Setup อีกรอบ แถวนี้จะถูกเพิ่มลงตารางเอง");

        // จดลงไฟล์ด้วย — ข้อความในซีน (hint, บทพูด) เครื่องมือ Setup ไม่ได้เปิดซีนไล่หา
        // ทางที่เจอข้อความใหม่ได้ครบที่สุดคือ "ตอนมันขึ้นจอจริง" แล้วให้ Setup รอบหน้ามาเก็บไป
        try
        {
            System.IO.File.AppendAllText(MissingLogPath, EscapeLine(source) + "\n", Encoding.UTF8);
        }
        catch (System.IO.IOException) { /* ไฟล์ถูกเปิดค้างอยู่ — รอบหน้าค่อยจด */ }
#endif
    }

#if UNITY_EDITOR
    /// <summary>ไฟล์จดข้อความที่ยังไม่มีคำแปล — อยู่ใน Library เพราะเป็นของเครื่องนี้ ไม่ต้องขึ้น git</summary>
    public static string MissingLogPath =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "L10n-missing.tsv"));

    public static string EscapeLine(string s) =>
        s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
#endif
}
