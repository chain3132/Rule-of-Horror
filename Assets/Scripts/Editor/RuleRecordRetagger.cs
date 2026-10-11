using System.Text;
using ScriptableObject;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time repair pass over the rule conversations.
///
/// What was wrong: recordsRule was set on nearly every node of a conversation instead of one per
/// branch. RuleBookController.Record() ignores repeats, so the first tagged node won — which was
/// the opening line, before the mysterious person had said anything. Two consequences:
///   • the rule appeared in the phone app before it was told
///   • the defiant branch still recorded cooperative, because the shared opening got there first,
///     so the "answer rudely and he tells you less" design never actually fired
///
/// The fix is one tag per branch, placed on the node where that branch finishes telling the rule.
/// In every one of these five conversations reply 1 is the hostile answer and reply 2 is the
/// cooperative one, and the branch lengths confirm it: the cooperative side carries the schedule
/// and the full detail, the defiant side is a two-line brush-off.
///
/// Safe to re-run, but the node numbers below are specific to the scripts as they are now — after
/// the dialogue is rewritten, retag by hand in the graph window instead of running this again.
/// This file can be deleted once the fix is committed.
/// </summary>
public static class RuleRecordRetagger
{
    private const string Rules = "Assets/Scriptable/Rules/";
    private const string Chat  = "Assets/Scripts/Scriptable/ScriptableObject/";

    private readonly struct Plan
    {
        public readonly string Conversation;
        public readonly string Rule;
        public readonly int    Defiant;        // the node that ends the "answer rudely" branch
        public readonly int    Cooperative;    // the node that ends the "play along" branch
        public readonly string Note;

        public Plan(string conversation, string rule, int defiant, int cooperative, string note)
        {
            Conversation = conversation;
            Rule         = rule;
            Defiant      = defiant;
            Cooperative  = cooperative;
            Note         = note;
        }
    }

    private static readonly Plan[] Plans =
    {
        new Plan(Chat + "OpeningRule1.asset", Rules + "RuleEntry_1.asset", 6, 7,
                 "#6 'Good luck' vs #7 which gives 19:00-19:40"),

        new Plan(Chat + "Rule/Rule2.asset", Rules + "RuleEntry_2.asset", 8, 12,
                 "#8 'You can't escape this' vs #12 which gives the time"),

        new Plan(Chat + "Rule/Rule3.asset", Rules + "RuleEntry_3.asset", 4, 9,
                 "#4 'it's your own choice' vs #9 which gives 21:00-21:40"),

        new Plan(Chat + "Rule/Rule4.asset", Rules + "RuleEntry_4.asset", 15, 12,
                 "#15 ends the short branch, #12 ends the detailed one"),

        new Plan(Chat + "Rule/Rule5.asset", Rules + "RuleEntry_5.asset", 13, 10,
                 "#13 'don't expect anyone to wait' vs #10 which gives 23:00-23:40"),
    };

    [MenuItem("Rule of Horror/Chat/Retag Rule Records (one per branch)", false, 97)]
    private static void Retag()
    {
        var report = new StringBuilder("[Retag] recordsRule — one tag per branch\n");
        int touched = 0;

        foreach (var plan in Plans)
        {
            var data = AssetDatabase.LoadAssetAtPath<ConversationData>(plan.Conversation);
            var rule = AssetDatabase.LoadAssetAtPath<Object>(plan.Rule);

            if (data == null) { report.AppendLine($"  ✗ missing {plan.Conversation}"); continue; }
            if (rule == null) { report.AppendLine($"  ✗ missing {plan.Rule}"); continue; }

            var so    = new SerializedObject(data);
            var nodes = ConversationEditing.Nodes(so);

            int cleared = 0;
            for (int i = 0; i < nodes.arraySize; i++)
            {
                var tag = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("recordsRule");
                if (tag.objectReferenceValue == null) continue;

                tag.objectReferenceValue = null;
                cleared++;
            }

            bool okDefiant = Apply(nodes, plan.Defiant, rule, false);
            bool okCoop    = Apply(nodes, plan.Cooperative, rule, true);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            touched++;

            report.AppendLine($"  {data.name}: cleared {cleared} tag(s) → " +
                              $"#{plan.Defiant} defiant{(okDefiant ? "" : " ✗ OUT OF RANGE")}, " +
                              $"#{plan.Cooperative} cooperative{(okCoop ? "" : " ✗ OUT OF RANGE")}");
            report.AppendLine($"      {plan.Note}");
        }

        AssetDatabase.SaveAssets();

        report.Append($"  {touched} conversation(s) rewritten");
        Debug.Log(report.ToString());
    }

