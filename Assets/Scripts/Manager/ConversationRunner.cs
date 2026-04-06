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
        currentConversation = data;
        currentIndex = 0;
        DisplayNode();
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
