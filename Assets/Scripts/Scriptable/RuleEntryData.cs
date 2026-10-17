using UnityEngine;

namespace ScriptableObject
{
    /// <summary>
    /// เนื้อหาของกฎ 1 ข้อตามที่มันไปโผล่ใน "แอปกฎ" (ปุ่ม 3) ของโทรศัพท์
    ///
    /// จุดสำคัญของดีไซน์: สิ่งที่ผู้เล่นรู้ ขึ้นกับว่าตอบแชทว่าอะไร
    ///   ตอบแบบให้ความร่วมมือ  → คนปริศนาบอกรายละเอียด = ได้เวลาเริ่ม-จบ และรู้ครบทุกข้อ
    ///   ตอบแบบหัวแข็ง         → มันไม่บอก = เวลาเป็น --:-- และรู้แค่ข้อเดียว ที่เหลือเป็น "—"
    ///
    /// สองเคสนี้ไม่ใช่ subset ของกัน (ข้อ ๕ ตอบหัวแข็งได้ "อย่าปล่อยสายสิญจน์" ซึ่งไม่มีใน
    /// ฝั่งให้ความร่วมมือเลย) เลยแยกเป็นสองลิสต์ ไม่ใช่ลิสต์เดียวแล้วติดธงซ่อน
    ///
    /// factSlots คือจำนวนบรรทัดที่การ์ดจองไว้เสมอ — ที่เหลือเติม "—" ให้
    /// ความสูงของการ์ดจะได้ไม่กระโดดระหว่างกฎ และช่องว่างเองก็เป็นการบอกผู้เล่นว่า "ยังมีที่ไม่รู้"
    /// </summary>
    [CreateAssetMenu(menuName = "Phone/Rule Entry", fileName = "RuleEntry_")]
    public class RuleEntryData : UnityEngine.ScriptableObject
    {
        private const string ThaiDigits = "๐๑๒๓๔๕๖๗๘๙";

        [Header("หัวข้อ")]
        [Tooltip("เลขกฎ 1-9 ใช้ทั้งเรียงลำดับและแปลงเป็นเลขไทยบนแถบบนสุด")]
        [Range(1, 9)]
        public int ruleNumber = 1;

        [Tooltip("ชื่อกฎ เช่น อย่าไปไกลกว่านี้ / สายสิญจน์")]
        public string title;

        [Tooltip("ตัวกฎเต็มๆ ที่คนปริศนาส่งมา — ย่อให้พออ่านจบในจอเดียว")]
        [TextArea(2, 4)]
        public string summary;

        [Header("ช่วงเวลาของกฎ")]
        public int startHour = 19;
        public int startMinute;
        public int endHour = 19;
        public int endMinute = 40;

        [Header("สิ่งที่รู้")]
        [Tooltip("จำนวนบรรทัดขั้นต่ำที่การ์ดจองไว้\n" +
                 "ปกติไม่ต้องแตะ — ช่องว่างนับจากฝั่งที่รู้มากกว่าให้เองแล้ว " +
                 "ค่านี้เป็นแค่พื้นขั้นต่ำเผื่ออยากจองที่ไว้มากกว่านั้น")]
        [Range(1, 8)]
        public int factSlots = 4;

        [Tooltip("ตอบแชทแบบให้ความร่วมมือ (ตัวเลือกที่ 2) → รู้เท่านี้")]
        [TextArea(1, 3)]
        public string[] factsIfCooperative;

        [Tooltip("ตอบแชทแบบหัวแข็ง (ตัวเลือกที่ 1) → มันไม่บอก เหลือเท่านี้")]
        [TextArea(1, 3)]
        public string[] factsIfDefiant;

        [Header("มือของคุณ")]
        [Tooltip("ปุ่มทั้งหมดของกฎข้อนี้ เหมือนกันทั้งสองเคสการตอบ — ปุ่มคือสิ่งที่ผู้เล่นมี ไม่ใช่สิ่งที่ผีให้")]
        public RuleKeyHint[] hands;

        /// <summary>
        /// จำนวนรายละเอียดสูงสุดที่กฎข้อนี้มีให้รู้ได้ — ฝั่งที่รู้มากกว่าเป็นตัวตั้ง
        ///
        /// ใช้นับว่า "หายไปกี่ข้อ" ตอนผู้เล่นตอบหัวแข็ง: ข้อ ๔ ฝั่งร่วมมือมี 5 ฝั่งหัวแข็งมี 1
        /// เท่ากับมีขีดขาว 4 เส้น และเลขนี้ขยับตามเองเวลาเพิ่ม/ลดข้อในลิสต์
        /// ไม่ต้องไปตามแก้ factSlots ให้ตรงด้วยมือ
        /// </summary>
        public int MaxFactCount =>
            Mathf.Max(factsIfCooperative?.Length ?? 0, factsIfDefiant?.Length ?? 0);

        /// <summary>เลขไทยของกฎข้อนี้ สำหรับแถบ ๑ ๒ ๓ ๔ ๕ ด้านบน</summary>
        public string ThaiNumeral =>
            ruleNumber >= 0 && ruleNumber < ThaiDigits.Length ? ThaiDigits[ruleNumber].ToString() : "·";

        /// <summary>
        /// "19:00 – 19:40" — ใช้เมื่อผู้เล่นรู้เวลา
        ///
        /// ผ่าน TimeManager.Format เพราะกฎข้อ 6 ใช้ชั่วโมง 24 (นาฬิกาในเกมเดินเป็นเส้นตรง
        /// ไม่วนรอบ เพื่อให้ลำดับกฎทั้งคืนเทียบกันได้) การ์ดจึงต้องโชว์ 00:00 – 00:40
        /// </summary>
        public string ScheduleText =>
            $"{Manager.TimeManager.Format(startHour, startMinute)} – " +
            $"{Manager.TimeManager.Format(endHour, endMinute)}";

        /// <summary>true = เวลาตอนนี้อยู่ในช่วงของกฎข้อนี้ (ใช้ไฮไลต์กฎที่กำลังมีผล)</summary>
        public bool IsWithin(int hour, int minute)
        {
            int now   = hour * 60 + minute;
            int start = startHour * 60 + startMinute;
            int end   = endHour * 60 + endMinute;
            return end >= start ? now >= start && now <= end       // ช่วงปกติ
                                : now >= start || now <= end;      // ช่วงที่คร่อมเที่ยงคืน
        }
    }
}
