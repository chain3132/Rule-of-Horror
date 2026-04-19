using System.Collections.Generic;
using Enum;
using ScriptableObject;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Manager
{
    public class ChatUIController : MonoBehaviour
    {
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject leftBubblePrefab;
        [SerializeField] private GameObject rightBubblePrefab;
        [SerializeField] private ScrollRect scrollRect;
        
        [SerializeField] private ConversationData testConversation;
        [SerializeField] private ConversationRunner runner;

        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
        [SerializeField] private Transform replyRoot;
        [SerializeField] private GameObject replyButtonPrefab;
        private ChatHistory currentHistory;
        private Dictionary<ConversationData, ChatHistory> histories;
        private List<(ConversationData data, int nodeIndex)> timeline
            = new List<(ConversationData, int)>();

        private HashSet<ConversationData> playedConversations
            = new HashSet<ConversationData>();
        [SerializeField] private ConversationManager convoManager;
        private ConversationData currentRunningData;

        private void OnEnable()
        {
            runner.OnNodeDisplayed += AddMessage;
            runner.OnReplyRequired += ShowReplies;
        }

        private void OnDisable()
        {
            runner.OnNodeDisplayed -= AddMessage;
            runner.OnReplyRequired -= ShowReplies;
        }

        public void OpenConversation()
        {
            phoneSystem.ChangeState(PhoneState.ChatView);

            ClearChatDisplay();

            // 🔥 render ทั้ง timeline
            foreach (var entry in timeline)
            {
                var node = entry.data.nodes[entry.nodeIndex];
                InstantiateBubble(node);
            }

            // 🔥 หา conversation ล่าสุดที่ยังไม่จบ
            var unlocked = convoManager.UnlockedData;

            foreach (var data in unlocked)
            {
                if (!playedConversations.Contains(data))
                {
                    currentRunningData = data;
                    playedConversations.Add(data);

                    runner.StartConversation(data);
                    return;
                }
            }

            Debug.Log("All conversations played");
        }
        private void OpenConversationInternal(ConversationData data)
        {
            if (!histories.ContainsKey(data))
                histories[data] = new ChatHistory();

            currentHistory = histories[data];

            ClearChatDisplay();

            if (currentHistory.shownNodeIndices.Count > 0)
            {
                foreach (var index in currentHistory.shownNodeIndices)
                {
                    InstantiateBubble(data.nodes[index]);
                }

                if (!currentHistory.isFinished)
                {
                    runner.ResumeConversation(data, currentHistory.lastNodeIndex);
                }
            }
            else
            {
                runner.StartConversation(data);
            }
        }
        
        private void InstantiateBubble(ChatNode node)
        {
            var prefab = node.isPlayer ? rightBubblePrefab : leftBubblePrefab;
            var bubble = Instantiate(prefab, contentRoot);
            bubble.GetComponentInChildren<TextMeshProUGUI>().text = node.message;
    
            // จัด Scroll ให้อยู่ล่างสุด
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
        public void AddMessage(ChatNode node)
        {
            int index = runner.CurrentIndex;
            var currentData = currentRunningData; // ต้องเก็บตัวนี้ตอน Start/Resume

            var entry = (currentData, index);

            if (!timeline.Contains(entry))
            {
                timeline.Add(entry);
            }

            InstantiateBubble(node);
        }
        private void RenderMessage(ChatNode node)
        {
            var prefab = node.isPlayer ? rightBubblePrefab : leftBubblePrefab;
            var bubble = Instantiate(prefab, contentRoot);
            bubble.GetComponentInChildren<TextMeshProUGUI>().text = node.message;
    
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
        private void ClearChatDisplay()
        {
            foreach (Transform child in contentRoot) Destroy(child.gameObject);
        }
        public void ShowReplies(List<ReplyOption> replies)
        {
            ClearReplies();

            for (int i = 0; i < replies.Count; i++)
            {
                int index = i;

                var btn = Instantiate(replyButtonPrefab, replyRoot);

                btn.GetComponentInChildren<TextMeshProUGUI>().text =
                    replies[i].replyText;

                btn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    OnReplyClicked(index, replies[index]);
                });
            }
        }
        private void OnReplyClicked(int index, ReplyOption reply)
        {
            AddPlayerMessage(reply.replyText);

            ClearReplies();

            runner.SelectReply(index);
        }
        private void AddPlayerMessage(string message)
        {
            var bubble = Instantiate(rightBubblePrefab, contentRoot);

            bubble.GetComponentInChildren<TextMeshProUGUI>().text = message;

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
        private void ClearReplies()
        {
            foreach (Transform child in replyRoot)
                Destroy(child.gameObject);
        }
    }
    
}
