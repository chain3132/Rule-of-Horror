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

    /// <summary>
    /// true = ข้อความนี้แปลมาแล้ว อย่าแปลซ้ำ — ใช้กับข้อความที่ต้องแปลก่อนแทนค่า เช่น
    /// "ฟังเสียงระฆังให้ครบ {0} ครั้ง" ที่ผู้เรียกแทน {0} เองแล้วค่อยส่งมา
    /// NonSerialized เพราะเป็นเรื่องของโค้ดที่เรียก ไม่ใช่ช่องที่คนเขียนบทต้องเห็นใน Inspector
    /// </summary>
    [NonSerialized] public bool alreadyTranslated;
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

    /// <summary>
    /// แสดงบทพูดบรรทัดเดียว — ข้อความจะถูกแปลเป็นภาษาที่เลือกอยู่ให้เอง
    /// alreadyTranslated = true ถ้าผู้เรียกแปลและแทนค่า {0} ไปแล้ว (ดู DialogueLine.alreadyTranslated)
    /// </summary>
    public void ShowLine(string text, float holdDuration = -1f, bool alreadyTranslated = false)
    {
        ShowSequence(new[] { new DialogueLine { text = text, holdDuration = holdDuration,
                                                alreadyTranslated = alreadyTranslated } });
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

        // แปลตอนจะพิมพ์ ไม่ใช่ตอนเข้าคิว — บทพูดที่ต่อคิวไว้หลายบรรทัดแล้วผู้เล่นเปลี่ยนภาษากลางทาง
        // บรรทัดที่ยังไม่ขึ้นจะออกมาเป็นภาษาใหม่
        string text = line.alreadyTranslated ? line.text : L10n.T(line.text);
        if (text == null) text = string.Empty;

        dialogueText.text = string.Empty;
        foreach (char c in text)
        {
            dialogueText.text += c;

            // U+200B คือจุดตัดคำของภาษาไทย มองไม่เห็น — ถ้ารอทุกตัวเหมือนตัวอักษร ประโยคไทย
            // จะพิมพ์ช้าลงเป็นจังหวะกระตุกๆ ตามจำนวนคำ
            if (c == '\u200B') continue;
            yield return new WaitForSeconds(typingSpeed);
        }

        float hold = line.holdDuration < 0
            ? Mathf.Max(minHoldDuration, text.Length * durationPerChar)
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
