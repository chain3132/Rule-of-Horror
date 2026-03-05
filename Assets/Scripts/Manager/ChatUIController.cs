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
            runner.StartConversation(testConversation);
        }
        public void AddMessage(ChatNode node)
        {
            var prefab = node.isPlayer ? rightBubblePrefab : leftBubblePrefab;
            var bubble = Instantiate(prefab, contentRoot);

            bubble.GetComponentInChildren<TextMeshProUGUI>().text = node.message;

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
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
