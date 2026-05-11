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
        // ─────────────────────────── Inspector ───────────────────────────

        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject leftBubblePrefab;
        [SerializeField] private GameObject rightBubblePrefab;
        [SerializeField] private ScrollRect scrollRect;

        [SerializeField] private ConversationRunner runner;
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
        [SerializeField] private Transform replyRoot;
        [SerializeField] private GameObject replyButtonPrefab;
        [SerializeField] private ConversationManager convoManager;

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

        /// <summary>Timeline แยกต่างหากสำหรับแต่ละ contact</summary>
        private Dictionary<FriendListController.ContactEntry, List<(ConversationData data, int nodeIndex)>>
            _contactTimelines = new Dictionary<FriendListController.ContactEntry, List<(ConversationData, int)>>();

        /// <summary>Conversations ที่เล่นไปแล้ว</summary>
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
                    InstantiateBubble(entry.data.nodes[entry.nodeIndex]);
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

        // ─────────────────────────── Legacy Open (ระบบเดิม) ───────────────────────────

        /// <summary>เปิด chat แบบเดิมผ่าน ConversationManager — ใช้ได้ถ้าไม่ใช้ per-contact</summary>
        public void OpenConversation()
        {
            _currentContact = null;
            phoneSystem.ChangeState(PhoneState.ChatView);
            ClearChatDisplay();

            foreach (var entry in timeline)
                InstantiateBubble(entry.data.nodes[entry.nodeIndex]);

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
        }

        // ─────────────────────────── Message Display ───────────────────────────

        public void AddMessage(ChatNode node)
        {
            int index       = runner.CurrentIndex;
            var currentData = currentRunningData;
            var entry       = (currentData, index);

            // global timeline
            if (!timeline.Contains(entry))
                timeline.Add(entry);

            // contact timeline
            if (_currentContact != null)
            {
                if (!_contactTimelines.ContainsKey(_currentContact))
                    _contactTimelines[_currentContact] = new List<(ConversationData, int)>();

                var ct = _contactTimelines[_currentContact];
                if (!ct.Contains(entry))
                    ct.Add(entry);
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

        // ─────────────────────────── Conversation End ───────────────────────────

        private void HandleConversationEnd()
        {
            if (_currentContact != null)
                OnContactConversationEnd?.Invoke(_currentContact);
        }
    }
}
