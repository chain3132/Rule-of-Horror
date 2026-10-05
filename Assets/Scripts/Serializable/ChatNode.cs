using System.Collections.Generic;
using ScriptableObject;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class ChatNode
{
    [TextArea(2,5)]
    public string message;

    public bool isPlayer;

    [ShowIf("isPlayer")]
    [TableList]
    public List<ReplyOption> replies;

    [HideIf("isPlayer")]
    public int nextNode;

    // ─────────────────────────── บันทึกลงแอปกฎ ───────────────────────────
    // ในแชทจะมีจังหวะที่ขึ้นว่า "กฎข้อที่ N — บันทึกใน [3]" ตรงนั้นแหละคือ node ที่ติดช่องนี้
    //
    // ทำไมอยู่ที่ node ไม่ใช่ที่ ReplyOption: หลังผู้เล่นเลือกตอบ บทสนทนาจะแยกเป็นสองสาย
    // และตัวกฎถูกบันทึกใน node ของสายนั้นอยู่แล้ว ติดธงตรง node จึงได้ทั้ง "บันทึกเมื่อไร"
    // และ "บันทึกแบบไหน" มาฟรีๆ โดยไม่ต้องจำว่าผู้เล่นกดอะไรไปเมื่อกี้

    [Tooltip("ใส่กฎที่จะถูกบันทึกลงแอปกฎตอน node นี้ถูกแสดง — เว้นว่าง = ไม่บันทึกอะไร")]
    public RuleEntryData recordsRule;

    [ShowIf("@recordsRule != null")]
    [Tooltip("✓ = สายที่ผู้เล่นให้ความร่วมมือ (รู้เวลา รู้ครบ)\n" +
             "✗ = สายที่ผู้เล่นหัวแข็ง (เวลาเป็น --:-- รู้ไม่ครบ)")]
    public bool recordsAsCooperative = true;
}
