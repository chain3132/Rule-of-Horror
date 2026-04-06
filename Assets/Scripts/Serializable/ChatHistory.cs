using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChatHistory
{
    public string conversationID; // ID ของบทสนทนา
    public int lastNodeIndex;     // คุยค้างไว้ที่ Node ไหน
    public List<ChatNode> historyMessages = new List<ChatNode>(); // ข้อความที่คุยไปแล้ว
    public bool isFinished;       // คุยจบหรือยัง
}
