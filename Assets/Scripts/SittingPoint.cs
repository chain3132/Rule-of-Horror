using System;
using InputSystem;
using Player;
using TMPro;
using UnityEngine;

public class SittingPoint : MonoBehaviour
{
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("ระยะ fallback สำหรับตรวจจับผู้เล่นเมื่อ trigger ยิงไม่ได้\n" +
             "(เช่น ผู้เล่นถูก skip intro มานั่งเลย → อยู่ layer PlayerSitting → trigger ไม่ทำงาน)")]
    [SerializeField] private float proximityRadius = 2.5f;

    private PlayerController _player;
    private bool _isSubscribed = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            TrySubscribe(other.GetComponent<PlayerController>());
    }

    // Fallback: ถ้าผู้เล่น "นั่งอยู่แล้ว" (layer = PlayerSitting) trigger จะไม่ยิง เช่นตอน skip intro
    // มานั่งเลยโดยไม่ได้เดินผ่านโซน — เช็คระยะแทนเฉพาะเคสนี้ (ปกติปล่อยให้ trigger จัดการ)
    private void Update()
    {
        if (_isSubscribed) return;

        var pc = PlayerController.Instance;
        if (pc == null || !pc.IsSitting()) return;

        if (Vector3.Distance(transform.position, pc.transform.position) <= proximityRadius)
            TrySubscribe(pc);
    }

    private void TrySubscribe(PlayerController player)
    {
        if (_isSubscribed || player == null) return;

        _player = player;
        if (interactionText != null) interactionText.gameObject.SetActive(true);

        if (inputHandler != null)
        {
            inputHandler.OnInteractPressed += Interact;
            _isSubscribed = true;
        }
        else
        {
            Debug.LogWarning("[SittingPoint] inputHandler is NULL — cannot subscribe Interact", this);
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
