using ScriptableObject;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ปุ่ม 1 อันบนจอ — กรอบปุ่ม (ภาพหรือตัวหนังสือ) + คำอธิบายว่ามันทำอะไร
///
/// แปะไว้ที่รากของ prefab แถวเดียว แล้วทุกที่ที่ต้องโชว์ปุ่ม (การ์ดมือของคุณในแอปกฎ,
/// prompt ลอยข้างวัตถุ, แถบมุมซ้ายล่าง) ใช้ prefab แบบเดียวกันหมด เรียกแค่ Set(key, action)
///
/// ตัดสินใจเองว่าจะวาดเป็นภาพหรือตัวหนังสือ:
///   มีภาพในตาราง → โชว์ Image ปิด TMP
///   ไม่มี         → โชว์ TMP ปิด Image   (เช่น "คลิกซ้าย ค้าง" ที่ไม่มีวันเป็นไอคอนปุ่มเดียว)
/// เลยค่อยๆ ใส่ภาพทีละปุ่มได้ ไม่ต้องรอให้ครบทุกปุ่มก่อนถึงจะใช้ระบบนี้ได้
/// </summary>
[DisallowMultipleComponent]
public class KeyCapView : MonoBehaviour
{
    [Header("ช่องที่จะเติม")]
    [Tooltip("ภาพของปุ่ม — ใช้ตอนปุ่มในตารางเก็บไว้เป็น Sprite")]
    [SerializeField] private Image iconImage;

    [Tooltip("ที่ให้ prefab ของปุ่มไปเกิด — ใช้ตอนปุ่มในตารางเก็บไว้เป็น Prefab\n" +
             "เว้นว่างจะใช้ตัว KeyCapView นี้เองเป็นที่เกิด")]
    [SerializeField] private Transform iconSlot;

    [Tooltip("ตัวหนังสือในกรอบปุ่ม — ใช้ตอนปุ่มนั้นยังไม่มีภาพ")]
    [SerializeField] private TMP_Text keyText;

    [Tooltip("คำอธิบายข้างปุ่ม เช่น เปิดแอป / จับสายสิญจน์ — เว้นว่าง = ซ่อนไปเลย")]
    [SerializeField] private TMP_Text actionText;

    [Tooltip("กรอบพื้นหลังของปุ่ม — ซ่อนด้วยตอนใช้ภาพ ถ้าภาพมีกรอบมาในตัวแล้ว")]
    [SerializeField] private GameObject keyFrame;

    [Header("ตาราง")]
    [Tooltip("ตารางภาพปุ่ม — เว้นว่างจะใช้ Assets/Resources/KeyIcons.asset")]
    [SerializeField] private KeyIconLibrary icons;

    [Tooltip("✓ = ย่อภาพปุ่มให้คงสัดส่วนเดิม ไม่ยืดตามกรอบ")]
    [SerializeField] private bool preserveIconAspect = true;

    [Tooltip("✓ = ภาพ/prefab ของปุ่มมีกรอบวาดมาในตัวแล้ว เลยซ่อน Key Frame ตอนใช้มัน")]
    [SerializeField] private bool iconReplacesFrame = true;

    private KeyIconLibrary Icons => icons != null ? icons : KeyIconLibrary.Default;
    private Transform      Slot  => iconSlot != null ? iconSlot : transform;

    private GameObject _spawned;       // ตัวที่ Instantiate มาจริง
    private GameObject _spawnedFrom;   // prefab ต้นทางของมัน — ใช้เช็กว่าต้องสร้างใหม่ไหม

    /// <summary>ปุ่มกับคำอธิบายที่กำลังโชว์อยู่ — เผื่ออยากเช็กก่อนสั่งเปลี่ยนซ้ำ</summary>
    public string Key    { get; private set; }
    public string Action { get; private set; }

    /// <summary>ใส่ข้อมูลลงปุ่มนี้ — action เว้นว่างได้ (ตอนสอนครบแล้วเหลือแค่ปุ่ม)</summary>
    public void Set(string key, string action)
    {
        // ปุ่มเดี่ยวได้ข้อความแบบหลายปุ่มมา ("1|2|3") แปลว่า prefab นี้ไม่มี KeyHintRow —
        // ทำให้อ่านออกไว้ก่อน ดีกว่าโชว์ขีดคั่นดิบๆ ให้ผู้เล่นเห็น
        if (!string.IsNullOrEmpty(key) && key.IndexOf(KeyHintRow.Separator) >= 0)
            key = key.Replace(KeyHintRow.Separator, ' ');

        Key    = key;
        Action = action;

        var entry = Icons != null ? Icons.Resolve(key) : null;

        // prefab มาก่อน Sprite — ปุ่มที่ประกอบจากหลายชิ้นมีข้อมูลมากกว่าภาพแบนๆ อยู่แล้ว
        bool usePrefab = entry?.prefab != null;
        bool useSprite = !usePrefab && entry?.icon != null && iconImage != null;
        bool hasVisual = usePrefab || useSprite;

        ApplyPrefab(usePrefab ? entry.prefab : null);

        if (iconImage != null)
        {
            iconImage.gameObject.SetActive(useSprite);
            if (useSprite)
            {
                iconImage.sprite         = entry.icon;
                iconImage.preserveAspect = preserveIconAspect;
                iconImage.enabled        = true;
            }
        }

        if (keyText != null)
        {
            bool showText = !hasVisual && !string.IsNullOrEmpty(key);
            keyText.gameObject.SetActive(showText);
            if (showText) keyText.text = key;
        }

        if (keyFrame != null)
            keyFrame.SetActive(!(hasVisual && iconReplacesFrame) && !string.IsNullOrEmpty(key));

        if (actionText != null)
        {
            bool showAction = !string.IsNullOrEmpty(action);
            actionText.gameObject.SetActive(showAction);
            if (showAction) actionText.text = action;
        }
    }

