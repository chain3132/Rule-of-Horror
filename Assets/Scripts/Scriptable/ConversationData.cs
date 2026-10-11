using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptableObject
{
    [CreateAssetMenu(menuName = "Phone/Conversation")]
    public class ConversationData : SerializedScriptableObject
    {
        // ─────────────────────────── node ที่เริ่มเล่น ───────────────────────────
        // เดิมเริ่มที่ node 0 ตายตัว ซึ่งแปลว่าถ้าอยากเติมบทไว้ "ก่อน" บทแรก
        // ต้องยัดมันเข้าไปเป็น node 0 แล้วเลขของทุก node ขยับหมดทั้งไฟล์
        //
        // มีช่องนี้แล้วลำดับใน list เป็นแค่ลำดับที่เก็บ ไม่ใช่ลำดับที่เล่น — เติมบทต่อหัวได้
        // โดยไม่ต้องย้ายอะไรเลย ค่าเริ่มต้น 0 = พฤติกรรมเดิมเป๊ะ แอสเซตเก่าไม่ต้องแตะ
        //
        // ตั้งจากหน้าต่าง Conversation Graph ได้ด้วย คลิกขวาที่ node → Make this the start

        [Tooltip("node ที่บทสนทนานี้เริ่มเล่น — ปกติคือ 0")]
        public int startNode;

        [TableList]
        public List<ChatNode> nodes;
    }
}