    private static bool Apply(SerializedProperty nodes, int index, Object rule, bool cooperative)
    {
        if (index < 0 || index >= nodes.arraySize) return false;

        var node = nodes.GetArrayElementAtIndex(index);
        node.FindPropertyRelative("recordsRule").objectReferenceValue = rule;
        node.FindPropertyRelative("recordsAsCooperative").boolValue   = cooperative;

        return true;
    }

    // ─────────────────────────── Separate tidy-up ───────────────────────────

    /// <summary>
    /// Three unrelated faults the retagging pass turned up while reading every conversation.
    ///
    /// None of them break the game today, which is exactly why they are worth clearing: each one
    /// is a trap that only goes off the next time somebody inserts a node.
    /// </summary>
    [MenuItem("Rule of Horror/Chat/Tidy Conversation Data", false, 98)]
    private static void Tidy()
    {
        var report = new StringBuilder("[Tidy] conversation data\n");

        // Rule5 opens on node 14 while the list only goes up to 13. The runner falls back to 0 and
        // logs an error, and 0 happens to be the right opening line, so nothing looks broken.
        FixStart(report, Chat + "Rule/Rule5.asset", 0);

        // Rule3 #9 jumps to 12, which is past the end and therefore ends the conversation. That is
        // the intent, but it only works by accident — add one node and 12 becomes a real line.
        FixExit(report, Chat + "Rule/Rule3.asset", 9, ConversationEditing.EndTarget);

        // Rule2 #5 and #6 are the mysterious person speaking, yet both carry empty replies left
        // over from editing. The runner ignores replies on a non-player node, so they are dead
        // data — until somebody ticks "You" on one of them and two blank buttons appear.
        ClearReplies(report, Chat + "Rule/Rule2.asset", 5);
        ClearReplies(report, Chat + "Rule/Rule2.asset", 6);

        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    private static void FixStart(StringBuilder report, string path, int start)
    {
        var data = AssetDatabase.LoadAssetAtPath<ConversationData>(path);
        if (data == null) { report.AppendLine($"  ✗ missing {path}"); return; }

        var so  = new SerializedObject(data);
        var old = ConversationEditing.StartProperty(so).intValue;

        if (old == start) { report.AppendLine($"  {data.name}: start already #{start}"); return; }

        ConversationEditing.StartProperty(so).intValue = start;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);

        report.AppendLine($"  {data.name}: start #{old} → #{start} " +
                          $"(#{old} does not exist, the game was silently falling back to #0)");
    }

    private static void FixExit(StringBuilder report, string path, int node, int target)
    {
        var data = AssetDatabase.LoadAssetAtPath<ConversationData>(path);
        if (data == null) { report.AppendLine($"  ✗ missing {path}"); return; }

        var so    = new SerializedObject(data);
        var nodes = ConversationEditing.Nodes(so);
        if (node < 0 || node >= nodes.arraySize) { report.AppendLine($"  ✗ {data.name} has no #{node}"); return; }

        var next = nodes.GetArrayElementAtIndex(node).FindPropertyRelative("nextNode");
        if (next.intValue == target) { report.AppendLine($"  {data.name} #{node}: already {target}"); return; }

        report.AppendLine($"  {data.name} #{node}: next {next.intValue} → {target} (explicit END)");

        next.intValue = target;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);
    }

    private static void ClearReplies(StringBuilder report, string path, int node)
    {
        var data = AssetDatabase.LoadAssetAtPath<ConversationData>(path);
        if (data == null) { report.AppendLine($"  ✗ missing {path}"); return; }

        var so    = new SerializedObject(data);
        var nodes = ConversationEditing.Nodes(so);
        if (node < 0 || node >= nodes.arraySize) { report.AppendLine($"  ✗ {data.name} has no #{node}"); return; }

        var entry   = nodes.GetArrayElementAtIndex(node);
        var replies = ConversationEditing.Replies(entry);

        if (replies.arraySize == 0) { report.AppendLine($"  {data.name} #{node}: no stray replies"); return; }

        if (ConversationEditing.IsPlayer(entry))
        {
            report.AppendLine($"  ⚠ {data.name} #{node} is a player node now — leaving its " +
                              $"{replies.arraySize} replies alone");
            return;
        }

        report.AppendLine($"  {data.name} #{node}: removed {replies.arraySize} dead reply slot(s) " +
                          "from a non-player node");

        replies.ClearArray();
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);
    }
}
