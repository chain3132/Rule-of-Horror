using ScriptableObject;
using UnityEngine;

/// <summary>
/// บทสนทนา 1 ชุดที่ contact คนหนึ่งจะส่งมาตอนกี่โมง
///
/// ตารางเวลาต้องอยู่ที่ "ข้อความ" ไม่ใช่ที่ "คน" เพราะคนปริศนาส่งมาทั้งคืน 18:40 / 19:40 /
/// 20:40 / 21:40 / 22:40 — เป็นคนเดียวกันตลอด ถ้าผูกเวลาไว้กับ contact จะต้องสร้าง contact
/// ซ้ำกัน 5 อัน ซึ่งแปลว่ารายชื่อมี "ไม่ทราบชื่อ" โผล่ 5 แถว และแต่ละแถวต้องมีปุ่มของตัวเอง
/// </summary>
[System.Serializable]
public class ScheduledMessage
{
    [Tooltip("บทสนทนาที่จะเล่นเมื่อผู้เล่นเปิดอ่าน")]
    public ConversationData conversation;

    [Header("ส่งถึงตอน")]
    public int notifyHour;
    public int notifyMinute;

    [Tooltip("✓ = พออ่านบทสนทนานี้จบ → เริ่ม Rule1 sequence\n" +
             "และตอนข้อความนี้ส่งถึง นาฬิกาจะถูกหยุดไว้จนกว่าจะอ่านจบ")]
    public bool triggersRule1OnEnd;

    /// <summary>ถึงเวลาแล้วและเด้งแจ้งเตือนไปแล้ว</summary>
    [HideInInspector] public bool delivered;

    /// <summary>ผู้เล่นอ่านชุดนี้จนจบแล้ว — เปิดค้างไว้กลางคันยังไม่นับ</summary>
    [HideInInspector] public bool read;

    public int TotalMinutes => notifyHour * 60 + notifyMinute;
}
