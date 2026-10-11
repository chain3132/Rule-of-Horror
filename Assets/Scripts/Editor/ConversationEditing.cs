using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The shared rules for reading and reshaping a ConversationData, used by both the list editor
/// and the graph editor.
///
/// It lives apart from either window because the renumbering is the part that must not drift:
/// if the two windows disagreed about what inserting a node does to every stored jump, editing a
/// script in one and then the other would quietly corrupt it.
///
/// Everything works through SerializedProperty, so Undo and the asset's dirty flag come for free.
/// </summary>
internal static class ConversationEditing
{
    /// <summary>
    /// What we store for "the conversation stops here".
    ///
    /// ConversationRunner ends on any index outside the list, so several values would work, but a
    /// negative one reads unmistakably in the asset and can never collide with a real node after
    /// a renumber.
    /// </summary>
    public const int EndTarget = -1;

    // ─────────────────────────── Access ───────────────────────────

    public static SerializedProperty Nodes(SerializedObject so) => so.FindProperty("nodes");

    public static int Count(SerializedObject so) => Nodes(so).arraySize;

    public static SerializedProperty Node(SerializedObject so, int index)
        => Nodes(so).GetArrayElementAtIndex(index);

    public static bool IsPlayer(SerializedProperty node) => node.FindPropertyRelative("isPlayer").boolValue;

    public static SerializedProperty Replies(SerializedProperty node) => node.FindPropertyRelative("replies");

    /// <summary>true = this node waits for the player to pick an answer, so it has several exits.</summary>
    public static bool Branches(SerializedProperty node)
        => IsPlayer(node) &&
           !node.FindPropertyRelative("scriptedLine").boolValue &&
           !node.FindPropertyRelative("offersReward").boolValue;

    // ─────────────────────────── Exits ───────────────────────────
    // "Exit" means one way out of a node, whether that is the single Next field or one of several
    // replies. Treating both the same way is what lets the graph draw and rewire them identically.

    public static int ExitCount(SerializedProperty node)
        => Branches(node) ? Mathf.Max(1, Replies(node).arraySize) : 1;

    /// <summary>The int property holding exit <paramref name="exit"/>'s destination.</summary>
    public static SerializedProperty ExitTarget(SerializedProperty node, int exit)
    {
        var replies = Replies(node);

        if (Branches(node) && replies.arraySize > 0 && exit < replies.arraySize)
            return replies.GetArrayElementAtIndex(exit).FindPropertyRelative("nextNode");

        return node.FindPropertyRelative("nextNode");
    }

    public static string ExitLabel(SerializedProperty node, int exit)
    {
        var replies = Replies(node);

        if (Branches(node) && replies.arraySize > 0 && exit < replies.arraySize)
            return Condense(replies.GetArrayElementAtIndex(exit).FindPropertyRelative("replyText").stringValue, 28);

        return "Next";
    }

    public static IEnumerable<int> Targets(SerializedProperty node)
    {
        int exits = ExitCount(node);
        for (int e = 0; e < exits; e++) yield return ExitTarget(node, e).intValue;
    }

    // ─────────────────────────── Renumbering ───────────────────────────

    // ─────────────────────────── Where the script begins ───────────────────────────

    public static SerializedProperty StartProperty(SerializedObject so) => so.FindProperty("startNode");

    /// <summary>
    /// The node the conversation opens on, clamped to something that exists.
    ///
    /// Deleting the start node can leave the stored value pointing at nothing, and every reader
    /// here would then have to handle that separately — clamping once keeps the rest simple, and
    /// Problems() still reports the broken value so it does not pass unnoticed.
    /// </summary>
    public static int StartIndex(SerializedObject so)
    {
        int count = Count(so);
        if (count == 0) return 0;

        int start = StartProperty(so).intValue;
        return start >= 0 && start < count ? start : 0;
    }

