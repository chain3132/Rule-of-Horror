using FMODUnity;
using InputSystem;
using RuleSystem.Rule;
using TMPro;
using UnityEngine;

namespace System
{
    /// <summary>
    /// สวิตช์ไฟศาลา — ใช้แค่ในกฎ 2 (ไฟดับครั้งแรก + เดินกลับมาเปิดหลังซ่อมตู้ไฟ)
    ///
    /// ปุ่ม "E เปิดไฟ" ขึ้นบนจอเฉพาะตอนเข้าใกล้ + กฎ 2 ปลดล็อกให้แล้ว
    /// กฎอื่นกดไม่ได้ จึงไม่ขึ้นปุ่มให้ผู้เล่นเข้าใจผิด
    /// </summary>
    public class SwitchLight : MonoBehaviour
    {
        private const string PromptId = "interact.switch";

        public RuleSystem.Rule.Rule2 rule2;
        [Header("Audio")]
        [SerializeField] private EventReference switchSound;
        public bool isOn = false;
        [SerializeField] private Light saraLight;
        [SerializeField] private InputHandler inputHandler;

        [Tooltip("ข้อความลอยในโลกแบบเดิม — ไม่ใช้แล้ว (ปุ่มขึ้นบนจอแทน) ใส่ไว้ก็จะถูกซ่อนตลอด")]
        [SerializeField] private TMP_Text interactionText;

        [Tooltip("คำที่ขึ้นข้างปุ่ม E บนจอ")]
        [SerializeField] private string promptAction = "เปิดไฟ";

        [SerializeField] private GameObject onSwitchObject;
        [SerializeField] private GameObject offSwitchObject;
        private bool _isSubscribed = false;
        private bool _isUnlocked = false;
        private bool _lastLightStatus;
        private bool _isPlayerInTrigger = false;   // ← track ว่า player อยู่ใน zone ไหม
        private readonly InteractPromptHandle _prompt = new InteractPromptHandle();

        private void Awake()
        {
            if (interactionText != null) interactionText.gameObject.SetActive(false);
        }

        // ฟังปุ่มตลอดตอนเปิดอยู่ แล้วเช็คเงื่อนไขตอนกดแทน (เหมือน RadioControl)
        private void OnEnable()
        {
            if (inputHandler != null && !_isSubscribed)
            {
                inputHandler.OnInteractPressed += Interact;
                _isSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (inputHandler != null && _isSubscribed) inputHandler.OnInteractPressed -= Interact;
            _isSubscribed      = false;
            _isPlayerInTrigger = false;
            _prompt.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _isPlayerInTrigger = true;
            RefreshPrompt();
        }

        private void Update()
        {
            if (saraLight.enabled != _lastLightStatus)
            {
                _lastLightStatus = saraLight.enabled;
                UpdateVisuals();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _isPlayerInTrigger = false;
            RefreshPrompt();
        }

        private void PlaySwitchSound()
        {
            RuntimeManager.PlayOneShot(switchSound, transform.position);
        }

        public void UnlockSwitch(bool value)
        {
            _isUnlocked = value;
            RefreshPrompt();
        }

        private void RefreshPrompt()
        {
            if (_isPlayerInTrigger && _isUnlocked) _prompt.Set(PromptId, promptAction);
            else                                   _prompt.Clear();
        }

        private void UpdateVisuals()
        {
            onSwitchObject.SetActive(_lastLightStatus);
            offSwitchObject.SetActive(!_lastLightStatus);
        }

        public void Interact()
        {
            // เดิมกดซ้ำได้เรื่อยๆ หลังเปิดไปแล้ว (เสียงสวิตช์ดังทุกครั้ง) — ตอนนี้กดได้เฉพาะตอนกฎปลดล็อก
            if (!_isPlayerInTrigger || !_isUnlocked) return;

            _isUnlocked = false;
            RefreshPrompt();
            TurnOnLight();
            PlaySwitchSound();
            // ส่ง event ไป Rule
            rule2.OnLightTurnedOn();
        }

        public bool IsLightOn()
        {
            return saraLight.enabled;
        }

        void TurnOnLight()
        {
            saraLight.enabled = true;
        }
    }
}
