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

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        canvasGroup.alpha = 0f;
    }

    private void OnDestroy()
    {
        SetAutoDismiss(false);
    }

    // ─────────────────────────── Public API ───────────────────────────

    /// <summary>
    /// แสดง hint ข้อความ
    /// autoDismissOnChatKey=true → ซ่อนเองอัตโนมัติเมื่อผู้เล่นกดปุ่มแชท (key 1)
    /// </summary>
    public void Show(string text, bool autoDismissOnChatKey = false)
    {
        hintText.text = text;
        SetAutoDismiss(autoDismissOnChatKey);
        FadeTo(1f);
    }

    /// <summary>ซ่อน hint (fade out) และยกเลิก auto-dismiss subscription</summary>
    public void Hide()
    {
        SetAutoDismiss(false);
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
