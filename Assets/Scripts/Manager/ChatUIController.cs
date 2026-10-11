using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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

        [Header("Chat Header")]
        [Tooltip("Text ชื่อ contact บนหัวแชท")]
        [SerializeField] private TMP_Text headerNameText;
        [Tooltip("Image รูปโปรไฟล์ contact บนหัวแชท")]
        [SerializeField] private Image headerAvatarImage;

        // ─────────────────────────── ฟอง "กำลังพิมพ์" ───────────────────────────
        // ข้อความของคนปริศนาไม่โผล่มาทันที มันจะขึ้นฟองนี้ค้างไว้ก่อน แล้วค่อยส่งมา
        // (เวลาที่รอเป็นของ ConversationRunner ตัวนี้แค่วาดตามที่มันบอกผ่าน OnTypingChanged)
        //
        // ฟองนี้ไม่ถูกบันทึกลง timeline ของ contact — มันไม่ใช่ข้อความ เปิดแชทใหม่จะไม่เห็นมันซ้ำ

        [Header("กำลังพิมพ์")]
        [Tooltip("ฟอง \"กำลังพิมพ์\" — เว้นว่าง = ใช้ฟองข้อความฝั่งซ้ายอันเดิม ไม่ต้องทำ prefab ใหม่")]
        [SerializeField] private GameObject typingBubblePrefab;

        [Tooltip("คำที่โชว์ในฟอง — เว้นว่าง = มีแต่จุด")]
        [SerializeField] private string typingLabel = "กำลังพิมพ์";

        [Tooltip("ตัวที่ใช้เป็นจุด — ใช้ . เพราะฟอนต์ไทยมีแน่ ส่วน ● บางฟอนต์ไม่มีแล้วจะได้สี่เหลี่ยม")]
        [SerializeField] private string typingDot = ".";

        [Tooltip("จุดกี่ตัว")]
        [SerializeField] private int typingDotCount = 3;

        [Tooltip("จุดกระพริบเปลี่ยนตัวทุกกี่วินาที")]
        [SerializeField] private float typingFrameInterval = 0.4f;

        [Header("Keyboard")]
        [Tooltip("ใส่ InputHandler — ใช้อ่านล้อเมาส์สำหรับเลื่อนแชท")]
        [SerializeField] private InputSystem.InputHandler inputHandler;

        [Tooltip("ความเร็วเลื่อนแชทด้วยล้อเมาส์")]
        [SerializeField] private float scrollSpeed = 0.35f;

        // ─────────────────────────── Events ───────────────────────────

        /// <summary>Fire เมื่อ conversation ของ contact จบ — FriendListController subscribe ไว้</summary>
        public event Action<FriendListController.ContactEntry, ConversationData> OnContactConversationEnd;

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

        /// <summary>Conversations ที่เล่น "จบ" แล้ว (มาร์คตอน HandleConversationEnd ไม่ใช่ตอนเริ่ม)</summary>
        private HashSet<ConversationData> playedConversations = new HashSet<ConversationData>();

        /// <summary>Conversation ที่เริ่มแล้วแต่ผู้เล่นสลับ contact ออกกลางคัน — เก็บ index ไว้เล่นต่อ</summary>
        private Dictionary<ConversationData, int> _resumeIndex = new Dictionary<ConversationData, int>();

        /// <summary>ตัวเลือกคำตอบที่ค้างอยู่บนจอตอนนี้ — กดเลข 1/2 แทนการคลิกได้</summary>
        private List<ReplyOption> _pendingReplies;

        private GameObject _typingBubble;
        private Coroutine  _typingAnim;


        // ─────────────────────────── Keyboard (จอโทรศัพท์คลิกไม่ได้) ───────────────────────────

        /// <summary>true = มีตัวเลือกคำตอบรออยู่ ปุ่มตัวเลขตอนนี้ควรเป็นปุ่มเลือกคำตอบ ไม่ใช่ปุ่มสลับแอป</summary>
        public bool AwaitingReply => _pendingReplies != null && _pendingReplies.Count > 0;

        /// <summary>
        /// เลือกคำตอบด้วยหมายเลข (0 = ตัวเลือกแรก) — คืน false ถ้าไม่มีตัวเลือกนั้น
        ///
        /// จอโทรศัพท์ถ่ายผ่านกล้องลง RenderTexture เมาส์จึงคลิกปุ่มในนั้นไม่ได้เลย
        /// ปุ่มยังอยู่และยังกดได้ถ้าวันหลังย้าย UI ออกมาจอจริง แต่ทางหลักคือคีย์บอร์ด
        /// </summary>
        public bool TrySelectReply(int index)
        {
            if (!AwaitingReply || index < 0 || index >= _pendingReplies.Count) return false;

            OnReplyClicked(index, _pendingReplies[index]);
            return true;
        }

        private void Update()
        {
            // เลื่อนแชทด้วยล้อเมาส์ — ล้อยังใช้ได้ถึงจะคลิกไม่ได้
            if (inputHandler == null || phoneSystem == null || scrollRect == null) return;
            if (phoneSystem.CurrentState != PhoneState.ChatView) return;

            float scroll = inputHandler.GetScrollDelta();
            if (Mathf.Approximately(scroll, 0f)) return;

            scrollRect.verticalNormalizedPosition =
                Mathf.Clamp01(scrollRect.verticalNormalizedPosition + scroll * scrollSpeed * Time.unscaledDeltaTime);
        }


        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable()
        {
            runner.OnNodeDisplayed   += AddMessage;
            runner.OnReplyRequired   += ShowReplies;
            runner.OnConversationEnd += HandleConversationEnd;
            runner.OnTypingChanged   += SetTyping;
        }

        private void OnDisable()
        {
            runner.OnNodeDisplayed   -= AddMessage;
            runner.OnReplyRequired   -= ShowReplies;
            runner.OnConversationEnd -= HandleConversationEnd;
            runner.OnTypingChanged   -= SetTyping;
        }

        // ─────────────────────────── Per-Contact Chat (ระบบใหม่) ───────────────────────────

        /// <summary>
        /// เปิด chat ของ contact ที่ระบุ
        /// แสดงเฉพาะ history ของ contact นี้ แล้วเล่น conversation ถัดไปของ contact
        /// </summary>
        public void OpenContactChat(FriendListController.ContactEntry contact)
        {
            // ── หยุด conversation ของ contact เดิมก่อน (กัน node/ตัวเลือกเด้งข้ามแชท) ──
            // เก็บ index ที่ค้างไว้ ถ้ายังเล่นไม่จบ จะได้เล่นต่อตอนกลับมาเปิดใหม่
            if (currentRunningData != null && !playedConversations.Contains(currentRunningData))
                _resumeIndex[currentRunningData] = runner.CurrentIndex;
            runner.StopConversation();
            currentRunningData = null;
            ClearReplies();

            _currentContact = contact;
            phoneSystem.ChangeState(PhoneState.ChatView);

            // หัวแชท: ชื่อ + รูปโปรไฟล์ของ contact นี้
            if (headerNameText != null)
                headerNameText.text = contact.contactName;
            if (headerAvatarImage != null && contact.avatar != null)
                headerAvatarImage.sprite = contact.avatar;

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

            // หาชุดถัดไปที่ส่งถึงแล้วแต่ยังอ่านไม่จบ — เล่นต่อถ้าค้างไว้ ไม่งั้นเริ่มใหม่
            //
            // กรองด้วย delivered ด้วย ไม่ใช่แค่ยังไม่จบ ไม่งั้นเปิดแชทตอนสองทุ่มจะได้อ่าน
            // ข้อความที่คนปริศนายังไม่ได้ส่งไปแล้ว
            foreach (var message in contact.DeliveredMessages)
            {
                var data = message.conversation;
                if (playedConversations.Contains(data)) continue;

                currentRunningData = data;
                if (_resumeIndex.TryGetValue(data, out int savedIdx))
                    runner.ResumeFrom(data, savedIdx);
                else
                    runner.StartConversation(data);
                return;
            }

            // ทุก conversation ของ contact นี้จบแล้ว — ไม่ทำอะไร
        }


        // ─────────────────────────── Message Display ───────────────────────────

        public void AddMessage(ChatNode node)
        {
            // ข้อความมาถึงแล้ว ฟองพิมพ์ต้องหายก่อนฟองข้อความจะโผล่ — เก็บตรงนี้ด้วย
            // ไม่ใช่รอแต่ OnTypingChanged(false) เผื่อมีทางที่ข้อความมาโดยไม่ผ่านการรอ
            HideTyping();

            int index       = runner.CurrentIndex;
            var currentData = currentRunningData;
            var tupleEntry  = (currentData, index);

            // global timeline
            if (!timeline.Contains(tupleEntry))
                timeline.Add(tupleEntry);

            // contact timeline
            bool alreadyShown = false;
            if (_currentContact != null)
            {
                if (!_contactTimelines.ContainsKey(_currentContact))
                    _contactTimelines[_currentContact] = new List<ChatHistoryEntry>();

                var ct = _contactTimelines[_currentContact];
                // ป้องกัน duplicate โดยเช็ค data+index
                alreadyShown = ct.Exists(e => e.data == currentData && e.nodeIndex == index);
                if (!alreadyShown)
                    ct.Add(ChatHistoryEntry.FromNode(currentData, index));
            }

            // node ที่เคยแสดงแล้ว (ตอน resume) — history render วาดไปแล้ว ไม่ต้องวาดซ้ำ
            if (!alreadyShown)
                InstantiateBubble(node);
        }

        private void InstantiateBubble(ChatNode node)
        {
            var prefab = node.isPlayer ? rightBubblePrefab : leftBubblePrefab;
            var bubble = Instantiate(prefab, contentRoot);

            // FormatText แทน {reward} ด้วยชื่อรางวัลที่ผู้เล่นเพิ่งเลือก ข้อความที่ไม่มี
            // placeholder จะถูกคืนกลับมาทั้งดุ้นโดยไม่ไปแตะ RewardSystem เลย
            bubble.GetComponentInChildren<TextMeshProUGUI>().text = RewardSystem.FormatText(node.message);

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void ClearChatDisplay()
        {
            // ฟองพิมพ์เป็นลูกของ contentRoot เหมือนกัน ลบพร้อมกันไปแล้ว
            // ต้องเคลียร์ตัวอ้างอิงด้วย ไม่งั้นรอบหน้าจะเห็นว่า "มีอยู่แล้ว" แล้วไม่สร้างใหม่
            HideTyping();

            foreach (Transform child in contentRoot)
                Destroy(child.gameObject);
        }

        // ─────────────────────────── กำลังพิมพ์ ───────────────────────────

        private void SetTyping(bool typing)
        {
            if (typing) ShowTyping();
            else        HideTyping();
        }

        private void ShowTyping()
        {
            if (_typingBubble != null || contentRoot == null) return;

            var prefab = typingBubblePrefab != null ? typingBubblePrefab : leftBubblePrefab;
            if (prefab == null) return;

            _typingBubble = Instantiate(prefab, contentRoot);

            var label = _typingBubble.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) _typingAnim = StartCoroutine(AnimateTyping(label));

            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        }

        private void HideTyping()
        {
            if (_typingAnim != null)
            {
                StopCoroutine(_typingAnim);
                _typingAnim = null;
            }

            if (_typingBubble != null)
            {
                Destroy(_typingBubble);
                _typingBubble = null;
            }
        }

        private IEnumerator AnimateTyping(TMP_Text label)
        {
            var frames = BuildTypingFrames(label.richText);
            int frame  = 0;

            while (true)
            {
                label.text = frames[frame % frames.Length];
                frame++;

                // Realtime เพราะ GameModeController หรี่ timeScale ตอนกะพริบตา
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, typingFrameInterval));
            }
        }

        /// <summary>
        /// สร้างข้อความของแต่ละเฟรมไว้ล่วงหน้า
        ///
        /// มี rich text = จุดอยู่ครบทุกตัวทุกเฟรม สว่างทีละตัว → กระพริบอยู่กับที่ ฟองไม่ขยับ
        /// ไม่มี rich text = จุดเพิ่มทีละตัว (ฟองจะกว้างขึ้นหน่อยถ้ามี ContentSizeFitter)
        /// </summary>
        private string[] BuildTypingFrames(bool richText)
        {
            int count   = Mathf.Max(1, typingDotCount);
            var frames  = new string[count];
            var builder = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                builder.Clear();
                builder.Append(typingLabel);

                for (int dot = 0; dot < count; dot++)
                {
                    if (!richText)
                    {
                        if (dot <= i) builder.Append(typingDot);
                        continue;
                    }

                    builder.Append(dot == i ? "<alpha=#ff>" : "<alpha=#40>").Append(typingDot);
                }

                if (richText) builder.Append("<alpha=#ff>");
                frames[i] = builder.ToString();
            }

            return frames;
        }

        // ─────────────────────────── Replies ───────────────────────────

        public void ShowReplies(List<ReplyOption> replies)
        {
            ClearReplies();
            if (replies == null) return;   // เมธอดเป็น public ใครเรียกก็ได้ — อย่าให้ล้มที่ .Count

            _pendingReplies = replies;
            for (int i = 0; i < replies.Count; i++)
            {
                int index = i;
                var btn   = Instantiate(replyButtonPrefab, replyRoot);

                // ปุ่มเลขที่ต้องกดคือ index + 1 — InputHandler ยิง 0 ตอนกดเลข 1
                var view = btn.GetComponent<ReplyButtonView>();
                if (view != null) view.Set(replies[i].replyText, index + 1);
                else              btn.GetComponentInChildren<TextMeshProUGUI>().text = replies[i].replyText;

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
            _pendingReplies = null;
            foreach (Transform child in replyRoot)
                Destroy(child.gameObject);
        }

        // ─────────────────────────── Conversation End ───────────────────────────

        private void HandleConversationEnd()
        {
            var finished = currentRunningData;

            // มาร์คว่าเล่นจบตรงนี้ (ไม่ใช่ตอนเริ่ม) — abandon กลางคันจะได้เล่นต่อได้ ไม่หาย
            if (finished != null)
            {
                playedConversations.Add(finished);
                _resumeIndex.Remove(finished);
            }

            // ส่ง conversation ที่จบไปด้วย เพราะ contact หนึ่งคนมีหลายชุด
            // ฝั่งรับต้องรู้ว่าชุดไหนจบ ถึงจะตัดสินได้ว่าต้องเข้า Rule 1 ต่อหรือเปล่า
            if (_currentContact != null)
                OnContactConversationEnd?.Invoke(_currentContact, finished);
        }
    }
}
