namespace Enum
{
    /// <summary>
    /// รางวัลที่คนปริศนาให้เลือกหลังจบกฎแต่ละข้อ
    ///
    /// ห้ามแทรกตรงกลางหรือสลับลำดับ — ค่านี้ถูก serialize เป็นตัวเลขดิบอยู่ใน ChatNode
    /// ของ ConversationData และในลิสต์ของ RewardSystem เหมือนกับ PhoneState
    /// ของใหม่ให้ต่อท้ายอย่างเดียว
    /// </summary>
    public enum RewardKind
    {
        /// <summary>ไม่ใช่รางวัล — ค่าเริ่มต้นของช่องที่ยังไม่ได้ตั้ง</summary>
        None = 0,

        /// <summary>เทียน — Relax: ให้แสงสว่าง รู้สึกปลอดภัย / Tension: ไฟดับ มีควันจางลอยออกมา</summary>
        Candle,

        /// <summary>เสาไฟ — Relax: แสงสว่าง บรรยากาศชัดเจน / Tension: ไฟกระพริบ</summary>
        StreetLamp,

        /// <summary>วิทยุ — Relax: เพลงช่วยให้ผ่อนคลาย / Tension: เสียงที่ผิดเพี้ยน</summary>
        Radio,

        /// <summary>หิ่งห้อย — Relax: แสงนวลทำให้พื้นที่ดูปลอดภัย / Tension: เปลี่ยนเป็นสีแดง</summary>
        Firefly,

        /// <summary>ธูป — Relax: ควันธูปบอกเขตปลอดภัย / Tension: ควันบิดเบี้ยว ไม่เห็นเขตปลอดภัย</summary>
        Incense,

        /// <summary>
        /// เส้นด้ายสีแดง — ให้โอกาสพิเศษ 1 ครั้งในกฎถัดไปหาก OverHeartbeat
        ///
        /// ยังไม่เปิดใช้ตามที่สั่งไว้ (ไม่มีรายการของมันใน RewardSystem มันเลยไม่ถูกสุ่มออกมา)
        /// เก็บค่าไว้ในนี้ตั้งแต่ตอนนี้เพื่อให้วันที่เปิดใช้ ตัวเลขของรางวัลอื่นไม่ขยับ
        /// </summary>
        RedThread,
    }
}
