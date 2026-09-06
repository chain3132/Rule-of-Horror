using System.Collections;
using InputSystem;
using TMPro;
using UnityEngine;

/// <summary>
/// แสดง key hint overlay ให้ผู้เล่ย (เช่น "TAB หยิบโทรศัพท์", "E ลุกขึ้น")
///
/// การใช้งาน:
///   GameHintUI.instance.Show("TAB  หยิบโทรศัพท์\n1    เปิดแชท");
///   GameHintUI.instance.Show(text, autoDismissOnChatKey: true);  // ซ่อนเองเมื่อกดปุ่มแชท
///   GameHintUI.instance.Hide();
/// </summary>
public class GameHintUI : MonoBehaviour
{
    public static GameHintUI instance;

    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text hintText;

    [Header("Input (for auto-dismiss)")]
    [Tooltip("ใส่ InputHandler จาก scene — ใช้ตรวจจับปุ่มแชทเพื่อซ่อน hint อัตโนมัติ\n" +
             "เว้นว่างได้ถ้าไม่ต้องการ auto-dismiss")]
    [SerializeField] private InputHandler inputHandler;

    [Header("Fade")]
    [SerializeField] private float fadeInSpeed  = 4f;
    [SerializeField] private float fadeOutSpeed = 3f;

    private Coroutine _fadeCoroutine;
    private bool      _autoDismissOnChat;
    private bool      _phoneOnboarding;
    private string    _phoneStage2Text;

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        canvasGroup.alpha = 0f;
    }

    private void OnDestroy()
    {
        SetAutoDismiss(false);
        SetPhoneOnboarding(false);
    }

    // ─────────────────────────── Public API ───────────────────────────

    /// <summary>
    /// แสดง hint ข้อความ
    /// autoDismissOnChatKey=true → ซ่อนเองอัตโนมัติเมื่อผู้เล่นกดปุ่มแชท (key 1)
    /// </summary>
    public void Show(string text, bool autoDismissOnChatKey = false)
    {
        SetPhoneOnboarding(false);   // Show ปกติ = ยกเลิก onboarding ที่อาจค้าง
        hintText.text = text;
        SetAutoDismiss(autoDismissOnChatKey);
        FadeTo(1f);
    }

    /// <summary>
    /// Onboarding โทรศัพท์แบบ 2 สเต็ป:
    ///   สเต็ป 1 = beforeOpenText (เช่น "TAB เปิดโทรศัพท์")
    ///   สเต็ป 2 = afterOpenText (เช่น "1 เปิดแชท\n2 ไฟฉาย") — สลับให้อัตโนมัติเมื่อผู้เล่นเปิดโทรศัพท์ครั้งแรก
    ///   จากนั้นซ่อนเองเมื่อกดปุ่มแชท (key 1)
    /// </summary>
    public void ShowPhoneOnboarding(string beforeOpenText, string afterOpenText)
    {
        _phoneStage2Text = afterOpenText;
        SetAutoDismiss(false);
        SetPhoneOnboarding(true);
        hintText.text = beforeOpenText;
        FadeTo(1f);
    }

    /// <summary>ซ่อน hint (fade out) และยกเลิก subscription ทั้งหมด</summary>
    public void Hide()
    {
        SetAutoDismiss(false);
        SetPhoneOnboarding(false);
        FadeTo(0f);
    }

    // ─────────────────────────── Auto-Dismiss ───────────────────────────

    private void SetAutoDismiss(bool enable)
    {
        if (_autoDismissOnChat == enable) return;
        _autoDismissOnChat = enable;

        if (inputHandler == null) return;

        if (enable)
            inputHandler.OnAppKeyPressed += HandleAppKey;
        else
            inputHandler.OnAppKeyPressed -= HandleAppKey;
    }

    private void HandleAppKey(int index)
    {
        if (index == 0) // key 1 = แชท
            Hide();
    }

    // ─────────────────────────── Phone Onboarding (2 สเต็ป) ───────────────────────────

    private void SetPhoneOnboarding(bool enable)
    {
        if (_phoneOnboarding == enable) return;
        _phoneOnboarding = enable;

        if (inputHandler == null) return;

        if (enable)
            inputHandler.OnPhoneToggle += HandlePhoneOpened;
        else
            inputHandler.OnPhoneToggle -= HandlePhoneOpened;
    }

    private void HandlePhoneOpened()
    {
        // toggle แรกระหว่าง onboarding = ผู้เล่นเปิดโทรศัพท์ → ไปสเต็ป 2
        SetPhoneOnboarding(false);
        Show(_phoneStage2Text, autoDismissOnChatKey: true);
    }

    // ─────────────────────────── Fade ───────────────────────────

    private void FadeTo(float target)
    {
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(target));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float speed = target > 0f ? fadeInSpeed : fadeOutSpeed;
        while (Mathf.Abs(canvasGroup.alpha - target) > 0.01f)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, speed * Time.deltaTime);
            yield return null;
        }
        canvasGroup.alpha = target;
        _fadeCoroutine = null;
    }
}
