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
    }
}
