using System;
using System.Collections;
using System.Collections.Generic;
using ScriptableObject;
using UnityEngine;

public class ConversationRunner : MonoBehaviour
{
    public event Action<ChatNode> OnNodeDisplayed;
    public event Action<List<ReplyOption>> OnReplyRequired;
    public event Action OnConversationEnd;

    /// <summary>
    /// คนปริศนาเริ่ม/เลิก "กำลังพิมพ์" — ChatUIController เอาไปโชว์ฟองจุดกระพริบ
    ///
    /// ยิง true ตอนเริ่มรอ ยิง false ก่อน OnNodeDisplayed ของข้อความนั้นเสมอ
    /// (ฝั่ง UI ซ่อนฟองพิมพ์ตอนมีข้อความใหม่เข้ามาด้วย เผื่อมีทางที่ false หลุดหาย)
    /// </summary>
    public event Action<bool> OnTypingChanged;

    /// <summary>ผู้เล่นเพิ่งเลือกคำตอบ — (node ที่ถาม, ลำดับที่เลือก, ตัวเลือกที่เลือก)</summary>
    public event Action<ChatNode, int, ReplyOption> OnReplySelected;

    /// <summary>
    /// ให้ระบบอื่นสร้างตัวเลือกของ node นี้แทนที่จะใช้ที่เขียนไว้ในแอสเซต (คืน null = ใช้ของเดิม)
    ///
    /// มีไว้ให้ RewardSystem สุ่มรางวัลสองอย่างมาเป็นตัวเลือกตอนเล่น ตัวเดินบทสนทนาจึงไม่ต้อง
    /// รู้จักระบบรางวัลเลย แค่ถามว่า "ใครจะแทนตัวเลือกของ node นี้ไหม" แล้วใช้ที่ได้มา
    /// </summary>
    public Func<ChatNode, List<ReplyOption>> ReplyOverride;

    // ─────────────────────────── จังหวะการพิมพ์ ───────────────────────────

    [Header("จังหวะการพิมพ์")]
    [Tooltip("✓ = ข้อความของคนปริศนาขึ้น \"กำลังพิมพ์...\" ค้างไว้ก่อนตามเวลาที่สุ่มได้\n" +
             "✗ = โผล่มาทันทีเหมือนเดิม")]
    [SerializeField] private bool showTyping = true;

    [Tooltip("พิมพ์อยู่นานสุด/น้อยสุดกี่วินาทีก่อนข้อความจะมาถึง")]
    [SerializeField] private float typingMinSeconds = 2f;
    [SerializeField] private float typingMaxSeconds = 5f;

    [Tooltip("เว้นกี่วินาทีก่อนไป node ถัดไป — ของเดิมคือ 1\n" +
             "ตอนนี้มีฟอง \"กำลังพิมพ์\" คั่นให้อยู่แล้ว ลดลงหรือใส่ 0 ได้ถ้ารู้สึกอืด")]
    [SerializeField] private float delayBetweenNodes = 1f;

    public int CurrentIndex => currentIndex;

    private ConversationData currentConversation;
    private int currentIndex;

    /// <summary>รอบที่กำลังรอ "พิมพ์" อยู่ — ต้องหยุดเองตอนสลับ contact ไม่งั้นข้อความข้ามแชท</summary>
    private Coroutine _typingRoutine;

    /// <summary>
    /// ตัวเลือกที่ค้างอยู่บนจอจริงๆ ตอนนี้ — อาจไม่ใช่ node.replies ถ้าถูกสร้างตอนเล่น
    ///
    /// ต้องเก็บไว้ ไม่งั้น SelectReply จะไปอ่าน node.replies (ซึ่งว่างเปล่าสำหรับ node
    /// ที่ให้เลือกรางวัล) แล้วกดเลือกอะไรก็ไม่มีอะไรเกิดขึ้น
    /// </summary>
    private List<ReplyOption> _activeReplies;

    public void StartConversation(ConversationData data)
    {
        CancelInvoke(nameof(DisplayNode));   // กัน auto-advance ค้างจาก conversation ก่อนหน้า
        CancelTyping();
        currentConversation = data;
        currentIndex = 0;
        DisplayNode();
    }

