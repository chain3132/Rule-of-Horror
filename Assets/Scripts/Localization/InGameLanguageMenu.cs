using InputSystem;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// เมนูหยุดเกมในฉากเล่น — กด Esc เปิด/ปิด มีปุ่มเปลี่ยนภาษา (LanguageSwitcher) กับปุ่มกลับเข้าเกม
///
/// ตอนเปิด:
///   หยุดเวลา (Time.timeScale = 0) + หยุดเสียง FMOD ทั้งหมด — กฎแต่ละข้อมีตัวนับเวลาตายอยู่
///   (กฎ 1 ออกนอกเขต 6 วิ = ตาย) ถ้าเปลี่ยนภาษาแล้วเวลายังเดิน ผู้เล่นจะตายใต้เมนู
///   ปิด InputHandler — ตัวนั้นรับทั้งเดิน มอง และทุกปุ่ม (TAB/1/2/3/E) ปิดตัวเดียวจบ
///   ปลดเมาส์ให้คลิกปุ่มได้ — โทรศัพท์ในเกมใช้แต่คีย์บอร์ด เมาส์จึงถูกล็อกไว้ตลอดเกม
///
/// ตอนปิด: คืนทุกอย่าง "เป็นค่าที่จำไว้ตอนเปิด" ไม่ใช่ค่าตายตัว — ถ้าตอนเปิดเมนู
/// InputHandler ถูกระบบอื่นปิดอยู่แล้ว (เช่นฉากเปิด) ต้องไม่ไปเปิดมันคืนให้
///
/// เรื่อง timeScale: GameModeController ก็เขียน timeScale เอง (ช่วงกะพริบตาหรี่เหลือ 0.6)
/// ถ้าเปิดเมนูกลางการกะพริบ coroutine ของมันจะเขียนทับค่า 0 ของเรา จึงต้องยืนยัน 0 ซ้ำใน
/// LateUpdate ทุกเฟรม (LateUpdate รันหลัง coroutine) แล้วตอนปิดคืนค่าที่จำไว้ ให้มันทำงานต่อ
/// </summary>
public class InGameLanguageMenu : MonoBehaviour
{
    [Header("หน้าเมนู")]
    [Tooltip("แผงเมนูทั้งแผ่น — ปิดไว้ตอนเริ่ม script จะเปิดเองตอนกด Esc")]
    [SerializeField] private GameObject panel;

    [Tooltip("ปุ่มกลับเข้าเกม — เว้นว่างได้ (กด Esc ซ้ำก็กลับได้)")]
    [SerializeField] private Button resumeButton;

    [Header("ระบบที่ต้องหยุดตอนเปิดเมนู")]
    [Tooltip("ตัวรับปุ่มของผู้เล่น — เว้นว่าง = หาในซีนเอง")]
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("✗ = ห้ามเปิดระหว่างฉากเปิดเกม (ฉากนั้นมี Space ข้ามอยู่แล้ว และกำลังขยับกล้องอยู่)")]
    [SerializeField] private bool allowDuringCutscene = false;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => IsOpen = false;

    private float         _savedTimeScale = 1f;
    private bool           _savedInputEnabled;
    private CursorLockMode _savedLock;
    private bool           _savedCursorVisible;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (inputHandler == null) inputHandler = FindAnyObjectByType<InputHandler>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Close);

        // object ถูกปิด/ทำลายระหว่างเมนูเปิดอยู่ (เช่นโหลดซีนใหม่) — ต้องไม่ทิ้งเกมไว้ในสภาพหยุด
        if (IsOpen) Close();
    }

    private void Update()
    {
        if (!EscapePressed()) return;

        if (IsOpen) Close();
        else if (allowDuringCutscene || !CutsceneManager.IsPlaying) Open();
    }

    private void LateUpdate()
    {
        if (IsOpen && Time.timeScale != 0f) Time.timeScale = 0f;
    }

    public void Open()
    {
        if (IsOpen || panel == null) return;
        IsOpen = true;

        _savedTimeScale = Time.timeScale;
        Time.timeScale  = 0f;
        FMODUnity.RuntimeManager.PauseAllEvents(true);

        if (inputHandler != null)
        {
            _savedInputEnabled   = inputHandler.enabled;
            inputHandler.enabled = false;
        }

        _savedLock          = Cursor.lockState;
        _savedCursorVisible = Cursor.visible;
        Cursor.lockState    = CursorLockMode.None;
        Cursor.visible      = true;

        panel.SetActive(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        if (panel != null) panel.SetActive(false);

        Time.timeScale = _savedTimeScale;
        FMODUnity.RuntimeManager.PauseAllEvents(false);

        if (inputHandler != null) inputHandler.enabled = _savedInputEnabled;

        Cursor.lockState = _savedLock;
        Cursor.visible   = _savedCursorVisible;
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
        return false;
    }
}
