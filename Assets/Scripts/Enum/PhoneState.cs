namespace Enum
{
    public enum PhoneState
    {
        Hidden,
        PhoneRaised,
        AppSelection,
        FriendList,
        ChatView,
        FlashLight,
        Clock,

        /// <summary>
        /// แอปกฎ (ปุ่ม 3) — ที่เก็บกฎที่คนปริศนาส่งมา เปิดอ่านได้ทุกเมื่อ
        /// เข้ามาแทนที่แอปนาฬิกาตามดีไซน์ใหม่ เพราะเวลาย้ายไปอยู่บนแถบสถานะถาวรแล้ว
        /// (Clock ยังอยู่ให้ของเดิมที่อ้างถึงมันไม่พัง)
        /// </summary>
        RuleBook,

        /// <summary>
        /// แอปมินิเกมงูกินหาง (ปุ่ม 4) — ต้องต่อท้าย enum เท่านั้น ห้ามแทรกกลาง
        /// PhoneAppData เก็บ openState เป็นตัวเลขดิบในไฟล์ .asset แทรกกลางทีเดียว
        /// แอปที่ตั้งค่าไว้แล้วทุกตัวจะเลื่อนไปชี้ state ผิดหมดแบบไม่มี error ฟ้อง
        /// </summary>
        Snake,
    }
}