    /// <summary>เล่น conversation ต่อจาก index ที่ค้างไว้ (แสดง node ที่ index นั้นด้วย)</summary>
    public void ResumeFrom(ConversationData data, int index)
    {
        CancelInvoke(nameof(DisplayNode));
        CancelTyping();
        currentConversation = data;
        currentIndex = index;
        DisplayNode();
    }

    /// <summary>หยุด conversation ที่กำลังเล่น + ยกเลิก auto-advance ที่ค้างอยู่ (ใช้ตอนสลับ contact)</summary>
    public void StopConversation()
    {
        CancelInvoke(nameof(DisplayNode));
        CancelTyping();
        currentConversation = null;
        currentIndex = 0;
        _activeReplies = null;   // สลับ contact ทิ้งตัวเลือกที่ค้างไว้ด้วย ไม่งั้นมันข้ามแชทไปได้
    }

    private void DisplayNode()
    {
        if (currentConversation == null ||
            currentIndex < 0 ||
            currentIndex >= currentConversation.nodes.Count)
        {
            OnConversationEnd?.Invoke();
            return;
        }

        var node = currentConversation.nodes[currentIndex];

        // ข้อความของคนปริศนาไม่โผล่มาทันทีแล้ว — ขึ้น "กำลังพิมพ์" ค้างไว้ก่อนตามเวลาที่สุ่มได้
        //
        // หน่วงที่ตัวเดินบทสนทนา ไม่ใช่ที่ฝั่ง UI เพราะ OnNodeDisplayed ลากของอื่นไปด้วย
        // (บันทึกกฎลงแอปกฎ / เปิดตัวเลือกรางวัล) ถ้าไปหน่วงแค่ฟองข้อความ กฎจะถูกบันทึกลงแอป
        // และปุ่มตัวเลือกจะเด้งขึ้น ก่อนที่ข้อความจะมาถึงจอ
        //
        // บทพูดของผู้เล่นไม่ต้องรอ — ผู้เล่นเป็นคนพิมพ์เอง ไม่ใช่คนที่เรากำลังรอ
        if (showTyping && !node.isPlayer && _typingRoutine == null)
        {
            _typingRoutine = StartCoroutine(TypeThenShow(node));
            return;
        }

        ShowNode(node);
    }

    /// <summary>รอให้ "พิมพ์" เสร็จก่อนค่อยส่งข้อความ</summary>
    private IEnumerator TypeThenShow(ChatNode node)
    {
        OnTypingChanged?.Invoke(true);

        float min  = Mathf.Max(0f, typingMinSeconds);
        float wait = UnityEngine.Random.Range(min, Mathf.Max(min, typingMaxSeconds));

        // Realtime เพราะ GameModeController หรี่ timeScale ลงตอนกะพริบตา
        // แชทในมือถือไม่ควรพิมพ์ช้าลงตามจังหวะนั้น
        yield return new WaitForSecondsRealtime(wait);

        _typingRoutine = null;
        OnTypingChanged?.Invoke(false);

        ShowNode(node);
    }

