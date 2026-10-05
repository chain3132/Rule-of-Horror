using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// วาด prompt ปุ่มที่ KeyPromptManager สั่งมา ให้ลอยอยู่ติดกับวัตถุนั้นบนจอ
///
/// ใช้ canvas แบบ Screen Space แล้วฉายตำแหน่งโลกมาเป็นพิกัดจอเอง ไม่ได้ใช้ world-space canvas
/// ต่อวัตถุ เพราะแบบนั้นขนาดตัวอักษรจะเปลี่ยนตามระยะ อ่านยากเวลายืนไกล และต้องไปแปะ canvas
/// ไว้กับทุกวัตถุที่กดได้ ซึ่งคือปัญหาเดิมที่ระบบนี้มาแก้
///
/// prompt หลายอันขึ้นพร้อมกันได้ (ยืนตรงที่เห็นทั้งสวิตช์และวิทยุ) ตัวนี้จัดการเป็นรายตัวตาม id
/// </summary>
public class KeyPromptBinder : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("RectTransform ของ Canvas ที่จะให้ prompt ไปเกิด (Screen Space - Overlay หรือ Camera)")]
    [SerializeField] private RectTransform promptRoot;

    [Tooltip("prefab ของ prompt 1 อัน — ต้องมี TMP_Text 2 ตัว (ตัวแรก = ปุ่ม, ตัวที่สอง = คำอธิบาย) " +
             "และควรมี CanvasGroup ไว้หรี่ตอนสอนครบแล้ว")]
    [SerializeField] private GameObject promptPrefab;

    [Header("ตำแหน่ง")]
    [Tooltip("กล้องที่ใช้ฉายพิกัด — เว้นว่างจะใช้ Camera.main")]
    [SerializeField] private Camera worldCamera;

    [Tooltip("ยกขึ้นจากจุดกึ่งกลางวัตถุกี่เมตร ไม่ให้ prompt ไปทับตัววัตถุเอง")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.25f, 0f);

    [Tooltip("prompt ที่ไม่ได้ผูกกับวัตถุไหน จะไปอยู่ตรงนี้ (สัดส่วนของจอ 0-1)")]
    [SerializeField] private Vector2 screenCenterFallback = new Vector2(0.5f, 0.25f);

    [Header("หน้าตา")]
    [Tooltip("ความทึบตอนสอนครบ 3 ครั้งแล้ว (เหลือแค่ปุ่ม)")]
    [Range(0f, 1f)]
    [SerializeField] private float fadedAlpha = 0.45f;

    [Tooltip("ซ่อน prompt ที่อยู่หลังกล้อง แทนที่จะให้มันเด้งไปโผล่อีกฝั่งจอ")]
    [SerializeField] private bool hideWhenBehindCamera = true;

    [Tooltip("ดัน prompt ที่อยู่ริมจอกลับเข้ามาให้อ่านครบ — ไม่งั้นวัตถุที่อยู่ริมภาพจะโดนขอบจอตัดครึ่ง")]
    [SerializeField] private bool keepOnScreen = true;

    [Tooltip("เว้นจากขอบจอกี่พิกเซลตอนดันกลับเข้ามา")]
    [SerializeField] private float screenMargin = 24f;

    [Tooltip("✓ = ไม่โผล่ระหว่างฉากเปิด — ผู้เล่นยังกดอะไรไม่ได้ ขึ้นไปก็บังภาพเปล่าๆ")]
    [SerializeField] private bool hideDuringCutscene = true;

    private readonly Dictionary<string, GameObject> _views = new Dictionary<string, GameObject>();

    // ─────────────────────────── Lifecycle ───────────────────────────

    private bool _subscribed;

    private void OnEnable() => TrySubscribe();

    /// <summary>
    /// ลองสมัครอีกรอบตอน Start — ถึงตรงนี้ Awake ของทุก component ในซีนรันครบแล้วแน่นอน
    /// (OnEnable อย่างเดียวไม่พอ ลำดับระหว่าง component ไม่การันตี แม้อยู่บน GameObject เดียวกัน)
    /// </summary>
    private void Start()
    {
        WarnIfRootDrivesChildren();

        if (TrySubscribe()) return;

        Debug.LogError("[KeyPrompt] ไม่เจอ KeyPromptManager ในซีน — prompt จะไม่ขึ้นเลย", this);
    }

    /// <summary>
    /// Layout Group บน Prompt Root ทำให้ระบบนี้ใช้ไม่ได้ และมันพังแบบเงียบมาก
    ///
    /// ตัวนี้วางตำแหน่งใน LateUpdate แต่ Layout Group คำนวณตอน Canvas จะวาด ซึ่งเกิดทีหลัง —
    /// ตำแหน่งที่คำนวณไว้เลยถูกเขียนทับทุกเฟรม prompt จะไปกองเรียงกันในช่องของ Layout Group
    /// แทนที่จะเกาะวัตถุ และตัวกันหลุดขอบจอก็ไม่มีผลเพราะผลลัพธ์ถูกทิ้งไปแล้ว
    /// </summary>
    private void WarnIfRootDrivesChildren()
    {
        if (promptRoot == null) return;

        var group = promptRoot.GetComponent<LayoutGroup>();
        if (group == null) return;

        Debug.LogError(
            $"[KeyPrompt] '{promptRoot.name}' มี {group.GetType().Name} ติดอยู่ — prompt จะไม่เกาะวัตถุ " +
            "เพราะ Layout Group ลากมันกลับไปเรียงในช่องของตัวเองทุกเฟรม\n" +
            "Prompt Root ต้องเป็น RectTransform เปล่าๆ ที่ไม่มี Layout Group " +
            "(ถ้าอยากได้แถบปุ่มเรียงมุมจอ อันนั้นเป็นหน้าที่ของ ContextPromptBar คนละตัวกัน)",
            promptRoot);
    }

    private bool TrySubscribe()
    {
        if (_subscribed) return true;

        var manager = KeyPromptManager.instance;
        if (manager == null) return false;

        manager.OnPromptShown  += Spawn;
        manager.OnPromptHidden += Despawn;
        _subscribed = true;

        // เพิ่งเปิดมาแต่มี prompt ค้างอยู่แล้ว — วาดตามให้ทัน
        foreach (var prompt in manager.Active.Values) Spawn(prompt);
        return true;
    }

    private void OnDisable()
    {
        var manager = KeyPromptManager.instance;
        if (_subscribed && manager != null)
        {
            manager.OnPromptShown  -= Spawn;
            manager.OnPromptHidden -= Despawn;
        }
        _subscribed = false;

        foreach (var view in _views.Values) if (view != null) Destroy(view);
        _views.Clear();
    }

    /// <summary>ตามวัตถุใน LateUpdate — กล้องขยับเสร็จแล้ว prompt จะได้ไม่สั่นตามหลังหนึ่งเฟรม</summary>
    private void LateUpdate()
    {
        if (_views.Count == 0 || KeyPromptManager.instance == null) return;

        // ฉากเปิดจบแล้ว Place() จะเปิดกลับมาให้เอง ไม่ต้องจำสถานะว่าใครเคยโชว์อยู่
        if (hideDuringCutscene && CutsceneManager.IsPlaying)
        {
            foreach (var view in _views.Values)
                if (view != null && view.activeSelf) view.SetActive(false);
            return;
        }

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;

        foreach (var pair in KeyPromptManager.instance.Active)
        {
            if (!_views.TryGetValue(pair.Key, out var view) || view == null) continue;
            Place(view, pair.Value.anchor, cam);
        }
    }

    // ─────────────────────────── Spawn / Place ───────────────────────────

    private void Spawn(KeyPromptManager.Prompt prompt)
    {
        if (promptRoot == null || promptPrefab == null) return;

        if (!_views.TryGetValue(prompt.id, out var view) || view == null)
        {
            view = Instantiate(promptPrefab, promptRoot);
            _views[prompt.id] = view;
        }

        // Apply จัดการให้ทั้งกรณี prefab มีภาพปุ่ม (KeyCapView/KeyHintRow) และ prefab เก่าที่มีแต่ TMP
        // และปิดช่องคำอธิบายให้เองตอนสอนครบแล้ว ไม่งั้นกรอบจะกว้างค้างไว้เปล่าๆ
        KeyCapView.Apply(view, prompt.key, prompt.action);

        // บังคับคำนวณขนาดเดี๋ยวนี้ ไม่ต้องรอรอบวาดถัดไป — ไม่งั้นเฟรมแรกตัวกันหลุดขอบจอ
        // จะวัดความกว้างได้เป็นขนาดใน prefab (ที่ยังไม่ใช่ขนาดจริง) แล้ววางผิดไปหนึ่งเฟรม
        if (view.transform is RectTransform viewRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewRect);

        var group = view.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = prompt.faded ? fadedAlpha : 1f;

        // เกิดระหว่างฉากเปิด — ซ่อนไว้ตั้งแต่แรก ไม่ให้แวบขึ้นมาหนึ่งเฟรมก่อน LateUpdate จะเก็บ
        if (hideDuringCutscene && CutsceneManager.IsPlaying) { view.SetActive(false); return; }

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam != null) Place(view, prompt.anchor, cam);
    }

    private void Despawn(string id)
    {
        if (!_views.TryGetValue(id, out var view)) return;

        if (view != null) Destroy(view);
        _views.Remove(id);
    }

    private void Place(GameObject view, Transform anchor, Camera cam)
    {
        var rect = view.transform as RectTransform;
        if (rect == null) return;

        if (anchor == null)
        {
            rect.position = new Vector3(Screen.width  * screenCenterFallback.x,
                                        Screen.height * screenCenterFallback.y, 0f);
            if (!view.activeSelf) view.SetActive(true);
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(anchor.position + worldOffset);

        // z < 0 = วัตถุอยู่หลังกล้อง WorldToScreenPoint จะคืนพิกัดกลับด้าน prompt จะไปโผล่ผิดที่
        bool visible = !hideWhenBehindCamera || screen.z > 0f;
        if (view.activeSelf != visible) view.SetActive(visible);
        if (!visible) return;

        screen.z      = 0f;
        rect.position = keepOnScreen ? ClampToScreen(rect, screen) : screen;
    }

    /// <summary>
    /// ดันจุดวางให้กรอบทั้งอันยังอยู่ในจอ
    ///
    /// pivot ของแถวมักเป็น 0.5 แปลว่าครึ่งกรอบอยู่ซ้ายของจุดที่วาง — วัตถุที่อยู่ริมภาพจึงทำให้
    /// ครึ่งซ้ายของ prompt หลุดออกนอกจอ เห็นเป็นตัวหนังสือโดนตัดกองอยู่ที่มุม
    ///
    /// แถวที่กว้างเกินจอไปเลยจะถูกชิดซ้ายไว้ ดีกว่าให้มันไหลออกทั้งสองข้าง
    /// </summary>
    private Vector3 ClampToScreen(RectTransform rect, Vector3 screen)
    {
        Vector2 size = Vector2.Scale(rect.rect.size, rect.lossyScale);

        float minX = screenMargin + size.x * rect.pivot.x;
        float maxX = Screen.width  - screenMargin - size.x * (1f - rect.pivot.x);
        float minY = screenMargin + size.y * rect.pivot.y;
        float maxY = Screen.height - screenMargin - size.y * (1f - rect.pivot.y);

        screen.x = maxX >= minX ? Mathf.Clamp(screen.x, minX, maxX) : minX;
        screen.y = maxY >= minY ? Mathf.Clamp(screen.y, minY, maxY) : minY;
        return screen;
    }
}
