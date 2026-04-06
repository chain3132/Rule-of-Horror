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
        private ChatHistory currentHistory = new ChatHistory();
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

            // 2. ถ้ามีประวัติเก่า ให้ "วาด" ของเก่าขึ้นมาก่อน (แบบเงียบๆ ไม่ผ่าน Runner)
            if (currentHistory.historyMessages.Count > 0)
            {
                foreach (var node in currentHistory.historyMessages)
                {
                    InstantiateBubble(node);
                }
        
                // 3. พอวาดเสร็จ ค่อยให้ Runner เริ่มทำงานต่อจากจุดล่าสุด
                if (!currentHistory.isFinished)
                {
                    // ตรงนี้ต้องระวัง: ResumeConversation ต้องไม่ไปสั่ง AddMessage ซ้ำของเดิม
                    runner.ResumeConversation(testConversation, currentHistory.lastNodeIndex);
                }
            }
            else 
            {
                // ถ้าไม่มีประวัติเลย ถึงค่อยเริ่มใหม่ตั้งแต่ต้น
                runner.StartConversation(testConversation);
            }
        }
        private void ReloadHistory()
        {
            foreach (var node in currentHistory.historyMessages)
            {
                RenderMessage(node); // แยก Logic การ Instantiate ออกมาเป็น Method กลาง
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
            // บันทึกลงประวัติ (ป้องกันการซ้อนต้องเช็คก่อนว่ามีในประวัติหรือยัง)
            if (!currentHistory.historyMessages.Contains(node))
            {
                currentHistory.historyMessages.Add(node);
            }
    
            currentHistory.lastNodeIndex = runner.CurrentIndex;
    
            InstantiateBubble(node); // แสดงผลบนจอ
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
