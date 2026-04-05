using System;
using InputSystem;
using TMPro;
using UnityEngine;

namespace Rule2
{
    public class LightPanel : MonoBehaviour
    {
        [SerializeField] private Light panelLight;
        
        [SerializeField] private TMP_Text interactionText;
        private InputHandler inputHandler;
        private bool _isSubscribed = false;
        private bool isFixed = false;
        private bool isUnlocked = false;

        private RuleSystem.Rule.Rule2 rule;

        public void UnlockPanel(bool value)
        {
            isUnlocked = value;
        }
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && !_isSubscribed )
            {
                interactionText.gameObject.SetActive(true);
                if (inputHandler != null  && isUnlocked)
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
            
                if (inputHandler != null)
                {
                    inputHandler.OnInteractPressed -= Interact;
                    _isSubscribed = false; // 
                }
            }
        }
        public void SetRule(RuleSystem.Rule.Rule2 ruleRef,InputHandler inputRef)
        {
            rule = ruleRef;
            inputHandler = inputRef;    
        }

        public void SetActiveLight(bool state)
        {
            panelLight.enabled = state;
            
        }
        public void Interact()
        {
            if (isFixed)
            {
                return;
            }
            isFixed = true;
            panelLight.color = Color.green;
            rule.OnPanelFixed();
        }
    }
}
