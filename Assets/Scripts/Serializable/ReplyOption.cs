
using UnityEngine;

[System.Serializable]
public class ReplyOption
{
    [TextArea(1,3)]
    public string replyText;

    public int nextNode;
}