    private void CancelTyping()
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        OnTypingChanged?.Invoke(false);
    }

    private void ShowNode(ChatNode node)
    {
        // ต้องยิงก่อนขยับ currentIndex — ChatUIController.AddMessage อ่าน CurrentIndex
        // ไปเป็นคีย์ของ history ถ้าขยับก่อน ข้อความนี้จะถูกบันทึกไว้ใต้ index ของ node ถัดไป
        OnNodeDisplayed?.Invoke(node);

        AdvanceOrWaitForReply(node);
    }

    /// <summary>
    /// node นี้ต้องหยุดรอผู้เล่นเลือกตอบ หรือไปต่อเองได้
    ///
    /// แยกออกมาเพราะมีสองทางเข้า (DisplayNode / ResumeConversation) ถ้าให้แต่ละที่ตัดสินใจเอง
    /// กฎ scriptedLine จะหลุดที่ใดที่หนึ่งแน่ตอนแก้ครั้งถัดไป
    /// </summary>
    private void AdvanceOrWaitForReply(ChatNode node)
    {
        _activeReplies = null;

        // ถามก่อนว่ามีใครจะสร้างตัวเลือกให้ตอนเล่นไหม (node ให้เลือกรางวัล) ไม่มีก็ใช้ของในแอสเซต
        List<ReplyOption> replies = ReplyOverride != null ? ReplyOverride(node) : null;
        if (replies == null) replies = node.replies;

        if (node.NeedsReply && replies != null && replies.Count > 0)
        {
            _activeReplies = replies;
            OnReplyRequired?.Invoke(replies);
            return;
        }

        // node ให้เลือกรางวัลแต่สุ่มไม่ออก = รางวัลหมดแล้ว (ได้ครบทุกอย่าง/เทียนจุดหมดแล้ว)
        // ไม่ใช่ความผิดพลาดของคนเขียนบท ข้ามไปเล่าต่อเฉยๆ
        if (node.NeedsReply && node.offersReward)
        {
            Debug.Log($"[Conversation] node {currentIndex} ให้เลือกรางวัลแต่ไม่เหลืออะไรให้สุ่มแล้ว — " +
                      $"ข้ามไป node {node.nextNode}", this);
        }
        // ติ๊ก Is Player ไว้แต่ไม่ได้ใส่ Replies — เดิมตรงนี้ยิง OnReplyRequired ด้วยลิสต์ว่าง
        // ShowReplies จึงไม่สร้างปุ่มอะไรเลย และไม่มีใครนัด DisplayNode ต่อ = แชทค้างตายสนิท
        // โดยไม่มี log อะไรบอก (ถ้า replies เป็น null ยิ่งหนัก ShowReplies อ่าน .Count แล้ว throw)
        // ตอนนี้ข้ามไปให้ก่อนแล้วฟ้องดังๆ ว่าถ้าตั้งใจให้เป็นบทพูดตายตัวต้องติ๊ก Scripted Line
        else if (node.NeedsReply)
        {
            Debug.LogError($"[Conversation] node {currentIndex} ติด Is Player ไว้แต่ไม่มี Replies — " +
                           $"ถ้าอยากให้เป็นบทพูดตายตัวให้ติ๊ก Scripted Line ด้วย " +
                           $"ตอนนี้ข้ามไป node {node.nextNode} ให้ก่อนกันแชทค้าง", this);
        }

        currentIndex = node.nextNode;
        Invoke(nameof(DisplayNode), Mathf.Max(0f, delayBetweenNodes));
    }

    public void ResumeConversation(ConversationData data, int savedIndex)
    {
        currentConversation = data;
        currentIndex = savedIndex;
    
        // ตรวจสอบว่า Node ปัจจุบันต้องการการตอบโต้จากผู้เล่นหรือไม่
        // (ไม่ใช่ข้อความของผู้เล่นที่ต้องเลือกตอบ = รอ 1 วิแล้วไปต่อ)
        AdvanceOrWaitForReply(currentConversation.nodes[currentIndex]);
    }

    public void SelectReply(int replyIndex)
    {
        var node = currentConversation.nodes[currentIndex];

        // ใช้ตัวเลือกที่ค้างอยู่บนจอจริงๆ ไม่ใช่ node.replies — node ที่ให้เลือกรางวัล
        // ไม่มี replies ในแอสเซตเลย ตัวเลือกถูกสร้างตอนแสดง node
        var replies = _activeReplies ?? node.replies;
        if (replies == null || replyIndex < 0 || replyIndex >= replies.Count)
            return;

        ReplyOption reply = replies[replyIndex];
        _activeReplies = null;

        // ยิงก่อนขยับ currentIndex — คนฟัง (RewardSystem) ต้องรู้ว่าเลือกจาก node ไหน
        OnReplySelected?.Invoke(node, replyIndex, reply);

        currentIndex = reply.nextNode;
        DisplayNode();
    }

    public void Continue()
    {
        var node = currentConversation.nodes[currentIndex];
        currentIndex = node.nextNode;
        DisplayNode();
    }
}
