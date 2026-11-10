using System.Collections;
using Enum;
using RuleSystem;
using UnityEngine;

/// <summary>
/// ปุ่มกลับเมนูหลักตอนเกมจบ — โผล่เองหลังบทจบของกฎ 6 (Trap / Escape / Death) และหลังตายถาวรในคืนรอบสอง
/// บทจบ Loop (ขึ้นรถคันที่ 4) ไม่นับ เพราะเกมยังเล่นต่อ (วนคืนเดิม)
///
/// ใส่ตัวนี้ไว้บน Canvas ที่เปิดอยู่ตลอด แล้วให้ Panel (ที่มีปุ่ม MainMenuButton) ปิดไว้ — สร้างให้ด้วย
/// เมนู Rule of Horror ▸ Localization ▸ Add Main Menu Buttons
///
/// รอสักพักก่อนโผล่ (Delay) ให้จอบทจบ/ข้อความตอนตายได้ขึ้นก่อน ไม่งั้นปุ่มจะแย่งสายตาตั้งแต่วินาทีแรก
/// และปลดเมาส์ให้กดได้ — ตลอดเกมเมาส์ถูกล็อกไว้กลางจอ
/// </summary>
public class GameOverMenu : MonoBehaviour
{
    [Tooltip("แผงที่มีปุ่มกลับเมนูหลัก — ปิดไว้ตอนเริ่ม script จะเปิดเองตอนเกมจบ")]
    [SerializeField] private GameObject panel;

    [Tooltip("ใช้ค่อยๆ โผล่ — เว้นว่าง = โผล่ทันที")]
    [SerializeField] private CanvasGroup fadeGroup;

    [Tooltip("กฎ 6 — ฟังบทจบ (เว้นว่าง = หาในซีนเอง)")]
    [SerializeField] private RuleSystem.Rule.Rule6 rule6;

    [Tooltip("รอกี่วินาทีหลังเกมจบก่อนปุ่มโผล่ (นับเวลาจริง ไม่ขึ้นกับ timeScale)")]
    [SerializeField] private float delay = 2f;

    [Tooltip("เวลาค่อยๆ โผล่ (วินาที)")]
    [SerializeField] private float fadeDuration = 1f;

    [Header("โผล่ตอนไหน")]
    [SerializeField] private bool showOnEndings        = true;
    [SerializeField] private bool showOnPermanentDeath = true;

    /// <summary>ปุ่มกลับเมนูหลักขึ้นจออยู่</summary>
    public bool IsShowing { get; private set; }

    private Coroutine _routine;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (rule6 == null) rule6 = FindAnyObjectByType<RuleSystem.Rule.Rule6>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        PermanentDeath.OnPermanentDeath += HandlePermanentDeath;
        if (rule6 != null) rule6.OnEnding += HandleEnding;
    }

    private void OnDisable()
    {
        PermanentDeath.OnPermanentDeath -= HandlePermanentDeath;
        if (rule6 != null) rule6.OnEnding -= HandleEnding;
    }

    private void HandleEnding(Rule6BusKind kind)
    {
        if (showOnEndings && kind != Rule6BusKind.Loop) Show();
    }

    private void HandlePermanentDeath()
    {
        if (showOnPermanentDeath) Show();
    }

    /// <summary>เปิดปุ่มกลับเมนูหลัก (เรียกเองจากที่อื่นได้)</summary>
    public void Show()
    {
        if (IsShowing || panel == null) return;
        IsShowing = true;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        if (fadeGroup != null) fadeGroup.alpha = 0f;
        panel.SetActive(true);

        if (fadeGroup != null)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeDuration));
                yield return null;
            }
            fadeGroup.alpha = 1f;
        }

        _routine = null;
    }

    private void LateUpdate()
    {
        // ระบบอื่น (เมนู Esc ตอนปิด, ผู้เล่น) ล็อกเมาส์คืนได้ — ระหว่างปุ่มขึ้นจอต้องคลิกได้เสมอ
        if (!IsShowing || panel == null || !panel.activeInHierarchy) return;
        if (InGameLanguageMenu.IsOpen) return;

        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }
}
