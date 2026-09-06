using System;
using System.Collections.Generic;
using ScriptableObject;
using UnityEngine;

public class ConversationRunner : MonoBehaviour
{
    public event Action<ChatNode> OnNodeDisplayed;
    public event Action<List<ReplyOption>> OnReplyRequired;
    public event Action OnConversationEnd;
    public int CurrentIndex => currentIndex;

    private ConversationData currentConversation;
    private int currentIndex;

    public void StartConversation(ConversationData data)
    {
        CancelInvoke(nameof(DisplayNode));   // กัน auto-advance ค้างจาก conversation ก่อนหน้า
        currentConversation = data;
        currentIndex = 0;
        DisplayNode();
    }

    /// <summary>เล่น conversation ต่อจาก index ที่ค้างไว้ (แสดง node ที่ index นั้นด้วย)</summary>
    public void ResumeFrom(ConversationData data, int index)
    {
        CancelInvoke(nameof(DisplayNode));
        currentConversation = data;
        currentIndex = index;
        DisplayNode();
    }

    /// <summary>หยุด conversation ที่กำลังเล่น + ยกเลิก auto-advance ที่ค้างอยู่ (ใช้ตอนสลับ contact)</summary>
    public void StopConversation()
    {
        CancelInvoke(nameof(DisplayNode));
        currentConversation = null;
        currentIndex = 0;
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

        OnNodeDisplayed?.Invoke(node);

        if (node.isPlayer)
        {
            OnReplyRequired?.Invoke(node.replies);
        }
        else
        {
            currentIndex = node.nextNode;
            Invoke(nameof(DisplayNode), 1f); // delay 1 วิ
        }
    }
    public void ResumeConversation(ConversationData data, int savedIndex)
    {
        currentConversation = data;
        currentIndex = savedIndex;
    
        // ตรวจสอบว่า Node ปัจจุบันต้องการการตอบโต้จากผู้เล่นหรือไม่
        var node = currentConversation.nodes[currentIndex];
        if (node.isPlayer)
        {
            OnReplyRequired?.Invoke(node.replies);
        }
        else
        {
            // ถ้าเป็นข้อความ NPC ให้รอ 1 วิแล้วไปต่อ
            currentIndex = node.nextNode;
            Invoke(nameof(DisplayNode), 1f);
        }
    }

    public void SelectReply(int replyIndex)
    {
        var node = currentConversation.nodes[currentIndex];

        if (replyIndex < 0 || replyIndex >= node.replies.Count)
            return;

        currentIndex = node.replies[replyIndex].nextNode;
        DisplayNode();
    }

    public void Continue()
    {
        var node = currentConversation.nodes[currentIndex];
        currentIndex = node.nextNode;
        DisplayNode();
    }
}
