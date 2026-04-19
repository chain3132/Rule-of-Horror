using ScriptableObject;
using UnityEngine;

[System.Serializable]
public class TimedConversation
{
    public ConversationData data;

    public int startHour;
    public int startMinute;

    public int endHour;
    public int endMinute;

    [HideInInspector] public bool isUnlocked;
}
