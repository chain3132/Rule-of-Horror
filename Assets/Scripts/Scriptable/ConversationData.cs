using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptableObject
{
    [CreateAssetMenu(menuName = "Phone/Conversation")]
    public class ConversationData : SerializedScriptableObject
    {
        [TableList]
        public List<ChatNode> nodes;
    }
}
