using System;
using InputSystem;
using Player;
using TMPro;
using UnityEngine;

public class SittingPoint : MonoBehaviour
{
    private const string SitPromptId   = "interact.sit";
    private const string StandPromptId = "interact.stand";

    [Tooltip("ข้อความลอยในโลกแบบเดิม — ไม่ใช้แล้ว (ปุ่มขึ้นบนจอแทน) ใส่ไว้ก็จะถูกซ่อนตลอด")]
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("คำข้างปุ่ม E ตอนยืนอยู่ใกล้ม้านั่ง")]
    [SerializeField] private string sitAction = "นั่ง";

    [Tooltip("คำข้างปุ่ม E ตอนนั่งอยู่ — ขึ้นเฉพาะตอนลุกได้จริง (กฎที่บังคับนั่งจะไม่ขึ้น)")]
    [SerializeField] private string standAction = "ลุก";

    private readonly InteractPromptHandle _prompt = new InteractPromptHandle();

    [Tooltip("ระยะ fallback สำหรับตรวจจับผู้เล่นเมื่อ trigger ยิงไม่ได้\n" +
             "(เช่น ผู้เล่นถูก skip intro มานั่งเลย → อยู่ layer PlayerSitting → trigger ไม่ทำงาน)")]
    [SerializeField] private float proximityRadius = 2.5f;

    private PlayerController _player;
    private bool _isSubscribed = false;
    private Collider _zone;

    private void Awake()
    {
        _zone = GetComponent<Collider>();
        if (interactionText != null) interactionText.gameObject.SetActive(false);
    }

    /// <summary>
    /// ปุ่มบนจอ: ยืนใกล้ม้านั่ง = "E นั่ง" / นั่งอยู่และลุกได้ = "E ลุก"
    /// ไม่ขึ้นตอนกำลังเปลี่ยนท่า, ตอนกฎล็อกการลุก, และตอน E เป็นของสมุดบนโต๊ะ
    /// </summary>
    private void RefreshPrompt(PlayerController pc)
    {
        if (!_isSubscribed || pc == null || pc.IsChangingPose || Book.BookPageTurner.ClaimsInteract)
        {
            _prompt.Clear();
            return;
        }

        if (!pc.IsSitting())            _prompt.Set(SitPromptId, sitAction);
        else if (!pc.isBlockStanding)   _prompt.Set(StandPromptId, standAction);
        else                            _prompt.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            TrySubscribe(other.GetComponent<PlayerController>());
    }

    // Fallback: ถ้าผู้เล่น "นั่งอยู่แล้ว" (layer = PlayerSitting) trigger จะไม่ยิง เช่นตอน skip intro
    // มานั่งเลยโดยไม่ได้เดินผ่านโซน — เช็คระยะแทนเฉพาะเคสนี้ (ปกติปล่อยให้ trigger จัดการ)
    //
    // ขาออกก็พึ่ง OnTriggerExit อย่างเดียวไม่ได้ด้วยเหตุผลเดียวกัน: ตอนนั่ง/ลุก CharacterController
    // ถูกปิดชั่วคราวและ layer ถูกสลับ ผู้เล่นจึงออกจากโซนได้โดยไม่มี OnTriggerExit มาเลย
    // แล้วม้านั่งตัวนี้จะยังฟังปุ่ม E ค้างอยู่ตลอดเกม — กด E ขึ้นรถบัสทีไร ม้านั่งก็ดึงตัวกลับมานั่งที่ศาลาด้วย
    // จึงเช็คระยะขาออกเองทุกเฟรมด้วย
    private void Update()
    {
        var pc = PlayerController.Instance;

        if (_isSubscribed)
        {
            if (pc == null || !IsPlayerInRange(pc)) Unsubscribe();
            RefreshPrompt(pc);
            return;
        }

        if (pc == null || !pc.IsSitting()) return;

        if (Vector3.Distance(transform.position, pc.transform.position) <= proximityRadius)
            TrySubscribe(pc);
        RefreshPrompt(pc);
    }

    private void TrySubscribe(PlayerController player)
    {
        if (_isSubscribed || player == null) return;

        _player = player;

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
            Unsubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        _prompt.Clear();
        if (!_isSubscribed) return;

        if (inputHandler != null) inputHandler.OnInteractPressed -= Interact;

        _isSubscribed = false;
        _player = null;
    }

    /// <summary>
    /// ผู้เล่นยังอยู่ที่ม้านั่งนี้จริงไหม — อยู่ในโซน trigger หรืออยู่ในรัศมี fallback อย่างใดอย่างหนึ่ง
    ///
    /// ใช้สองเงื่อนไขรวมกัน เพราะโซน trigger อาจใหญ่กว่ารัศมี (เดินอยู่ขอบโซนก็ต้องยังนั่งได้)
    /// และตอนนั่งอยู่ ตัวผู้เล่นอยู่ที่เบาะซึ่งอยู่ในรัศมีเสมอ
    /// </summary>
    private bool IsPlayerInRange(PlayerController pc)
    {
        Vector3 p = pc.transform.position;

        if (Vector3.Distance(transform.position, p) <= proximityRadius) return true;

        // ClosestPoint คืนจุดเดิมถ้าอยู่ในโซน — เผื่อระยะไว้นิดหน่อยเพราะตำแหน่งผู้เล่นคือเท้า
        return _zone != null && _zone.enabled
            && (_zone.ClosestPoint(p) - p).sqrMagnitude <= 0.5f * 0.5f;
    }

    public void Interact()
    {
        if (_player == null) return;

        // ผู้เล่นกำลังเล็ง/ถือสมุดโน้ตบนโต๊ะศาลา — E ตอนนี้เป็นของสมุด ไม่ใช่ของม้านั่ง
        if (Book.BookPageTurner.ClaimsInteract) return;

        // กันไว้อีกชั้นตอนกดจริง — ต่อให้ยังไม่ทันถอนการฟังปุ่ม ม้านั่งที่อยู่ไกลต้องไม่ดึงตัวผู้เล่นกลับมา
        if (!IsPlayerInRange(_player))
        {
            Unsubscribe();
            return;
        }

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
