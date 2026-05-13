using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rule2
{
    /// <summary>
    /// ปุ่มบนตู้ไฟ — มองแล้ว คลิกซ้าย (raycast จากกล้อง)
    ///
    /// Setup:
    ///   1. ติด Collider (ไม่ต้อง IsTrigger) บน GameObject นี้
    ///   2. เลือก ButtonType = Confirm (เขียว) หรือ Reset (แดง)
    ///   3. ลาก LeverPuzzleController + Camera ใส่ Inspector
    ///      (Camera เว้นว่างได้ — จะใช้ Camera.main)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PuzzleButton : MonoBehaviour
    {
        public enum ButtonType { Confirm, Reset }

        [Header("Button Config")]
        [SerializeField] private ButtonType            buttonType;
        [SerializeField] private LeverPuzzleController puzzleController;

        [Header("Raycast")]
        [Tooltip("Camera ที่ใช้ raycast — ถ้าเว้นว่างจะใช้ Camera.main")]
        [SerializeField] private Camera   playerCamera;
        [Tooltip("ระยะมองสูงสุด (เมตร)")]
        [SerializeField] private float    maxLookDistance = 4f;

        [Header("Visual Feedback")]
        [SerializeField] private Renderer buttonRenderer;
        [SerializeField] private Color    normalColor  = Color.white;
        [SerializeField] private Color    hoveredColor = Color.yellow;
        [SerializeField] private Color    pressedColor = Color.gray;
        [SerializeField] private float    pressedDuration = 0.12f;

        // ── Runtime ────────────────────────────────────────────────────

        private Collider _col;
        private bool     _isHovered;
        private bool     _pressing;

        // ── Lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            _col = GetComponent<Collider>();
        }

        private void Start()
        {
            SetColor(normalColor);
        }

        private void Update()
        {
            if (_pressing) return;

            bool nowHovered = IsLookingAt();

            if (nowHovered != _isHovered)
            {
                _isHovered = nowHovered;
                SetColor(_isHovered ? hoveredColor : normalColor);
            }

            // คลิกซ้ายขณะมองโดน → กดปุ่ม
            if (_isHovered && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                OnPress();
            }
        }

        // ── Raycast ────────────────────────────────────────────────────

        private bool IsLookingAt()
        {
            Camera cam = playerCamera != null ? playerCamera : Camera.main;
            if (cam == null || _col == null) return false;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            return Physics.Raycast(ray, out RaycastHit hit, maxLookDistance)
                   && hit.collider == _col;
        }

        // ── Action ────────────────────────────────────────────────────

        private void OnPress()
        {
            if (puzzleController == null || !puzzleController.IsPuzzleRunning || _pressing) return;

            // บอก controller ว่า click นี้ถูกใช้โดยปุ่มแล้ว — อย่า grab lever
            puzzleController.ConsumeClick();

            switch (buttonType)
            {
                case ButtonType.Confirm: puzzleController.ConfirmCurrentLever(); break;
                case ButtonType.Reset:   puzzleController.ResetPhase();          break;
            }

            StartCoroutine(PressFlashRoutine());
        }

        private IEnumerator PressFlashRoutine()
        {
            _pressing = true;
            SetColor(pressedColor);
            yield return new WaitForSeconds(pressedDuration);
            SetColor(_isHovered ? hoveredColor : normalColor);
            _pressing = false;
        }

        // ── Crosshair Hint (OnGUI) ────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_isHovered || _pressing) return;
            if (puzzleController == null || !puzzleController.IsPuzzleRunning) return;

            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            bool   isConfirm = buttonType == ButtonType.Confirm;
            Color  hintColor = isConfirm ? Color.green : new Color(1f, 0.35f, 0.35f);
            string hint      = isConfirm ? "[ คลิก: ยืนยัน ]" : "[ คลิก: รีเซ็ต ]";

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize  = 14,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = hintColor;

            Color prev = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(cx - 99, cy + 14, 200, 28), hint, style);
            GUI.color = hintColor;
            GUI.Label(new Rect(cx - 100, cy + 15, 200, 28), hint, style);
            GUI.color = prev;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private void SetColor(Color c)
        {
            if (buttonRenderer != null)
                buttonRenderer.material.color = c;
        }
    }
}
