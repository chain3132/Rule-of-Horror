using TMPro;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// ปุ่มตัวเลือกคำตอบในแชท — ถือช่องข้อความกับป้ายเลขปุ่มไว้ให้ ChatUIController เซ็ต
    ///
    /// ทำไมต้องมีแทนที่จะคว้าเอาเอง: เดิม ChatUIController ใช้ GetComponentInChildren&lt;TextMeshProUGUI&gt;()
    /// ซึ่งคว้าตัวแรกที่เจอในลำดับ hierarchy พอเพิ่มป้ายเลขเข้าไปเป็นลูกอีกตัว มันมีสิทธิ์
    /// คว้าป้ายเลขมาใส่ข้อความคำตอบแทน แล้วปุ่มจะกลายเป็นตัวเลขล้วนโดยไม่มีอะไรฟ้อง
    /// ชี้ช่องตรงๆ จากตัวนี้แล้วสลับกันไม่ได้
    ///
    /// ป้ายเลขมีไว้เพราะจอโทรศัพท์ถ่ายลง RenderTexture เมาส์คลิกปุ่มในนั้นไม่ได้
    /// ปุ่มตัวเลขบนคีย์บอร์ดจึงเป็นทางเดียวที่เลือกคำตอบได้ ต้องบอกผู้เล่นให้ชัด
    /// </summary>
    public class ReplyButtonView : MonoBehaviour
    {
        /// <summary>ปุ่มตัวเลขที่ InputHandler ผูกไว้จริงมีแค่ 1-4 เกินนี้กดไม่ได้</summary>
        public const int MaxUsableKey = 4;

        [Tooltip("ข้อความของคำตอบ")]
        [SerializeField] private TMP_Text replyText;

        [Tooltip("ป้ายบอกว่าต้องกดเลขไหนถึงจะได้คำตอบนี้ — เว้นว่างได้ถ้าไม่อยากโชว์")]
        [SerializeField] private TMP_Text keyLabel;

        /// <summary>
        /// ตั้งข้อความและเลขปุ่มของคำตอบนี้
        /// </summary>
        /// <param name="text">ข้อความที่ผู้เล่นจะเห็นบนปุ่ม</param>
        /// <param name="keyNumber">เลขที่ต้องกดจริงๆ เริ่มที่ 1 ไม่ใช่ index ในลิสต์</param>
        public void Set(string text, int keyNumber)
        {
            if (replyText != null) replyText.text = text;
            if (keyLabel == null) return;

            // ตัวเลือกที่ 5 ขึ้นไปไม่มีปุ่มให้กด ซ่อนป้ายไปเลยดีกว่าโชว์เลขที่กดแล้วไม่เกิดอะไร
            bool usable = keyNumber >= 1 && keyNumber <= MaxUsableKey;

            keyLabel.gameObject.SetActive(usable);
            if (usable) keyLabel.text = keyNumber.ToString();
        }
    }
}
