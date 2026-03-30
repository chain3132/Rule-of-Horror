using System;
using InputSystem;
using RuleSystem.Rule;
using TMPro;
using UnityEngine;

public class RadioControl : MonoBehaviour
{
    [SerializeField] private InputHandler inputHandler;
    [SerializeField] private TMP_Text interactionText;
    public RuleSystem.Rule.Rule2 rule2;

    private bool isUnlocked = false;
    private bool _isSubscribed = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !_isSubscribed)
        {
            interactionText.gameObject.SetActive(true);
            
            if (inputHandler != null && isUnlocked)
            {
                inputHandler.OnInteractPressed += Interact;
                _isSubscribed = true; 
            }
        }
    }
    public void UnlockRadio(bool value)
    {
        isUnlocked = value;
    }

        
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") )
        {
            interactionText.gameObject.SetActive(false);
            
            if (inputHandler != null && _isSubscribed)
            {
                inputHandler.OnInteractPressed -= Interact;
                _isSubscribed = false; 
            }
        }
    }
        
    public void Interact()
    {
        AudioManager.instance.PlayRadio();
        rule2.OnRadioTurnedOn();
    }
}
