namespace Enum
{
    /// <summary>
    /// "ตอนนี้ผู้เล่นอยู่ในสถานการณ์ไหน" สำหรับแถบคำใบ้ปุ่มมุมซ้ายล่าง
    ///
    /// ไม่ได้ใช้ PhoneState ตรงๆ เพราะสองอย่างนี้ไม่ใช่เรื่องเดียวกัน:
    ///   • หน้าแชทมีสองโหมด (รออ่าน / มีตัวเลือกคำตอบค้างอยู่) ซึ่งปุ่มที่กดได้ต่างกัน
    ///     แต่เป็น PhoneState.ChatView เหมือนกัน
    ///   • ตอนเก็บโทรศัพท์ ผู้เล่นยังมีปุ่มให้กดอยู่ (เดิน/หยิบ) ซึ่ง PhoneState ไม่รู้เรื่องด้วย
    /// </summary>
    public enum PromptContext
    {
        /// <summary>เก็บโทรศัพท์แล้ว — เดินอยู่ในโลก</summary>
        World,

        /// <summary>ยกโทรศัพท์ขึ้น อยู่หน้าโฮม</summary>
        PhoneHome,

        /// <summary>แอปข้อความ หน้ารายชื่อ</summary>
        FriendList,

        /// <summary>อยู่ในแชท แต่ยังไม่มีตัวเลือกคำตอบ</summary>
        Chat,

        /// <summary>อยู่ในแชท และมีตัวเลือกคำตอบค้างอยู่ — เลข 1/2 เปลี่ยนความหมาย</summary>
        ChatReply,

        /// <summary>แอปไฟฉาย</summary>
        FlashLight,

        /// <summary>แอปกฎ</summary>
        RuleBook,
    }
}
