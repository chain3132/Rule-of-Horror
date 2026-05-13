using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    [TextArea(1, 3)]
    public string text;
    [Tooltip("วินาทีที่แสดงหลังพิมพ์ครบ ใช้ -1 ให้ระบบคำนวณอัตโนมัติจากความยาวข้อความ")]
    public float holdDuration = -1f;
}

/// <summary>
/// ระบบบทพูดผู้เล่น — monologue / ความคิดใน
/// ใช้ได้จากทุกที่: CutsceneManager, Rule1, Rule2, Rule3 หรือ script ใดก็ตาม
///
/// การใช้งาน:
///   PlayerDialogueUI.instance.ShowLine("ข้อความ");
///   PlayerDialogueUI.instance.ShowSequence(dialogueLinesArray);
///   PlayerDialogueUI.instance.OnSequenceComplete += callback;
/// </summary>
public class PlayerDialogueUI : MonoBehaviour
{
    public static PlayerDialogueUI instance;

    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text dialogueText;

    [Header("Typing Settings")]
    [SerializeField] private float typingSpeed = 0.045f;

    [Header("Fade Settings")]
    [SerializeField] private float fadeInSpeed = 3f;
    [SerializeField] private float fadeOutSpeed = 2f;

    [Header("Auto Duration")]
    [Tooltip("วินาทีต่อตัวอักษร สำหรับคำนวณเวลาแสดงอัตโนมัติ (holdDuration = -1)")]
    [SerializeField] private float durationPerChar = 0.07f;
    [SerializeField] private float minHoldDuration = 1.5f;

    /// <summary>เรียกเมื่อ sequence ทุก line จบครบ</summary>
    public event Action OnSequenceComplete;

    private Coroutine _currentRoutine;
    private readonly Queue<DialogueLine> _queue = new Queue<DialogueLine>();

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        canvasGroup.alpha = 0f;
        dialogueText.text = string.Empty;
    }

    #region Public API

    /// <summary>แสดงบทพูดบรรทัดเดียว</summary>
    public void ShowLine(string text, float holdDuration = -1f)
    {
        ShowSequence(new[] { new DialogueLine { text = text, holdDuration = holdDuration } });
    }

    /// <summary>แสดงบทพูดหลายบรรทัดตามลำดับ จบแล้ว fire OnSequenceComplete</summary>
    public void ShowSequence(DialogueLine[] lines)
    {
        if (_currentRoutine != null) StopCoroutine(_currentRoutine);
        _queue.Clear();
        foreach (var line in lines) _queue.Enqueue(line);
        _currentRoutine = StartCoroutine(SequenceRoutine());
    }

    /// <summary>แสดงบทพูดหลายบรรทัด (แบบ string[] สะดวก)</summary>
    public void ShowSequence(string[] lines)
    {
        var converted = new DialogueLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            converted[i] = new DialogueLine { text = lines[i] };
        ShowSequence(converted);
    }

    /// <summary>ซ่อนบทพูดและหยุด sequence ปัจจุบันทันที</summary>
    public void Hide()
    {
        if (_currentRoutine != null) StopCoroutine(_currentRoutine);
        _queue.Clear();
        _currentRoutine = StartCoroutine(FadeOutRoutine());
    }

    #endregion

    #region Routines

    private IEnumerator SequenceRoutine()
    {
        while (_queue.Count > 0)
            yield return ShowLineRoutine(_queue.Dequeue());

        _currentRoutine = null;
        OnSequenceComplete?.Invoke();
    }

    private IEnumerator ShowLineRoutine(DialogueLine line)
    {
        yield return FadeInRoutine();

        dialogueText.text = string.Empty;
        foreach (char c in line.text)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        float hold = line.holdDuration < 0
            ? Mathf.Max(minHoldDuration, line.text.Length * durationPerChar)
            : line.holdDuration;
        yield return new WaitForSeconds(hold);

        yield return FadeOutRoutine();
    }

    private IEnumerator FadeInRoutine()
    {
        while (canvasGroup.alpha < 1f)
        {
            canvasGroup.alpha += Time.deltaTime * fadeInSpeed;
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    private IEnumerator FadeOutRoutine()
    {
        while (canvasGroup.alpha > 0f)
        {
            canvasGroup.alpha -= Time.deltaTime * fadeOutSpeed;
            yield return null;
        }
        canvasGroup.alpha = 0f;
        dialogueText.text = string.Empty;
    }

    #endregion
}
