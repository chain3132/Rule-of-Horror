using UnityEngine;

/// <summary>
/// การ์ดแจ้งเตือน 1 ใบบนหน้าโฮมของโทรศัพท์
///
/// เก็บเวลาที่มันเด้งไว้ด้วย เพราะหน้าโฮมโชว์ "ตอนนี้" ตอนเพิ่งมา แล้วค่อยกลายเป็นเวลาจริง
/// เมื่อผ่านไปสักพัก — ผู้เล่นที่เปิดโทรศัพท์ช้าจะได้รู้ว่าข้อความค้างมานานแค่ไหนแล้ว
/// </summary>
public class PhoneNotification
{
    /// <summary>ชื่อผู้ส่ง เช่น ไม่ทราบชื่อ</summary>
    public string title;

    /// <summary>บรรทัดรอง เช่น ข้อความใหม่ 1 ข้อความ</summary>
    public string body;

    /// <summary>ไอคอน/รูปโปรไฟล์ — เว้นว่างได้</summary>
    public Sprite icon;

    /// <summary>กดปุ่มเลขอะไรถึงจะไปที่มัน (0 = ข้อความ, 1 = ไฟฉาย, 2 = กฎ)</summary>
    public int appIndex;

    /// <summary>เวลาในเกมตอนที่มันเด้ง</summary>
    public int hour, minute;

    /// <summary>ของที่เป็นเจ้าของการแจ้งเตือนนี้ (ปกติคือ ContactEntry) — ใช้เคลียร์ตอนผู้เล่นอ่านแล้ว</summary>
    public object source;

    public int TotalMinutes => hour * 60 + minute;

    // ─────────────────────────── สองภาษา ───────────────────────────
    // การ์ดอยู่บนหน้าโฮมได้นานหลายชั่วโมงในเกม ถ้าเก็บแต่ตัวหนังสือที่จัดรูปแล้ว ผู้เล่นที่เปลี่ยน
    // ภาษาทีหลังจะเห็นการ์ดเก่าค้างเป็นภาษาเดิม — เก็บต้นฉบับกับตัวเลขไว้ แล้วแปลตอนวาด

    /// <summary>ต้นฉบับของ body ที่ยังไม่แทนตัวเลข เช่น "new message {0} message" — ว่าง = ใช้ body ตรงๆ</summary>
    public string bodyFormat;

    /// <summary>ตัวเลขที่แทน {0} ใน bodyFormat</summary>
    public int bodyArg;

    public string DisplayTitle => L10n.T(title);

    public string DisplayBody => string.IsNullOrEmpty(bodyFormat) ? L10n.T(body)
                                                                   : L10n.Format(bodyFormat, bodyArg);
}
