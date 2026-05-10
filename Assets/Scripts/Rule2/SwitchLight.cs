using FMODUnity;
using InputSystem;
using RuleSystem.Rule;
using TMPro;
using UnityEngine;

namespace System
{
    public class SwitchLight : MonoBehaviour
    {
        public RuleSystem.Rule.Rule2 rule2;
        [Header("Audio")]
        [SerializeField] private EventReference switchSound;
        public bool isOn = false;
        [SerializeField] private Light saraLight;
        [SerializeField] private InputHandler inputHandler;
        [SerializeField] private TMP_Text interactionText;
        [SerializeField] private GameObject onSwitchObject;
        [SerializeField] private GameObject offSwitchObject;
        private bool _isSubscribed = false;
        private bool _isUnlocked = false;
        private bool _lastLightStatus;
        private bool _isPlayerInTrigger = false;   // ← track ว่า player อยู่ใน zone ไหม

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && !_isSubscribed)
            {
                _isPlayerInTrigger = true;
                interactionText.gameObject.SetActive(true);

                if (inputHandler != null && _isUnlocked)
                {
                    inputHandler.OnInteractPressed += Interact;
                    _isSubscribed = true;
                }
            }
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
            if (other.CompareTag("Player"))
            {
                _isPlayerInTrigger = false;
                interactionText.gameObject.SetActive(false);

                if (inputHandler != null && _isSubscribed)
                {
                    inputHandler.OnInteractPressed -= Interact;
                    _isSubscribed = false;
                }
            }
        }

        private void PlaySwitchSound()
        {
            RuntimeManager.PlayOneShot(switchSound, transform.position);
        }

        public void UnlockSwitch(bool value)
        {
            _isUnlocked = value;

            // ถ้า player ยืนอยู่ใน trigger อยู่แล้วตอน unlock → subscribe ทันทีเลย
            if (_isUnlocked && _isPlayerInTrigger && !_isSubscribed && inputHandler != null)
            {
                inputHandler.OnInteractPressed += Interact;
                _isSubscribed = true;
            }
        }

        private void UpdateVisuals()
        {
            onSwitchObject.SetActive(_lastLightStatus);
            offSwitchObject.SetActive(!_lastLightStatus);
        }

        public void Interact()
        {
            _isUnlocked = false;
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
