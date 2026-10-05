using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ระบบ prompt ปุ่มที่ "สอนแล้วค่อยๆ หายไป" ตามหลักการแสดงปุ่มในดีไซน์
///
///   ครั้งที่ 1-3 : โชว์เต็ม   [E] จับสายสิญจน์
///   ครั้งที่ 4+  : เหลือปุ่มจางๆ   [E]
///
/// ต่างจาก GameHintUI ตรงที่ตัวนั้นเป็นป้ายกลางจอสำหรับสอนครั้งเดียว ส่วนตัวนี้ผูกกับวัตถุ
/// ในโลกและเด้งซ้ำได้เรื่อยๆ ทุกครั้งที่ผู้เล่นเล็งไปโดน
///
/// ตัวนี้ไม่มี UI ในตัวเอง มันเก็บแค่ "ใครเคยเห็นอะไรมาแล้วกี่ครั้ง" แล้วบอกฝั่ง UI ผ่าน event
/// ว่าตอนนี้ควรวาดอะไรตรงไหน — ฝั่ง UI จะวาดเป็น world-space canvas หรือ screen overlay ก็ได้
///
/// จำนวนครั้งนับต่อ promptId ไม่ใช่ต่อวัตถุ: ตุ๊กตา 6 ตัวใช้ id เดียวกัน ผู้เล่นเรียน "E เก็บตุ๊กตา"
/// จากตัวแรกแล้ว ตัวที่ 4 ไม่ต้องสอนซ้ำ แต่ถ้าอยากให้แยกก็ตั้ง id ต่างกันได้
///
/// <example>
/// เล็งโดน:   KeyPromptManager.instance.Show("rule5.grab", "E", "จับสายสิญจน์", thread.transform);
/// เล็งหลุด:  KeyPromptManager.instance.Hide("rule5.grab");
/// </example>
/// </summary>
public class KeyPromptManager : MonoBehaviour
{
    // ─────────────────────────── Inner Types ───────────────────────────

    /// <summary>สิ่งที่ฝั่ง UI ต้องรู้เพื่อวาด prompt หนึ่งอัน</summary>
    public readonly struct Prompt
    {
        public readonly string    id;
        public readonly string    key;        // "E", "Space", "คลิกซ้าย ค้าง"
        public readonly string    action;     // ว่าง = เลยโควตาสอนแล้ว โชว์แค่ปุ่ม
        public readonly bool      faded;      // true = วาดจางๆ
        public readonly Transform anchor;     // วัตถุที่ prompt ต้องไปเกาะ (null = กลางจอ)

        public Prompt(string id, string key, string action, bool faded, Transform anchor)
        {
            this.id = id; this.key = key; this.action = action; this.faded = faded; this.anchor = anchor;
        }
    }

    // ─────────────────────────── Inspector ───────────────────────────

    private static KeyPromptManager _instance;