    /// <summary>
    /// สลับ prefab ของปุ่มในช่องไอคอน — prefab เดิมใช้ซ้ำ ไม่สร้างใหม่
    ///
    /// สำคัญกับแถบมุมซ้ายล่างที่เรียก Set ทุกครั้งที่สถานการณ์เปลี่ยน:
    /// ถ้า Destroy/Instantiate ทุกครั้งจะได้ขยะกองโตโดยที่ภาพบนจอไม่เปลี่ยนเลย
    /// </summary>
    private void ApplyPrefab(GameObject prefab)
    {
        if (_spawnedFrom == prefab)
        {
            if (_spawned != null) _spawned.SetActive(prefab != null);
            return;
        }

        if (_spawned != null) Destroy(_spawned);
        _spawned     = null;
        _spawnedFrom = prefab;

        if (prefab == null) return;

        _spawned = Instantiate(prefab, Slot);
        _spawned.transform.SetAsFirstSibling();   // ให้อยู่หลังตัวหนังสือที่อาจจะมีในช่องเดียวกัน
        KeepAuthoredSize(prefab, _spawned);
    }

    /// <summary>
    /// บอก layout group ว่าปุ่มนี้ควรกว้าง-สูงเท่าที่วาดไว้ใน prefab
    ///
    /// prefab ปุ่มเป็น UI ธรรมดา ไม่มี LayoutElement — พอไปอยู่ใต้ layout group ที่คุมขนาดลูก
    /// มันจะถูกถามว่าอยากได้ขนาดเท่าไร แล้วตอบไม่ได้ เลยโดนบีบเหลือ 0x0
    /// ลูกที่ anchor แบบ stretch (พื้นหลังปุ่ม) จะหายไปเลย ส่วนลูกที่ขนาดตายตัว (ตัวอักษร)
    /// ยังอยู่ — กลายเป็นปุ่มที่มีแต่ตัวหนังสือลอยๆ ไม่มีกรอบ
    ///
    /// แก้ที่นี่ทีเดียวแทนที่จะต้องไปแปะ LayoutElement ใส่ prefab ปุ่มทุกอันเอง
    /// ถ้า prefab ไหนใส่ LayoutElement มาเองแล้ว จะไม่ไปทับค่าที่ตั้งไว้
    /// </summary>
    private static void KeepAuthoredSize(GameObject prefab, GameObject instance)
    {
        var source = prefab.transform as RectTransform;
        if (source == null) return;

        var element = instance.GetComponent<LayoutElement>();
        if (element == null) element = instance.AddComponent<LayoutElement>();

        if (element.preferredWidth  < 0f) element.preferredWidth  = source.rect.width;
        if (element.preferredHeight < 0f) element.preferredHeight = source.rect.height;
    }

    public void Set(RuleKeyHint hint)
    {
        if (hint == null) { Set(string.Empty, string.Empty); return; }
        Set(hint.key, hint.action);
    }

    /// <summary>
    /// เติมข้อมูลลงแถวหนึ่ง ไม่ว่าแถวนั้นจะมี KeyCapView หรือเป็น prefab เก่าที่มีแต่ TMP 2 ตัว
    ///
    /// มีไว้เพื่อให้ prefab เดิมที่ทำไว้ก่อนระบบภาพปุ่มยังใช้ได้ต่อโดยไม่ต้องรื้อ —
    /// พอแปะ KeyCapView ลงไปเมื่อไร มันจะเปลี่ยนไปใช้ภาพให้เองโดยไม่ต้องแก้โค้ดฝั่งเรียก
    /// </summary>
    public static void Apply(GameObject row, string key, string action)
    {
        if (row == null) return;

        // แถวที่รองรับหลายปุ่ม ("1|2|3 เปิดแอป") ต้องมาก่อน ไม่งั้นจะได้ปุ่มเดียว
        var multi = row.GetComponentInChildren<KeyHintRow>(true);
        if (multi != null) { multi.Set(key, action); return; }

        var view = row.GetComponentInChildren<KeyCapView>(true);
        if (view != null) { view.Set(key, action); return; }

        var texts = row.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length > 0) texts[0].text = key;
        if (texts.Length > 1)
        {
            bool showAction = !string.IsNullOrEmpty(action);
            texts[1].gameObject.SetActive(showAction);
            if (showAction) texts[1].text = action;
        }
    }
}
