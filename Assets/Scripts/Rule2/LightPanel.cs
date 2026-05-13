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
        }

        [Header("Interaction Text")]
        [Tooltip("Text แสดง hint เมื่อผู้เล่นอยู่ใน zone (เว้นว่างถ้าไม่ต้องการ)")]
        [SerializeField] private TMP_Text interactionText;
        [Tooltip("ข้อความก่อนเริ่ม puzzle")]
        [SerializeField] private string   textBeforePuzzle = "E  ซ่อมตู้ไฟ";

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
            // แสดง hint เฉพาะก่อนเริ่ม puzzle (ระหว่าง puzzle ปุ่มบนตู้จัดการเอง)
            if (!puzzleController.IsPuzzleRunning)
                ShowText(textBeforePuzzle);
            if (_isUnlocked && !IsFixed) Subscribe();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInZone = false;
            ShowText(null);
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

        private void OnInteract()
        {
            if (IsFixed) return;
            if (puzzleController.IsPuzzleRunning) return; // ปุ่มบนตู้จัดการ confirm/reset เอง

            // เปิด puzzle ครั้งแรก
            puzzleController.StartPuzzle();
            ShowText(null); // ซ่อน hint หลังเปิด puzzle
        }

        // ─── Panel Fixed (callback path จาก Rule2.OnPanelFixed ผ่าน LeverPuzzleController) ───

        /// <summary>เรียกโดย Rule2 เมื่อตู้นี้ถูกซ่อมเสร็จ (เพื่ออัปเดต visual สุดท้าย)</summary>
        public void MarkFixed()
        {
            IsFixed = true;
            Unsubscribe();
            ShowText(null);
            if (panelLight != null) panelLight.color = Color.green;
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private void ShowText(string msg)
        {
            if (interactionText == null) return;
            if (string.IsNullOrEmpty(msg))
                interactionText.gameObject.SetActive(false);
            else
            {
                interactionText.text = msg;
                interactionText.gameObject.SetActive(true);
            }
        }
    }
}
