using System.Text;
using ScriptableObject;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the chats that lead into Rule 6 on the phone's schedule, in the open scene:
///
///   23:40  Unknow - Rule6   the last rule (no reward this time), how to pick the right bus
///   23:40  Ton    - Ton_Rule6 (already scheduled in the scene - only checked here)
///
/// Both land in the same minute; contacts are checked in list order, so Unknow (listed before
/// Ton) arrives first - the order the story wants: the rule, then Ton warning about it.
///
/// Safe to run again: a conversation already on a contact's schedule is left alone.
/// </summary>
public static class Rule6ChatScheduler
{
    private const string UnknownContact = "Unknow";
    private const string TonContact     = "Ton";
    private const string UnknownChat    = "Assets/Scripts/Scriptable/ScriptableObject/Rule/Rule6.asset";
    private const string TonChat        = "Assets/Scripts/Scriptable/ScriptableObject/Chat/Ton/Ton_Rule6.asset";
    private const int    Hour           = 23;
    private const int    Minute         = 40;

    [MenuItem("Rule of Horror/Rule 6/Schedule Rule 6 Chats (Unknown + Ton)", false, 30)]
    private static void Schedule()
    {
        var friends = Object.FindAnyObjectByType<FriendListController>(FindObjectsInactive.Include);
        if (friends == null)
        {
            EditorUtility.DisplayDialog("Rule 6 Chats", "No FriendListController in the open scene.", "OK");
            return;
        }

        var report = new StringBuilder("[Rule 6] Chats before Rule 6");
        report.AppendLine();

        var so       = new SerializedObject(friends);
        var contacts = so.FindProperty("contacts");

        Ensure(contacts, UnknownContact, AssetDatabase.LoadAssetAtPath<ConversationData>(UnknownChat), report);
        Ensure(contacts, TonContact,     AssetDatabase.LoadAssetAtPath<ConversationData>(TonChat),     report);

        if (so.ApplyModifiedProperties())
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        report.AppendLine("Then run Rule of Horror ▸ Localization ▸ 1. Setup Tables and Translations for the Thai text.");
        Debug.Log(report.ToString(), friends);
    }

    private static void Ensure(SerializedProperty contacts, string contactName, ConversationData chat, StringBuilder report)
    {
        if (chat == null)
        {
            report.AppendLine($"  ! Conversation for '{contactName}' not found - nothing scheduled");
            return;
        }

        SerializedProperty messages = null;
        for (int i = 0; i < contacts.arraySize; i++)
        {
            var c = contacts.GetArrayElementAtIndex(i);
            if (c.FindPropertyRelative("contactName").stringValue == contactName)
            {
                messages = c.FindPropertyRelative("messages");
                break;
            }
        }

        if (messages == null)
        {
            report.AppendLine($"  ! No contact named '{contactName}' in FriendListController");
            return;
        }

        for (int i = 0; i < messages.arraySize; i++)
        {
            var m = messages.GetArrayElementAtIndex(i);
            if (m.FindPropertyRelative("conversation").objectReferenceValue == chat)
            {
                report.AppendLine($"  - {contactName}: '{chat.name}' already scheduled at " +
                                  $"{m.FindPropertyRelative("notifyHour").intValue:00}:" +
                                  $"{m.FindPropertyRelative("notifyMinute").intValue:00}");
                return;
            }
        }

        int index = messages.arraySize;
        messages.InsertArrayElementAtIndex(index);
        var added = messages.GetArrayElementAtIndex(index);
        added.FindPropertyRelative("conversation").objectReferenceValue = chat;
        added.FindPropertyRelative("notifyHour").intValue          = Hour;
        added.FindPropertyRelative("notifyMinute").intValue        = Minute;
        added.FindPropertyRelative("triggersRule1OnEnd").boolValue = false;
        added.FindPropertyRelative("delivered").boolValue          = false;
        added.FindPropertyRelative("read").boolValue               = false;

        report.AppendLine($"  + {contactName}: '{chat.name}' at {Hour:00}:{Minute:00}");
    }
}
