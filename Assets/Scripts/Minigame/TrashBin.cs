using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// ถังขยะ — เป้าของมินิเกมปาขยะ
///
/// ต้องมี Collider ที่ติ๊ก Is Trigger อยู่บน GameObject เดียวกับสคริปต์นี้ (เป็น "โซนก้นถัง")
/// ส่วนผนังถังเป็น collider ธรรมดาของลูกๆ ไม่เกี่ยวกัน — ลูกที่ปาชนขอบถังแล้วเด้งออกจึงไม่นับ
/// นับเฉพาะลูกที่เข้าไปถึงในถังจริงๆ
///
/// คะแนนไม่มีผลกับเกมเลยตามดีไซน์ (มินิเกมคลายเครียด ไม่บังคับเล่น) เก็บไว้เพื่อให้ผู้เล่น
/// รู้สึกว่ามีอะไรให้ไล่ตามเท่านั้น สถิติสูงสุดเก็บลง PlayerPrefs แบบเดียวกับมินิเกมงูกินหาง
/// </summary>
public class TrashBin : MonoBehaviour
{
    private const string BestScoreKey = "ROH_Trash_Best";

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("พฤติกรรม")]
    [Tooltip("✓ = ลูกที่ลงถังถูกคืนเข้าลัง (ปาเล่นได้ไม่จบ)\n" +
             "✗ = ลูกหายไปเลย ลังจะพร่องลงเรื่อยๆ จนหมด")]
    [SerializeField] private bool returnBallToBox = true;

    [Tooltip("ลูกลงถังแล้วรอกี่วินาทีก่อนเก็บออก — เผื่อให้เห็นว่ามันลงไปจริง")]
    [SerializeField] private float swallowDelay = 0.4f;

    [Header("คะแนน (ไม่มีผลกับเกม)")]
    [Tooltip("เว้นว่างได้ — ไม่ใส่ก็ยังนับคะแนนอยู่ แค่ไม่มีที่โชว์")]
    [SerializeField] private TMP_Text scoreText;

    [Tooltip("{0} = ลงไปแล้วกี่ลูกรอบนี้, {1} = สถิติสูงสุด")]
    [SerializeField] private string scoreFormat = "ลงถัง {0} ลูก (สูงสุด {1})";

    [Tooltip("โชว์คะแนนค้างไว้กี่วินาทีแล้วค่อยซ่อน — 0 = โชว์ค้างไว้ตลอด")]
    [SerializeField] private float scoreDisplayTime = 3f;

    [Tooltip("✗ = ไม่จำสถิติข้ามรอบเล่น")]
    [SerializeField] private bool rememberBest = true;

    [Header("เสียง (เว้นว่างได้)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   scoreClip;
    [SerializeField, Range(0f, 1f)] private float scoreVolume = 0.6f;

    // ─────────────────────────── State ───────────────────────────

    /// <summary>ลงถังไปแล้วกี่ลูกรอบนี้ (int = คะแนนล่าสุด) — เผื่อระบบอื่นอยากเอาไปใช้</summary>
    public static event Action<int> OnScored;

    public int Score { get; private set; }
    public int Best  { get; private set; }

    private Coroutine _hideScoreRoutine;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (rememberBest) Best = PlayerPrefs.GetInt(BestScoreKey, 0);

        if (scoreText != null) scoreText.gameObject.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        // ก้อนกระดาษมี collider อยู่บนตัวเองก็จริง แต่ถ้าวันหลังเปลี่ยนไปใช้โมเดลที่
        // collider อยู่บนลูก ต้องเดินขึ้นไปหา component ที่แม่ ไม่ใช่ GetComponent เฉยๆ
        var ball = other.GetComponentInParent<PaperBall>();
        if (ball == null || ball.IsHeld || ball.IsScored) return;

        ball.MarkScored();
        Score++;

        if (Score > Best)
        {
            Best = Score;
            if (rememberBest)
            {
                PlayerPrefs.SetInt(BestScoreKey, Best);
                PlayerPrefs.Save();
            }
        }

        if (audioSource != null && scoreClip != null) audioSource.PlayOneShot(scoreClip, scoreVolume);

        ShowScore();
        OnScored?.Invoke(Score);

        StartCoroutine(SwallowRoutine(ball));
    }

    // ─────────────────────────── Internal ───────────────────────────

    private IEnumerator SwallowRoutine(PaperBall ball)
    {
        yield return new WaitForSeconds(swallowDelay);

        if (ball == null) yield break;   // เข้า Tension ระหว่างรอ ลังล้างก้อนทิ้งไปแล้ว

        if (returnBallToBox && ball.Origin != null) ball.Origin.ReturnBall(ball);
        else                                       ball.Despawn();
    }

    private void ShowScore()
    {
        if (scoreText == null) return;

        scoreText.text = string.Format(scoreFormat, Score, Best);
        scoreText.gameObject.SetActive(true);

        if (_hideScoreRoutine != null) StopCoroutine(_hideScoreRoutine);
        if (scoreDisplayTime > 0f) _hideScoreRoutine = StartCoroutine(HideScoreRoutine());
    }

    private IEnumerator HideScoreRoutine()
    {
        yield return new WaitForSeconds(scoreDisplayTime);

        if (scoreText != null) scoreText.gameObject.SetActive(false);
        _hideScoreRoutine = null;
    }
}
