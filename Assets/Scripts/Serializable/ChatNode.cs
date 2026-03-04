using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class ChatNode
{
    [TextArea(2,5)]
    public string message;

    public bool isPlayer;

    [ShowIf("isPlayer")]
    [TableList]
    public List<ReplyOption> replies;

    [HideIf("isPlayer")]
    public int nextNode;
}
