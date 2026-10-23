using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ปุ่ม ไทย / English — ใช้ทั้งหน้าเมนูหลักและเมนูในเกม
///
/// กดแล้วแค่บอก L10n ว่าเลือกภาษาไหน ส่วนการจำค่าไว้ข้ามการเปิดเกม และการเปลี่ยนข้อความ
/// กับฟอนต์ทั้งเกม เป็นงานของแพ็กเกจ Localization ตัวนี้ไม่ได้ถือ "ภาษาปัจจุบัน" ไว้เอง
/// ปุ่มจึงไม่มีทางแสดงภาษาไม่ตรงกับที่เกมใช้อยู่จริง
///
/// ชื่อภาษาบนปุ่มไม่ถูกแปลโดยตั้งใจ — "ไทย" ต้องเขียนเป็นไทยและ "English" เป็นอังกฤษเสมอ
/// คนที่อ่านภาษาปัจจุบันไม่ออกต้องหาปุ่มของภาษาตัวเองเจอ
/// </summary>
public class LanguageSwitcher : MonoBehaviour
{
    [Header("ปุ่ม")]
    [SerializeField] private Button thaiButton;
    [SerializeField] private Button englishButton;

    [Header("ภาษาที่เลือกอยู่")]
    [Tooltip("ความทึบของปุ่มภาษาที่ \"ไม่ได้\" เลือกอยู่ — ปุ่มที่เลือกอยู่ทึบเต็ม")]
    [Range(0f, 1f)]
    [SerializeField] private float unselectedAlpha = 0.4f;

    [Tooltip("ขีดเส้นใต้/กรอบของปุ่มที่เลือกอยู่ — เว้นว่างได้ (ใช้แค่ความทึบบอก)")]
    [SerializeField] private GameObject thaiSelectedMark;
    [SerializeField] private GameObject englishSelectedMark;

    private void OnEnable()
    {
        if (thaiButton    != null) thaiButton.onClick.AddListener(SetThai);
        if (englishButton != null) englishButton.onClick.AddListener(SetEnglish);

        L10n.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (thaiButton    != null) thaiButton.onClick.RemoveListener(SetThai);
        if (englishButton != null) englishButton.onClick.RemoveListener(SetEnglish);

        L10n.Changed -= Refresh;
    }

    public void SetThai()    => L10n.SetLanguage(L10n.ThaiCode);
    public void SetEnglish() => L10n.SetLanguage(L10n.EnglishCode);

    private void Refresh()
    {
        bool thai = L10n.IsThai;

        SetLook(thaiButton,    thai);
        SetLook(englishButton, !thai);

        if (thaiSelectedMark    != null) thaiSelectedMark.SetActive(thai);
        if (englishSelectedMark != null) englishSelectedMark.SetActive(!thai);
    }

    private void SetLook(Button button, bool selected)
    {
        if (button == null) return;

        float alpha = selected ? 1f : unselectedAlpha;

        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            // ป้ายตัวหนังสือหรี่ตาม แต่พื้นหลังปุ่มไม่หรี่ — ไม่งั้นปุ่มที่ไม่ได้เลือกจะดูเหมือนกดไม่ได้
            if (!(graphic is TMP_Text)) continue;

            var c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
