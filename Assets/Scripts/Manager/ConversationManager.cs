using System.Collections.Generic;
using Manager;
using ScriptableObject;
using UnityEngine;

public class ConversationManager : MonoBehaviour
{
    public TimedConversation[] conversations;
    private List<ConversationData> unlockedData = new List<ConversationData>();

    public IReadOnlyList<ConversationData> UnlockedData => unlockedData;

    private void OnEnable()
    {
        TimeManager.OnTimeChanged += CheckConversations;
    }

    private void OnDisable()
    {
        TimeManager.OnTimeChanged -= CheckConversations;
    }

    private void CheckConversations(int hour, int minute)
    {
        int currentTime = hour * 60 + minute;

        foreach (var convo in conversations)
        {
            int start = convo.startHour * 60 + convo.startMinute;

            if (!convo.isUnlocked && currentTime >= start)
            {
                convo.isUnlocked = true;
                unlockedData.Add(convo.data);
            }
        }
    }
}
