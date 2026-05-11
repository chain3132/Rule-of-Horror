using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MainMenuManager — จัดการหน้า Main Menu
///
/// Flow:
///   Scene เปิด → Fade in → รอ input
///   กด Start   → Fade out → LoadScene
///   กด Quit    → Application.Quit
///
/// Background:
///   รถวิ่งผ่านเป็น loop ซ้ำๆ พร้อมเสียง 3D
///   แต่ละรอบรถจะหยุดพักก่อน (delayBetweenPass)
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ─────────────────────────── Inspector ───────────────────────────

    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    [Header("Scene")]
    [Tooltip("ชื่อ Scene ที่จะโหลดเมื่อกด Start")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Fade")]
    [Tooltip("CanvasGroup บน Panel สีดำเต็มจอ — ใช้ fade in/out")]
    [SerializeField] private CanvasGroup fadeGroup;

    [Tooltip("เวลา fade in ตอนเปิดเมนู (วินาที)")]
    [SerializeField] private float fadeInDuration  = 1.0f;

    [Tooltip("เวลา fade out ก่อนเข้าเกม (วินาที)")]
    [SerializeField] private float fadeOutDuration = 1.0f;

    [Header("Background Car Loop")]
    [Tooltip("GameObject รถ — ปิดไว้ก่อน script จะเปิด/ปิดเอง")]
    [SerializeField] private GameObject carObject;

    [Tooltip("Waypoints ที่รถวิ่งผ่าน — [0] = จุดเริ่ม, [last] = จุดสิ้นสุด")]
    [SerializeField] private Transform[] carWaypoints;

    [Tooltip("ความเร็วรถ (units/วินาที)")]
    [SerializeField] private float carSpeed = 15f;

    [Tooltip("✓ = รถหันหน้าตามทิศทางที่วิ่ง")]
    [SerializeField] private bool carFaceDirection = true;

    [Tooltip("Offset หมุน Y ของ model (องศา) — ปรับตาม pivot\n-90 / 90 / 180")]
    [SerializeField] private float carRotationOffset = -90f;

    [Tooltip("หน่วงก่อนรถวิ่งรอบถัดไป (วินาที)")]
    [SerializeField] private float delayBetweenPass = 4f;

    [Header("Car Sound — เสียง 3D ติดตามตำแหน่งรถ")]
    [Tooltip("FMOD Event path เช่น event:/SFX/CarEngine\nเว้นว่างถ้าไม่ต้องการเสียง")]
    [SerializeField] private string carSoundEvent = "";

    [Header("Background Music")]
    [Tooltip("FMOD Event path เพลง BGM ที่เล่น loop ตลอดหน้า Main Menu\nเว้นว่างถ้าไม่ต้องการ")]
    [SerializeField] private string bgmEvent = "";

    // ─────────────────────────── Runtime ───────────────────────────

    private bool          _isTransitioning;
    private EventInstance _carSoundInstance;
    private EventInstance _bgmInstance;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Start()
    {
        // bind ปุ่ม
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (quitButton  != null) quitButton.onClick.AddListener(OnQuitClicked);

        // ซ่อนรถตั้งแต่ต้น
        if (carObject != null) carObject.SetActive(false);

        // fade in เมื่อเปิด scene
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;
            StartCoroutine(FadeRoutine(1f, 0f, fadeInDuration));
        }

        // เริ่ม BGM loop
        if (!string.IsNullOrWhiteSpace(bgmEvent))
        {
            _bgmInstance = RuntimeManager.CreateInstance(bgmEvent);
            _bgmInstance.start();
        }

        // เริ่ม car loop ถ้ามีข้อมูลครบ
        if (carObject != null && carWaypoints != null && carWaypoints.Length >= 2)
            StartCoroutine(CarLoopRoutine());
    }

    private void OnDestroy()
    {
        StopBgm();
        StopCarSound();
    }

    // ─────────────────────────── Button Handlers ───────────────────────────

    private void OnStartClicked()
    {
        if (_isTransitioning) return;
        StartCoroutine(StartGameRoutine());
    }

    private void OnQuitClicked()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        StopCarSound();

        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ─────────────────────────── Start Game ───────────────────────────

    private IEnumerator StartGameRoutine()
    {
        _isTransitioning = true;

        // หยุดเสียงทั้งหมดก่อน transition
        StopBgm();
        StopCarSound();

        // fade out
        if (fadeGroup != null)
            yield return StartCoroutine(FadeRoutine(0f, 1f, fadeOutDuration));

        SceneManager.LoadScene(gameSceneName);
    }

    // ─────────────────────────── Car Loop ───────────────────────────

    /// <summary>วนรถผ่านซ้ำๆ ไม่หยุด จนกว่าจะออกจาก scene</summary>
    private IEnumerator CarLoopRoutine()
    {
        while (true)
        {
            yield return StartCoroutine(CarPassRoutine());

            // หน่วงก่อนรอบถัดไป
            yield return new WaitForSeconds(delayBetweenPass);
        }
    }

    /// <summary>รถ teleport ไปจุดเริ่ม → วิ่งผ่าน waypoints → ซ่อน</summary>
    private IEnumerator CarPassRoutine()
    {
        Transform carT = carObject.transform;

        // teleport ไป waypoint แรก
        carT.position = carWaypoints[0].position;
        carT.rotation = carWaypoints[0].rotation;
        carObject.SetActive(true);

        // เริ่มเสียง 3D
        bool hasSound = !string.IsNullOrWhiteSpace(carSoundEvent);
        if (hasSound)
        {
            _carSoundInstance = RuntimeManager.CreateInstance(carSoundEvent);
            RuntimeManager.AttachInstanceToGameObject(_carSoundInstance, carT);
            _carSoundInstance.start();
        }

        // วิ่งผ่านทุก waypoint
        for (int i = 1; i < carWaypoints.Length; i++)
        {
            Transform target = carWaypoints[i];

            while (Vector3.Distance(carT.position, target.position) > 0.05f)
            {
                carT.position = Vector3.MoveTowards(
                    carT.position, target.position, carSpeed * Time.deltaTime);

                if (carFaceDirection)
                {
                    Vector3 dir = (target.position - carT.position);
                    dir.y = 0f;
                    if (dir != Vector3.zero)
                        carT.rotation = Quaternion.LookRotation(dir)
                                      * Quaternion.Euler(0f, carRotationOffset, 0f);
                }

                yield return null;
            }

            carT.position = target.position;
        }

        // ถึงปลายทาง → หยุดเสียง + ซ่อนรถ
        StopCarSound();
        carObject.SetActive(false);
    }

    // ─────────────────────────── Sound ───────────────────────────

    private void StopBgm()
    {
        if (_bgmInstance.isValid())
        {
            _bgmInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _bgmInstance.release();
            _bgmInstance = default;
        }
    }

    private void StopCarSound()
    {
        if (_carSoundInstance.isValid())
        {
            _carSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _carSoundInstance.release();
            _carSoundInstance = default;
        }
    }

    // ─────────────────────────── Fade ───────────────────────────

    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (fadeGroup == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed          += Time.deltaTime;
            fadeGroup.alpha   = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        fadeGroup.alpha = to;
    }
}
