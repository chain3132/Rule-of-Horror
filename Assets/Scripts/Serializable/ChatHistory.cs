using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChatHistory
{
    public List<int> shownNodeIndices = new(); // เก็บ index แทน node (ปลอดภัยกว่า)
    public int lastNodeIndex = 0;
    public bool isFinished = false;       // คุยจบหรือยัง
}
