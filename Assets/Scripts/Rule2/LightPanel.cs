using InputSystem;
using TMPro;
using UnityEngine;

namespace Rule2
{
    /// <summary>
    /// ตู้ไฟ — ตรวจจับผู้เล่นเข้า/ออก trigger zone และส่ง input ไปยัง LeverPuzzleController
    ///
    /// E key ที่ panel trigger:
    ///   - ครั้งแรก (puzzle ยังไม่เปิด) → StartPuzzle()
    /// ปุ่มกายภาพบนตู้ (PuzzleButton):
    ///   - ปุ่มเขียว (Confirm) → ConfirmCurrentLever()
    ///   - ปุ่มแดง  (Reset)   → ResetPhase()
    /// </summary>
    [RequireComponent(typeof(LeverPuzzleController))]
    public class LightPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light                  panelLight;
        [SerializeField] private LeverPuzzleController  puzzleController;

        private void Awake()
        {
            // auto-fetch ถ้ายังไม่ได้ assign ใน Inspector
            if (puzzleController == null)
                puzzleController = GetComponent<LeverPuzzleController>();

            if (interactionText != null) interactionText.gameObject.SetActive(false);
        }

        /// <summary>
        /// ปุ่ม "E ซ่อมตู้ไฟ" ขึ้นเฉพาะตอนอยู่ใกล้ + ถึงเฟสซ่อม + ยังไม่ได้ซ่อม + ไม่ได้เปิด puzzle อยู่
        /// เช็คทุกเฟรมเพราะ puzzle ปิดตัวเองได้หลายทาง (เดินออก / ตอบผิด / ตาย)
        /// </summary>
        private void Update()
        {
            if (_playerInZone && _isUnlocked && !IsFixed && !puzzleController.IsPuzzleRunning)
                _prompt.Set(PromptId, promptAction);
            else
                _prompt.Clear();
        }

        private void OnDisable() => _prompt.Clear();

        private const string PromptId = "interact.panel";

        [Header("Interaction Text")]
        [Tooltip("ข้อความลอยในโลกแบบเดิม — ไม่ใช้แล้ว (ปุ่มขึ้นบนจอแทน) ใส่ไว้ก็จะถูกซ่อนตลอด")]
        [SerializeField] private TMP_Text interactionText;
        [Tooltip("คำที่ขึ้นข้างปุ่ม E บนจอ ตอนเข้าใกล้ตู้ก่อนเริ่ม puzzle")]
        [SerializeField] private string   promptAction = "ซ่อมตู้ไฟ";

        private readonly InteractPromptHandle _prompt = new InteractPromptHandle();

        // ─── Runtime ───────────────────────────────────────────────────

        /// <summary>true หลังจากซ่อมตู้ไฟสำเร็จแล้ว</summary>
        public bool IsFixed { get; private set; }

        private InputHandler _inputHandler;
        private bool         _playerInZone;
        private bool         _isUnlocked;

        // ─── Rule Setup ───────────────────────────────────────────────────

        /// <summary>เรียกโดย Rule2 หลัง spawn</summary>
        public void SetRule(RuleSystem.Rule.Rule2 ruleRef, InputHandler inputRef)
        {
            _inputHandler = inputRef;
            puzzleController.Initialize(ruleRef, this);   // ส่งตัวเองไปด้วยเพื่อระบุว่าตู้ไหน
        }

        public void UnlockPanel(bool value)
        {
            _isUnlocked = value;
            if (value && _playerInZone && !IsFixed)
                Subscribe();
        }

        public void SetActiveLight(bool state)
        {
            if (panelLight != null) panelLight.enabled = state;
        }

        // ─── Trigger Zone ───────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInZone = true;
            if (_isUnlocked && !IsFixed) Subscribe();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInZone = false;
            _prompt.Clear();
            Unsubscribe();
            puzzleController.ExitPuzzle();
        }

        private void OnDestroy() => Unsubscribe();

        // ─── Input ───────────────────────────────────────────────────

        private void Subscribe()
        {
            if (_inputHandler == null) return;
            _inputHandler.OnInteractPressed += OnInteract;
        }

        private void Unsubscribe()
        {
            if (_inputHandler == null) return;
            _inputHandler.OnInteractPressed -= OnInteract;
        }

        /// <summary>
        /// ผู้เล่นตาย — ล็อกตู้ไม่ให้กด E เปิดใหม่ระหว่างฉากล้ม แล้วปิด puzzle ที่เปิดค้างอยู่
        ///
        /// ระหว่างฉากตายผู้เล่นยังยืนอยู่ใน trigger ของตู้ ถ้ากด E (ปุ่มเดียวกับลุก/หยิบ) puzzle จะเปิด
        /// อีกรอบ ปลดเมาส์ออกมาและล็อกตัวผู้เล่น ทับฉากตายที่กำลังเล่นอยู่ ตื่นมาเมาส์ค้างบนจอ
        /// </summary>
        public void LockForDeath()
        {
            _isUnlocked = false;
            Unsubscribe();
            _prompt.Clear();
            puzzleController.AbortForDeath();
        }

        private void OnInteract()
        {
            if (IsFixed || !_isUnlocked) return;
            if (puzzleController.IsPuzzleRunning) return; // ปุ่มบนตู้จัดการ confirm/reset เอง

            // เปิด puzzle ครั้งแรก
            puzzleController.StartPuzzle();
            _prompt.Clear(); // ซ่อนปุ่มหลังเปิด puzzle (ระหว่าง puzzle ปุ่มบนตู้จัดการเอง)
        }

        // ─── Panel Fixed (callback path จาก Rule2.OnPanelFixed ผ่าน LeverPuzzleController) ───

        /// <summary>เรียกโดย Rule2 เมื่อตู้นี้ถูกซ่อมเสร็จ (เพื่ออัปเดต visual สุดท้าย)</summary>
        public void MarkFixed()
        {
            IsFixed = true;
            Unsubscribe();
            _prompt.Clear();
            if (panelLight != null) panelLight.color = Color.green;
        }
    }
}
