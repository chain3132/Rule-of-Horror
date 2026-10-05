using TMPro;
using UnityEngine;

/// <summary>
/// หนึ่งบรรทัดของคำใบ้ปุ่ม — ปุ่มได้หลายอันต่อคำอธิบายเดียว
///
/// มีไว้เพราะในดีไซน์หลายบรรทัดเป็นแบบ "หลายปุ่ม ความหมายเดียว":
///   [1] [2] [3] เปิดแอป      [W] [S] เลือก      [A] [D] ดูกฎข้อก่อน
/// ถ้าใช้ KeyCapView ตัวเดียวจะได้ปุ่มเดียว เลยต้องมีตัวคุมแถวมาแจกให้ช่องปุ่มทีละอัน
///
/// เขียนหลายปุ่มด้วยการคั่นด้วย | เช่น "1|2|3" — ไม่ใช้ช่องว่างคั่นเพราะปุ่มบางอันมีช่องว่าง
/// ในชื่อตัวเอง ("คลิกซ้าย ค้าง" ต้องเป็นปุ่มเดียว ไม่ใช่สองปุ่ม)
///
/// ใส่ช่องปุ่มไว้ใน prefab เผื่อมากกว่าที่ใช้จริงได้ ช่องที่เกินจะถูกซ่อนเอง
/// </summary>
[DisallowMultipleComponent]
public class KeyHintRow : MonoBehaviour
{
    public const char Separator = '|';

    [Tooltip("ช่องปุ่มในแถวนี้ เรียงซ้ายไปขวา — เตรียมไว้มากกว่าที่ใช้จริงได้ ตัวเกินจะถูกซ่อน")]
    [SerializeField] private KeyCapView[] keyCaps;

    [Tooltip("คำอธิบายท้ายแถว เช่น เปิดแอป / เก็บโทรศัพท์")]
    [SerializeField] private TMP_Text actionText;

    /// <summary>ใส่ข้อมูลลงแถวนี้ — keys คั่นหลายปุ่มด้วย | เช่น "1|2|3"</summary>
    public void Set(string keys, string action)
    {
        var parts = string.IsNullOrEmpty(keys)
                  ? System.Array.Empty<string>()
                  : keys.Split(Separator);

        if (keyCaps != null)
        {
            int used = 0;
            for (int i = 0; i < keyCaps.Length; i++)
            {
                if (keyCaps[i] == null) continue;

                bool show = used < parts.Length && !string.IsNullOrWhiteSpace(parts[used]);
                keyCaps[i].gameObject.SetActive(show);

                // คำอธิบายเป็นของแถว ไม่ใช่ของปุ่ม — ปุ่มในแถวจึงส่ง action ว่างเสมอ
                if (show) keyCaps[i].Set(parts[used].Trim(), string.Empty);
                used++;
            }
        }

        if (actionText == null) return;

        bool hasAction = !string.IsNullOrEmpty(action);
        actionText.gameObject.SetActive(hasAction);
        if (hasAction) actionText.text = action;
    }

    public void Set(RuleKeyHint hint)
    {
        if (hint == null) { Set(string.Empty, string.Empty); return; }
        Set(hint.key, hint.action);
    }
}
