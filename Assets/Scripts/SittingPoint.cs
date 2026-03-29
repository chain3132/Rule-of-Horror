using System;
using InputSystem;
using Player;
using TMPro;
using UnityEngine;

public class SittingPoint : MonoBehaviour
{
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private InputHandler inputHandler;
    private PlayerController _player;
    private bool _isSubscribed = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !_isSubscribed)
        {
            _player = other.GetComponent<PlayerController>();
            interactionText.gameObject.SetActive(true);
            
            if (inputHandler != null)
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
            _player = null; 
        }
    }
    
    public void Interact()
    {
        if (_player == null) return;

        if (_player.IsSitting()) 
        {
            _player.StartStandingSequence();
        }
        else 
        {
            _player.StartSittingSequence(this.transform);
        }
    }

    
    
}