    /// <summary>
    /// ตัวจัดการในซีน — หาให้เองถ้ายังไม่มีใครเซ็ต
    ///
    /// ไม่พึ่ง Awake อย่างเดียว เพราะลำดับ Awake/OnEnable ระหว่าง component ไม่การันตี
    /// ตัวที่ไปอ่าน instance ใน OnEnable อาจมาถึงก่อน Awake ของตัวนี้ แม้จะอยู่บน GameObject เดียวกัน
    /// </summary>
    public static KeyPromptManager instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindAnyObjectByType<KeyPromptManager>();
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("กติกาการสอน")]
    [Tooltip("โชว์เต็ม (ปุ่ม + คำ) กี่ครั้งแรก หลังจากนั้นเหลือแค่ปุ่มจางๆ")]
    [SerializeField] private int fullDisplayCount = 3;

    [Tooltip("✓ = ซ่อน prompt ไปเลยหลังสอนครบ แทนที่จะเหลือปุ่มจางๆ\n" +
             "ดีไซน์ปัจจุบันให้เหลือปุ่มไว้ เลยปิดไว้เป็นค่าเริ่มต้น")]
    [SerializeField] private bool hideCompletely;

    [Header("Debug")]
    [Tooltip("log ทุกครั้งที่ prompt ถูกเรียก พร้อมจำนวนครั้งที่เคยโชว์")]
    [SerializeField] private bool logPrompts;

    // ─────────────────────────── Events ───────────────────────────

    /// <summary>มี prompt ต้องแสดง (หรือเปลี่ยนเนื้อหา) — ฝั่ง UI วาดตาม Prompt ที่ได้</summary>
    public event Action<Prompt> OnPromptShown;

    /// <summary>prompt นี้ไม่ต้องแสดงแล้ว (string = id)</summary>
    public event Action<string> OnPromptHidden;

    // ─────────────────────────── Runtime ───────────────────────────

    private readonly Dictionary<string, int>    _shownCount = new Dictionary<string, int>();
    private readonly Dictionary<string, Prompt> _active     = new Dictionary<string, Prompt>();

    /// <summary>prompt ที่กำลังแสดงอยู่ตอนนี้ — ใช้ตอน UI เพิ่งเปิดมาแล้วต้อง sync ตาม</summary>
    public IReadOnlyDictionary<string, Prompt> Active => _active;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─────────────────────────── Public API ───────────────────────────

    /// <summary>
    /// ขอให้แสดง prompt — เรียกซ้ำด้วย id เดิมขณะที่มันแสดงอยู่แล้วจะไม่นับเพิ่ม
    /// (เล็งค้างไว้ 5 วินาที = เห็นครั้งเดียว ไม่ใช่ 300 ครั้งตามจำนวนเฟรม)
    /// </summary>
    public void Show(string id, string key, string action, Transform anchor = null)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_active.ContainsKey(id)) return;

        _shownCount.TryGetValue(id, out int seen);
        _shownCount[id] = seen + 1;

        bool taught = seen >= fullDisplayCount;
        if (taught && hideCompletely) return;

        var prompt = new Prompt(id, key, taught ? string.Empty : action, taught, anchor);
        _active[id] = prompt;

        if (logPrompts)
            Debug.Log($"[KeyPrompt] {id} ครั้งที่ {seen + 1} — {(taught ? "ปุ่มอย่างเดียว" : "เต็ม")}", this);

        OnPromptShown?.Invoke(prompt);
    }

    /// <summary>เลิกแสดง prompt นี้ — เรียกตอนผู้เล่นเล็งหลุด หรือทำสิ่งนั้นไปแล้ว</summary>
    public void Hide(string id)
    {
        if (string.IsNullOrEmpty(id) || !_active.Remove(id)) return;
        OnPromptHidden?.Invoke(id);
    }

    /// <summary>เก็บ prompt ที่ค้างอยู่ทั้งหมด — ใช้ตอนตาย จบกฎ หรือเข้าคัตซีน</summary>
    public void HideAll()
    {
        if (_active.Count == 0) return;

        var ids = new List<string>(_active.Keys);
        _active.Clear();
        foreach (var id in ids) OnPromptHidden?.Invoke(id);
    }

    /// <summary>
    /// ลืมว่าเคยสอนอะไรไปแล้ว — เริ่มเกมใหม่ต้องเรียก ไม่งั้นผู้เล่นคนใหม่จะไม่เห็นคำสอน
    /// ใส่ id เพื่อรีเซ็ตเฉพาะอันเดียว (เช่นกฎที่ปุ่มเปลี่ยนความหมาย)
    /// </summary>
    public void ResetTaught(string id = null)
    {
        if (string.IsNullOrEmpty(id)) _shownCount.Clear();
        else                          _shownCount.Remove(id);
    }

    /// <summary>เคยโชว์ prompt นี้ไปกี่ครั้งแล้ว — เผื่ออยากเช็กก่อนทำอย่างอื่น</summary>
    public int TimesShown(string id) => _shownCount.TryGetValue(id, out int n) ? n : 0;
}
