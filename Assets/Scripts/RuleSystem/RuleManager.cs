using Manager;
using RuleSystem;
using UnityEngine;

public class RuleManager : MonoBehaviour
{
    public RuleBase[] rules;
    [SerializeField] private HorrorTextUI horrorTextUI;


    private void OnEnable()
    {
        TimeManager.OnTimeChanged += CheckRules;
    }

    private void OnDisable()
    {
        TimeManager.OnTimeChanged -= CheckRules;
    }

    void CheckRules(int hour, int minute)
    {
        int currentTime = hour * 60 + minute;

        foreach (var rule in rules)
        {
            if (rule == null) continue; // ป้องกัน slot ว่างใน Inspector

            int startTime = rule.startHour * 60 + rule.startMinute;
            int endTime = rule.endHour * 60 + rule.endMinute;

            // เริ่มกฎ
            if (!rule.ruleActive && currentTime >= startTime && currentTime < endTime)
            {
                rule.StartRule();
            }

            // จบกฎ
            if (rule.ruleActive && currentTime >= endTime)
            {
                rule.EndRule();
            }
        }
    }
    /// <summary>
    /// ปลดธง ruleActive ของกฎทุกข้อให้พร้อม trigger ใหม่ — ใช้ตอนเริ่มคืนเดิมใหม่
    /// (ผู้เล่นขึ้นรถคันที่ 4 ของกฎข้อ 6 แล้วเวลาย้อนกลับไป 18:40)
    ///
    /// ไม่เรียก EndRule() ของกฎไหนเลย เพราะกฎจบไปแล้วตามปกติในคืนที่ผ่านมา — ตัวที่เหลือ
    /// เป็นแค่ธงที่ต้องเคลียร์ให้ CheckRules มองเห็นว่า "ยังไม่เริ่ม" เมื่อเวลาเดินมาถึงรอบสอง
    /// เรียก EndRule ซ้ำจะไปปลุก cleanup ของกฎที่ไม่ได้เล่นอยู่ (ดับไฟ ลบผี ดึงผู้เล่นลุก)
    ///
    /// กฎที่มีสถานะภายในเกินกว่า ruleActive (เช่น Rule1 ที่ล็อก _hasCompleted ไว้กันเล่นซ้ำ)
    /// ต้อง override ResetForNewLoop() เอง
    ///
    /// ไล่จาก RuleBase ทุกตัวในซีน ไม่ใช่จากช่อง rules — เพราะกฎ 1 ไม่ได้อยู่ในลิสต์นั้น
    /// (มันถูก trigger จาก FriendListController ตอนอ่านแชทจบ ไม่ใช่ตามเวลา) ถ้าไล่จากลิสต์
    /// กฎ 1 จะยังล็อก _hasCompleted อยู่ แล้วคืนรอบสองจะข้ามกฎ 1 ไปทั้งข้อ
    /// </summary>
    public void RearmAllRules()
    {
        var all = FindObjectsByType<RuleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var rule in all)
        {
            if (rule == null) continue;

            rule.ruleActive = false;
            rule.ResetForNewLoop();
        }

        Debug.Log($"[RuleManager] ปลดธงกฎ {all.Length} ข้อ พร้อมเริ่มคืนใหม่", this);
    }
    // public bool CheckTime(int hour, int minute)
    // {
    //
    //
    // }
}
