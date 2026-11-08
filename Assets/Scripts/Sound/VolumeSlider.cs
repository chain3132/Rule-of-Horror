using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// แถบปรับเสียงรวม — ใส่คู่กับ UI Slider (ค่า 0..1) ใช้ได้ทั้งเมนู Esc และหน้าเมนูหลัก
/// ตัวเลข % ข้างแถบเว้นว่างได้
///
/// ระหว่างเมนู Esc เปิด เสียง FMOD ถูกหยุดทั้งหมด จึงยังไม่ได้ยินผลตอนลาก — ได้ยินตอนกลับเข้าเกม
/// </summary>
public class VolumeSlider : MonoBehaviour
{
    [Tooltip("แถบ Slider — Min 0, Max 1 (เว้นว่าง = หาบนตัวเอง/ลูก)")]
    [SerializeField] private Slider slider;

    [Tooltip("ตัวเลข % ข้างแถบ — เว้นว่างได้")]
    [SerializeField] private TMP_Text valueText;

    private void Awake()
    {
        if (slider == null) slider = GetComponentInChildren<Slider>(true);
    }

    private void OnEnable()
    {
        MasterVolume.EnsureApplied();

        if (slider != null)
        {
            slider.minValue     = 0f;
            slider.maxValue     = 1f;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(MasterVolume.Value);
            slider.onValueChanged.AddListener(OnSliderChanged);
        }

        MasterVolume.Changed += Refresh;
        Refresh(MasterVolume.Value);
    }

    private void OnDisable()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderChanged);
        MasterVolume.Changed -= Refresh;

        // ลากระหว่างเมนูเปิดไม่ได้เขียนดิสก์ — เขียนทีเดียวตอนเมนูปิด
        MasterVolume.Save();
    }

    private void OnSliderChanged(float value) => MasterVolume.Set(value, save: false);

    private void Refresh(float value)
    {
        if (slider != null && !Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
        if (valueText != null) valueText.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