    public static void SetStart(SerializedObject so, int index)
    {
        so.Update();
        StartProperty(so).intValue = Mathf.Clamp(index, 0, Mathf.Max(0, Count(so) - 1));
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Runs every stored jump in the asset through <paramref name="map"/> exactly once.
    ///
    /// The start index goes through it too. It is a stored jump like any other, and forgetting it
    /// would mean a conversation silently begins on the wrong line after an insert.
    ///
    /// Nodes that branch still carry a stale value in their own Next field, and it is remapped
    /// as well — otherwise switching such a node to Scripted later would resurrect an index that
    /// stopped meaning anything several edits ago.
    /// </summary>
    public static void RemapTargets(SerializedObject so, Func<int, int> map)
    {
        var nodes = Nodes(so);

        for (int i = 0; i < nodes.arraySize; i++)
            RemapNode(nodes.GetArrayElementAtIndex(i), map);

        var start = StartProperty(so);
        if (start != null && start.intValue >= 0) start.intValue = map(start.intValue);
    }

    private static void RemapNode(SerializedProperty node, Func<int, int> map)
    {
        var next = node.FindPropertyRelative("nextNode");
        if (next.intValue >= 0) next.intValue = map(next.intValue);

        var replies = Replies(node);
        for (int r = 0; r < replies.arraySize; r++)
        {
            var target = replies.GetArrayElementAtIndex(r).FindPropertyRelative("nextNode");
            if (target.intValue >= 0) target.intValue = map(target.intValue);
        }
    }

    /// <summary>
    /// Inserts a blank node, pushing every jump at or past that position up by one.
    ///
    /// The remap runs before the insert on purpose: while it runs the list still has its old
    /// numbering, which is exactly the numbering the stored jumps refer to.
    /// </summary>
    public static void Insert(SerializedObject so, int at, Vector2 graphPosition)
    {
        so.Update();

        var nodes = Nodes(so);
        at = Mathf.Clamp(at, 0, nodes.arraySize);

        RemapTargets(so, t => t >= at ? t + 1 : t);
        nodes.InsertArrayElementAtIndex(at);

        var node = nodes.GetArrayElementAtIndex(at);
        node.FindPropertyRelative("message").stringValue              = "";
        node.FindPropertyRelative("isPlayer").boolValue               = false;
        node.FindPropertyRelative("scriptedLine").boolValue           = false;
        node.FindPropertyRelative("offersReward").boolValue           = false;
        node.FindPropertyRelative("recordsRule").objectReferenceValue = null;
        node.FindPropertyRelative("recordsAsCooperative").boolValue   = true;
        node.FindPropertyRelative("graphPosition").vector2Value       = graphPosition;
        node.FindPropertyRelative("nextNode").intValue                = at + 1 < nodes.arraySize ? at + 1 : EndTarget;
        Replies(node).ClearArray();

        so.ApplyModifiedProperties();
    }

    public static void Duplicate(SerializedObject so, int at)
    {
        so.Update();

        var nodes = Nodes(so);

        // Unity's insert copies the element already there, so only jumps PAST it shift
        RemapTargets(so, t => t > at ? t + 1 : t);
        nodes.InsertArrayElementAtIndex(at);

        var copy = nodes.GetArrayElementAtIndex(at + 1);
        var pos  = copy.FindPropertyRelative("graphPosition");
        pos.vector2Value += new Vector2(40f, 40f);          // offset it, or it hides under the original

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Deletes a node and sends whatever pointed at it to wherever that node pointed.
    ///
    /// Bypassing is what you want nearly every time a line is cut out of the middle of a script.
    /// The alternative — leaving those jumps dangling — turns one deletion into a hunt for every
    /// exit that used to lead there, which is the exact problem this editor exists to remove.
    ///
    /// A branching node has several exits and no single line to bypass to, so anything pointing
    /// at it becomes END instead.
    /// </summary>
    public static void Delete(SerializedObject so, int at)
    {
        so.Update();

        var nodes = Nodes(so);
        var node  = nodes.GetArrayElementAtIndex(at);

        bool branches = Branches(node) && Replies(node).arraySize > 0;
        int  bypass   = branches ? EndTarget : node.FindPropertyRelative("nextNode").intValue;

        // The bypass is itself an old index, so renumber it the same way as everything else
        if (bypass == at)     bypass = EndTarget;
        else if (bypass > at) bypass -= 1;

        RemapTargets(so, t => t == at ? bypass : t > at ? t - 1 : t);

        nodes.DeleteArrayElementAtIndex(at);
        so.ApplyModifiedProperties();
    }

    public static void Swap(SerializedObject so, int a, int b)
    {
        so.Update();

        RemapTargets(so, t => t == a ? b : t == b ? a : t);
        Nodes(so).MoveArrayElement(a, b);

        so.ApplyModifiedProperties();
    }

    /// <summary>Moves a node to any position in the list, renumbering every jump to match.</summary>
    public static void Move(SerializedObject so, int from, int to)
    {
        so.Update();

        var nodes = Nodes(so);
        to = Mathf.Clamp(to, 0, nodes.arraySize - 1);
        if (from == to) return;

        // Everything the moved node passes over slides one place the other way
        RemapTargets(so, t => t == from ? to
                            : from < to  ? (t > from && t <= to ? t - 1 : t)
                                         : (t >= to && t < from ? t + 1 : t));

        nodes.MoveArrayElement(from, to);
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Adds a node immediately upstream of <paramref name="index"/>: whatever used to arrive at
    /// that node now arrives at the new one, which then leads into it.
    ///
    /// This is the operation that makes "write a scene before the opening line" a single click.
    /// Without it the only way to put a line in front of the first node is to make it node 0,
    /// which renumbers the whole file by hand.
    /// </summary>
    public static void InsertBefore(SerializedObject so, int index, Vector2 position)
    {
        bool wasStart = StartIndex(so) == index;

        Insert(so, index, position);          // the old node is now at index + 1

        so.Update();

        var nodes = Nodes(so);
        for (int i = 0; i < nodes.arraySize; i++)
        {
            // The new node keeps its own jump into the old one — that is what holds the chain
            if (i == index) continue;

            RemapNode(nodes.GetArrayElementAtIndex(i), t => t == index + 1 ? index : t);
        }

        // Inserting ahead of the opening line means the new line is the opening line now
        if (wasStart) StartProperty(so).intValue = index;

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Adds a node immediately downstream: the node now leads into the new one, and the new one
    /// carries on to wherever the node used to go.
    ///
    /// Only meaningful for a node with a single exit — a branching node has no one "after".
    /// </summary>
    public static void InsertAfter(SerializedObject so, int index, Vector2 position)
    {
        var node     = Node(so, index);
        bool branches = Branches(node) && Replies(node).arraySize > 0;
        int downstream = branches ? EndTarget : node.FindPropertyRelative("nextNode").intValue;

        int at = index + 1;
        Insert(so, at, position);

        so.Update();

        // downstream was read before the insert, so renumber it the same way Insert did
        int after = downstream >= at ? downstream + 1 : downstream;

        Node(so, at).FindPropertyRelative("nextNode").intValue = after;
        if (!branches) Node(so, index).FindPropertyRelative("nextNode").intValue = at;

        so.ApplyModifiedProperties();
    }

    public static void AddReply(SerializedProperty node, int fallbackTarget)
    {
        var replies = Replies(node);
        int size    = replies.arraySize;

        replies.arraySize = size + 1;

        var added = replies.GetArrayElementAtIndex(size);
        added.FindPropertyRelative("replyText").stringValue = "";
        added.FindPropertyRelative("nextNode").intValue     = fallbackTarget;
    }

    // ─────────────────────────── Analysis ───────────────────────────

    /// <summary>How many exits lead into each node.</summary>
    public static int[] Inbound(SerializedObject so)
    {
        int count = Count(so);
        var into  = new int[count];

        for (int i = 0; i < count; i++)
            foreach (int target in Targets(Node(so, i)))
                if (target >= 0 && target < count) into[target]++;

        return into;
    }

    /// <summary>
    /// Which nodes the player can actually arrive at, walked from the start node the way the
    /// runner would.
    ///
    /// This is stricter than "something points at it": two orphaned nodes pointing at each other
    /// both have an inbound count, and neither can ever be reached.
    /// </summary>
    public static bool[] Reachable(SerializedObject so)
    {
        int count = Count(so);
        var seen  = new bool[count];
        if (count == 0) return seen;

        int start = StartIndex(so);

        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen[start] = true;

        while (queue.Count > 0)
        {
            foreach (int target in Targets(Node(so, queue.Dequeue())))
            {
                if (target < 0 || target >= count || seen[target]) continue;

                seen[target] = true;
                queue.Enqueue(target);
            }
        }

        return seen;
    }

    public static List<string> Problems(SerializedObject so)
    {
        var problems = new List<string>();
        var reached  = Reachable(so);

        int count = Count(so);
        int start = StartIndex(so);
        int stored = StartProperty(so).intValue;

        if (count > 0 && (stored < 0 || stored >= count))
            problems.Add($"Start Node is set to {stored}, which does not exist — the game will fall back to #0");

        for (int i = 0; i < count; i++)
        {
            var node = Node(so, i);

            if (Branches(node) && Replies(node).arraySize == 0)
                problems.Add($"#{i} waits for a reply but has none — the chat stops dead there");

            foreach (int target in Targets(node))
                if (target == i)
                    problems.Add($"#{i} points at itself and can never move on");

            if (!reached[i])
                problems.Add($"#{i} is unreachable — nothing leads to it from the start (#{start})");
        }

        return problems;
    }

    // ─────────────────────────── Text ───────────────────────────

    public static string NodeLabel(SerializedObject so, int index, int maxChars)
    {
        var node = Node(so, index);

        return $"#{index}  {(IsPlayer(node) ? "You" : "Them")}: " +
               Condense(node.FindPropertyRelative("message").stringValue, maxChars);
    }

    /// <summary>
    /// Collapses a message into one preview line.
    ///
    /// Forward slashes become a lookalike character because Unity's popup control reads "/" as a
    /// submenu separator, which would shatter any line containing one into nested menus.
    /// </summary>
    public static string Condense(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(empty)";

        var sb    = new StringBuilder(text.Length);
        bool space = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }

            if (space && sb.Length > 0) sb.Append(' ');
            space = false;

            sb.Append(c == '/' ? '∕' : c);
        }

        string line = sb.ToString();
        return line.Length <= maxChars ? line : line.Substring(0, maxChars - 1) + "…";
    }

    /// <summary>The dropdown entries for picking a jump target — entry 0 is always END.</summary>
    public static string[] TargetOptions(SerializedObject so)
    {
        int count   = Count(so);
        var options = new string[count + 1];

        options[0] = "— END —";
        for (int i = 0; i < count; i++) options[i + 1] = NodeLabel(so, i, 52);

        return options;
    }
}
