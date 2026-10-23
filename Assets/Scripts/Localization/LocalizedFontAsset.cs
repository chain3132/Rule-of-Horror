using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

/// <summary>
/// สลับฟอนต์ของ TMP_Text ตามภาษาที่เลือก — อ่านฟอนต์จาก Asset Table "Fonts"
///
/// ไทยกับอังกฤษใช้ฟอนต์คนละตัว: ฟอนต์อังกฤษของเกม (LiberationSans / Special Elite / Inter)
/// ไม่มีตัวอักษรไทยเลย ข้อความไทยจะขึ้นเป็นสี่เหลี่ยม และฟอนต์ไทยส่วนใหญ่ก็ไม่ได้หน้าตา
/// แบบพิมพ์ดีดหลอนๆ ของ Special Elite — จึงต้องสลับทั้งตัว ไม่ใช่ใส่ fallback ต่อท้าย
///
/// แต่ละป้ายชี้ไปที่ "บทบาท" ของฟอนต์ (Font/Body, Font/Typewriter, Font/Inter) ไม่ได้ชี้ไปที่
/// ฟอนต์ตรงๆ — เปลี่ยนฟอนต์ไทยของบทบาทไหนก็แก้ช่องเดียวในตาราง ป้ายทุกอันที่ใช้บทบาทนั้น
/// ตามไปเอง ตัวนี้ถูกใส่ให้อัตโนมัติโดย Rule of Horror ▸ Localization ▸ 3. Localize Scenes
///
/// ตอนกลับมาภาษาอังกฤษ จะคืน "ฟอนต์กับ material ตัวเดิมของป้ายนี้" แทนที่จะใช้ฟอนต์จากตาราง
/// ตรงๆ — ฟอนต์อังกฤษในตารางเป็นสำเนา (Addressables โหลดของในโฟลเดอร์ Resources ไม่ได้ จึงต้อง
/// copy LiberationSans ออกมา) ถ้าใช้สำเนา ป้ายที่ตั้ง material preset ไว้ (ขอบ/เงา) จะหลุดกลับ
/// เป็น material ปกติของสำเนา ทั้งที่ภาษาไม่ได้เปลี่ยนอะไรเลย
///
/// ภาษาที่ใช้ตัดสินมาจากแพ็กเกจ (LocalizedAssetBehaviour ฟัง SelectedLocale ให้เอง)
/// </summary>
[AddComponentMenu("Localization/Asset/Localized Font Asset")]
[RequireComponent(typeof(TMP_Text))]
public class LocalizedFontAsset : LocalizedAssetBehaviour<TMP_FontAsset, LocalizedTmpFont>
{
    private TMP_Text      _text;
    private TMP_FontAsset _originalFont;
    private Material      _originalMaterial;
    private bool          _captured;

    private void Capture()
    {
        if (_captured) return;
        _captured = true;

        _text = GetComponent<TMP_Text>();
        if (_text == null) return;

        _originalFont     = _text.font;
        _originalMaterial = _text.fontSharedMaterial;
    }

    protected override void UpdateAsset(TMP_FontAsset font)
    {
        // เฉพาะตอนเล่น — ตัวสลับภาษาตอนแก้ฉาก (Localization Scene Controls) ก็เรียกตัวนี้ ถ้าสลับฟอนต์
        // ใน Edit mode แล้วกดเซฟ ฟอนต์ไทยจะถูกบันทึกลงซีนเป็น "ฟอนต์เดิม" ของป้ายไปเลย
        if (!Application.isPlaying) return;

        Capture();
        if (_text == null) return;

        // ยังไม่ได้ใส่ฟอนต์ของภาษานี้ในตาราง = คงฟอนต์เดิมไว้ ดีกว่าเปลี่ยนเป็น null แล้วข้อความหาย
        if (font == null) return;

        // ฟอนต์ในตารางเป็นตัวเดียวกับ (หรือสำเนาชื่อเดียวกันของ) ฟอนต์เดิมของป้าย = ภาษาที่ป้ายนี้
        // ถูกออกแบบมา คืนของเดิมทั้งฟอนต์และ material
        if (_originalFont != null && (font == _originalFont || font.name == _originalFont.name))
        {
            if (_text.font != _originalFont) _text.font = _originalFont;
            if (_originalMaterial != null && _text.fontSharedMaterial != _originalMaterial)
                _text.fontSharedMaterial = _originalMaterial;
            return;
        }

        if (_text.font != font) _text.font = font;
    }
}
