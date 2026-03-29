using InputSystem;
using RuleSystem.Rule;
using TMPro;
using UnityEngine;

namespace System
{
    public class SwitchLight : MonoBehaviour
    {
        public RuleSystem.Rule.Rule2 rule2;

        public bool isOn = false;
        [SerializeField] private Light saraLight;
        [SerializeField] private InputHandler inputHandler;
        [SerializeField] private TMP_Text interactionText;
        private bool _isSubscribed = false;
        private bool _isUnlocked = false;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && !_isSubscribed)
            {
                interactionText.gameObject.SetActive(true);
            
                if (inputHandler != null && _isUnlocked)
                {
                    inputHandler.OnInteractPressed += Interact;
                    _isSubscribed = true; 
                }
            }
        }

        
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player") && _isSubscribed)
            {
                interactionText.gameObject.SetActive(false);
            
                if (inputHandler != null )
                {
                    inputHandler.OnInteractPressed -= Interact;
                    _isSubscribed = false; // 
                }
            }
        }
        public void UnlockSwitch(bool value)
        {
            _isUnlocked = value;
        }
        
        public void Interact()
        {
            _isUnlocked = false;

            TurnOnLight();
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
