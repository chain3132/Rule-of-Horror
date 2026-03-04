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
        private void OnEnable()
        {
            runner.OnNodeDisplayed += AddMessage;
        }

        private void OnDisable()
        {
            runner.OnNodeDisplayed -= AddMessage;
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
    }
}
