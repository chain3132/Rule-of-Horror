using System;
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
        // ─────────────────────────── Inner Types ───────────────────────────

        /// <summary>Entry ใน chat history — เป็นได้ทั้ง node ref (NPC/system) หรือ player reply string</summary>
        private class ChatHistoryEntry
        {
            public ConversationData data;   // null = player reply
            public int nodeIndex;
            public string playerReplyText;  // ใช้เมื่อ data == null

            public static ChatHistoryEntry FromNode(ConversationData d, int i)
                => new ChatHistoryEntry { data = d, nodeIndex = i };

            public static ChatHistoryEntry FromReply(string text)
                => new ChatHistoryEntry { playerReplyText = text };
        }

        // ─────────────────────────── Inspector ───────────────────────────

        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject leftBubblePrefab;
        [SerializeField] private GameObject rightBubblePrefab;
        [SerializeField] private ScrollRect scrollRect;

        [SerializeField] private ConversationRunner runner;
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
        [SerializeField] private Transform replyRoot;
        [SerializeField] private GameObject replyButtonPrefab;

        // ─────────────────────────── Events ───────────────────────────

        /// <summary>Fire เมื่อ conversation ของ contact จบ — FriendListController subscribe ไว้</summary>
        public event Action<FriendListController.ContactEntry> OnContactConversationEnd;

        // ─────────────────────────── Runtime ───────────────────────────

        private ConversationData currentRunningData;

        /// <summary>Contact ที่กำลัง open chat อยู่ (null = ใช้ระบบ legacy)</summary>
        private FriendListController.ContactEntry _currentContact;

        /// <summary>Global timeline รวมทุก conversation (backward compat)</summary>
        private List<(ConversationData data, int nodeIndex)> timeline
            = new List<(ConversationData, int)>();

        /// <summary>Timeline แยกต่างหากสำหรับแต่ละ contact — รองรับทั้ง node ref และ player reply</summary>
        private Dictionary<FriendListController.ContactEntry, List<ChatHistoryEntry>>
            _contactTimelines = new Dictionary<FriendListController.ContactEntry, List<ChatHistoryEntry>>();

        /// <summary>Conversations ที่เล่นผ่าน OpenContactChat ไปแล้ว (per-contact system)</summary>
        private HashSet<ConversationData> playedConversations = new HashSet<ConversationData>();


        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable()
        {
            runner.OnNodeDisplayed   += AddMessage;
            runner.OnReplyRequired   += ShowReplies;
            runner.OnConversationEnd += HandleConversationEnd;
        }

        private void OnDisable()
        {
            runner.OnNodeDisplayed   -= AddMessage;
            runner.OnReplyRequired   -= ShowReplies;
            runner.OnConversationEnd -= HandleConversationEnd;
        }

        // ─────────────────────────── Per-Contact Chat (ระบบใหม่) ───────────────────────────

        /// <summary>
        /// เปิด chat ของ contact ที่ระบุ
        /// แสดงเฉพาะ history ของ contact นี้ แล้วเล่น conversation ถัดไปของ contact
        /// </summary>
        public void OpenContactChat(FriendListController.ContactEntry contact)
        {
            _currentContact = contact;
            phoneSystem.ChangeState(PhoneState.ChatView);
            ClearChatDisplay();

            // render history ของ contact นี้เท่านั้น
            if (_contactTimelines.TryGetValue(contact, out var contactTimeline))
            {
                foreach (var entry in contactTimeline)
                {
                    if (entry.data != null)
                        InstantiateBubble(entry.data.nodes[entry.nodeIndex]);
                    else
                        InstantiatePlayerReplyBubble(entry.playerReplyText);
                }
            }

            // หา conversation ถัดไปที่ยังไม่ได้เล่น
            foreach (var data in contact.conversations)
            {
                if (playedConversations.Contains(data)) continue;

                currentRunningData = data;
                playedConversations.Add(data);
                runner.StartConversation(data);
                return;
            }

            // ทุก conversation ของ contact นี้จบแล้ว — ไม่ทำอะไร
        }


        // ─────────────────────────── Message Display ───────────────────────────

        public void AddMessage(ChatNode node)
        {
            int index       = runner.CurrentIndex;
            var currentData = currentRunningData;
            var tupleEntry  = (currentData, index);

            // global timeline
            if (!timeline.Contains(tupleEntry))
                timeline.Add(tupleEntry);

            // contact timeline
            if (_currentContact != null)
            {
                if (!_contactTimelines.ContainsKey(_currentContact))
                    _contactTimelines[_currentContact] = new List<ChatHistoryEntry>();

                var ct = _contactTimelines[_currentContact];
                // ป้องกัน duplicate โดยเช็ค data+index
                bool exists = ct.Exists(e => e.data == currentData && e.nodeIndex == index);
                if (!exists)
                    ct.Add(ChatHistoryEntry.FromNode(currentData, index));
            }

            InstantiateBubble(node);
        }

        private void InstantiateBubble(ChatNode node)
        {
            var prefab = node.isPlayer ? rightBubblePrefab : leftBubblePrefab;
            var bubble = Instantiate(prefab, contentRoot);
            bubble.GetComponentInChildren<TextMeshProUGUI>().text = node.message;

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void ClearChatDisplay()
        {
            foreach (Transform child in contentRoot)
                Destroy(child.gameObject);
        }

        // ─────────────────────────── Replies ───────────────────────────

        public void ShowReplies(List<ReplyOption> replies)
        {
            ClearReplies();
            for (int i = 0; i < replies.Count; i++)
            {
                int index = i;
                var btn   = Instantiate(replyButtonPrefab, replyRoot);
                btn.GetComponentInChildren<TextMeshProUGUI>().text = replies[i].replyText;
                btn.GetComponent<Button>().onClick.AddListener(() => OnReplyClicked(index, replies[index]));
            }
        }

        private void OnReplyClicked(int index, ReplyOption reply)
        {
            // เซฟ player reply ลง contact timeline ก่อน แล้วค่อย instantiate
            if (_currentContact != null)
            {
                if (!_contactTimelines.ContainsKey(_currentContact))
                    _contactTimelines[_currentContact] = new List<ChatHistoryEntry>();

                _contactTimelines[_currentContact].Add(ChatHistoryEntry.FromReply(reply.replyText));
            }

            InstantiatePlayerReplyBubble(reply.replyText);
            ClearReplies();
            runner.SelectReply(index);
        }

        private void InstantiatePlayerReplyBubble(string message)
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

        // ─────────────────────────── Conversation End ───────────────────────────

        private void HandleConversationEnd()
        {
            if (_currentContact != null)
                OnContactConversationEnd?.Invoke(_currentContact);
        }
    }
}
