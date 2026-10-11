using ScriptableObject;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The linear view of a ConversationData: every node in list order, with its exits shown as the
/// text they lead to rather than as bare indices.
///
/// The graph window is the one for structure. This one is for reading and rewriting a script
/// top to bottom, which is still how long stretches of dialogue get written, and it stays in sync
/// with the graph because both reshape the asset through ConversationEditing.
/// </summary>
public class ConversationEditorWindow : EditorWindow
{
    [MenuItem("Rule of Horror/Chat/Conversation List", false, 90)]
    private static void Open()
    {
        var window = GetWindow<ConversationEditorWindow>("Conversation List");
        window.minSize = new Vector2(460f, 400f);
        window.TryAdoptSelection();
    }

    [MenuItem("CONTEXT/ConversationData/Open Conversation List")]
    private static void OpenFromContext(MenuCommand command)
    {
        var window = GetWindow<ConversationEditorWindow>("Conversation List");
        window.Bind(command.context as ConversationData);
        window.Focus();
    }

    // ─────────────────────────── State ───────────────────────────

    private ConversationData _data;
    private SerializedObject _so;

    private Vector2 _scroll;
    private string  _filter = "";
    private bool    _showProblems = true;

    private int _selected     = -1;
    private int _focusRequest = -1;                       // scroll to this node on the next repaint

    private readonly System.Collections.Generic.Dictionary<int, Rect> _nodeRects =
        new System.Collections.Generic.Dictionary<int, Rect>();

    // Structural edits are deferred to the top of the next OnGUI — reshaping the array mid-layout
    // desynchronises IMGUI's layout and repaint passes
    private System.Action _pending;

    private int[]  _inbound;
    private bool[] _reachable;

    private int Count => _so != null ? ConversationEditing.Count(_so) : 0;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void OnEnable()
    {
        if (_data != null) Bind(_data);
        else               TryAdoptSelection();
    }

    private void OnSelectionChange()
    {
        if (TryAdoptSelection()) Repaint();
    }

    private bool TryAdoptSelection()
    {
        if (Selection.activeObject is not ConversationData data || data == _data) return false;

        Bind(data);
        return true;
    }

    private void Bind(ConversationData data)
    {
        _data     = data;
        _so       = data != null ? new SerializedObject(data) : null;
        _selected = -1;
        _nodeRects.Clear();
    }

    // ─────────────────────────── GUI ───────────────────────────

    private void OnGUI()
    {
        if (_pending != null)
        {
            var action = _pending;
            _pending = null;
            action();
        }

        DrawToolbar();

        if (_data == null || _so == null)
        {
            EditorGUILayout.HelpBox("Select a Conversation asset, or drop one in the field above.",
                                    MessageType.Info);
            return;
        }

        // The asset can change underneath us — the graph window, Odin's inspector, an Undo
        _so.Update();

        _inbound   = ConversationEditing.Inbound(_so);
        _reachable = ConversationEditing.Reachable(_so);

        DrawProblems();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        for (int i = 0; i < Count; i++)
        {
            if (!PassesFilter(i)) continue;

            DrawInsertBar(i);
            DrawNode(i);
        }

        DrawInsertBar(Count);
        EditorGUILayout.Space(8f);

        EditorGUILayout.EndScrollView();

        ApplyFocusRequest();

        _so.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var picked = EditorGUILayout.ObjectField(_data, typeof(ConversationData), false,
                                                     GUILayout.Width(200f)) as ConversationData;
            if (picked != _data) Bind(picked);

            GUILayout.Space(6f);
            _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(70f));

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_data == null))
            {
                if (GUILayout.Button("Graph", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                {
                    Selection.activeObject = _data;
                    EditorApplication.ExecuteMenuItem("Rule of Horror/Chat/Conversation Graph");
                }

                if (GUILayout.Button("Add Node", EditorStyles.toolbarButton, GUILayout.Width(75f)))
                {
                    int at = Count;
                    _pending = () => AddNode(at);
                }
            }
        }
    }

    private bool PassesFilter(int index)
    {
        if (string.IsNullOrWhiteSpace(_filter)) return true;

        var node = ConversationEditing.Node(_so, index);
        if (Contains(node.FindPropertyRelative("message").stringValue)) return true;

        var replies = ConversationEditing.Replies(node);
        for (int r = 0; r < replies.arraySize; r++)
            if (Contains(replies.GetArrayElementAtIndex(r).FindPropertyRelative("replyText").stringValue))
                return true;

        return false;

        bool Contains(string text)
            => text.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ─────────────────────────── One node ───────────────────────────

    private void DrawNode(int index)
    {
        var node     = ConversationEditing.Node(_so, index);
        var message  = node.FindPropertyRelative("message");
        var isPlayer = node.FindPropertyRelative("isPlayer");
        var scripted = node.FindPropertyRelative("scriptedLine");
        var offers   = node.FindPropertyRelative("offersReward");
        var next     = node.FindPropertyRelative("nextNode");
        var rule     = node.FindPropertyRelative("recordsRule");
        var coop     = node.FindPropertyRelative("recordsAsCooperative");

        Color previous = GUI.backgroundColor;
        if (index == _selected)                            GUI.backgroundColor = new Color(1f, 0.92f, 0.5f);
        else if (_reachable != null && index > 0 && !_reachable[index])
                                                           GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUI.backgroundColor = previous;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool isStart = index == ConversationEditing.StartIndex(_so);
                GUILayout.Label(new GUIContent($"#{index}", isStart ? "The conversation begins here" : null),
                                EditorStyles.boldLabel, GUILayout.Width(34f));

                if (isStart) GUILayout.Label("START", EditorStyles.miniBoldLabel, GUILayout.Width(44f));
                else if (GUILayout.Button(new GUIContent("start", "Begin the conversation on this node"),
                                          EditorStyles.miniButton, GUILayout.Width(44f)))
                {
                    int n = index;
                    _pending = () => ConversationEditing.SetStart(_so, n);
                }

                isPlayer.boolValue = EditorGUILayout.Popup(isPlayer.boolValue ? 1 : 0,
                                                           new[] { "Them", "You" }, GUILayout.Width(62f)) == 1;

                if (isPlayer.boolValue)
                {
                    scripted.boolValue = GUILayout.Toggle(scripted.boolValue, "Scripted",
                                                          EditorStyles.miniButton, GUILayout.Width(66f));
                    offers.boolValue   = GUILayout.Toggle(offers.boolValue, "Reward",
                                                          EditorStyles.miniButton, GUILayout.Width(60f));
                }

                GUILayout.FlexibleSpace();

                int into = _inbound != null ? _inbound[index] : 0;
                GUILayout.Label(new GUIContent($"in {into}",
                                    into == 0 && index > 0
                                        ? "No other node leads here — this line can never be reached"
                                        : "How many exits point at this node"),
                                EditorStyles.miniLabel, GUILayout.Width(36f));

                int self = index;
                if (MiniButton("Up", self > 0))             _pending = () => ConversationEditing.Swap(_so, self - 1, self);
                if (MiniButton("Dn", self < Count - 1))     _pending = () => ConversationEditing.Swap(_so, self, self + 1);
                if (MiniButton("Dup", true))                _pending = () => ConversationEditing.Duplicate(_so, self);
                if (MiniButton("Del", true))                _pending = () => ConversationEditing.Delete(_so, self);
            }

            message.stringValue = EditorGUILayout.TextArea(message.stringValue, WrapStyle,
                                                           GUILayout.MinHeight(36f));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Records rule", GUILayout.Width(84f));
                EditorGUILayout.PropertyField(rule, GUIContent.none);

                if (rule.objectReferenceValue != null)
                    coop.boolValue = GUILayout.Toggle(coop.boolValue, "cooperative",
                                                      EditorStyles.miniButton, GUILayout.Width(88f));
            }

            if (offers.boolValue && isPlayer.boolValue)
                EditorGUILayout.LabelField("Reward options are rolled at runtime — every choice goes to Next.",
                                           EditorStyles.miniLabel);

            if (ConversationEditing.Branches(node)) DrawReplies(index, node);
            else                                    DrawExit("Next", next, index);
        }

        if (Event.current.type == EventType.Repaint)
            _nodeRects[index] = GUILayoutUtility.GetLastRect();

        GUI.backgroundColor = previous;
    }

    private void DrawReplies(int owner, SerializedProperty node)
    {
        var replies = ConversationEditing.Replies(node);

        if (replies.arraySize == 0)
            EditorGUILayout.HelpBox("A player node with no replies stalls the chat — add one, or " +
                                    "mark the node Scripted so it continues on its own.", MessageType.Warning);

        for (int r = 0; r < replies.arraySize; r++)
        {
            var reply = replies.GetArrayElementAtIndex(r);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{r + 1}", EditorStyles.miniLabel, GUILayout.Width(14f));

                var text = reply.FindPropertyRelative("replyText");
                text.stringValue = EditorGUILayout.TextArea(text.stringValue, WrapStyle,
                                                            GUILayout.MinHeight(20f));

                DrawTargetPicker(reply.FindPropertyRelative("nextNode"), owner);

                // Reply targets are node indices, so removing one needs no renumbering
                int at = r, host = owner;
                if (MiniButton("X", true)) _pending = () => DeleteReply(host, at);
            }
        }

        if (GUILayout.Button("Add reply", EditorStyles.miniButton, GUILayout.Width(80f)))
            ConversationEditing.AddReply(node, Mathf.Min(owner + 1, Count));
    }

    private void DrawExit(string label, SerializedProperty target, int owner)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(84f));
            DrawTargetPicker(target, owner);
        }
    }

    /// <summary>The dropdown of real lines that replaces typing an index by hand.</summary>
    private void DrawTargetPicker(SerializedProperty target, int owner)
    {
        string[] options = ConversationEditing.TargetOptions(_so);

        int value   = target.intValue;
        bool inList = value >= 0 && value < Count;
        int choice  = inList ? value + 1 : 0;

        int picked = EditorGUILayout.Popup(choice, options);
        if (picked != choice)
            target.intValue = picked == 0 ? ConversationEditing.EndTarget : picked - 1;

        if (inList && value == owner)
            GUILayout.Label(new GUIContent("loop!", "This node points at itself and will never move on"),
                            EditorStyles.miniLabel, GUILayout.Width(34f));
        else if (inList && value < owner)
            GUILayout.Label(new GUIContent("back", "Jumps backwards — the chat replays from there"),
                            EditorStyles.miniLabel, GUILayout.Width(32f));

        using (new EditorGUI.DisabledScope(!inList))
        {
            if (GUILayout.Button(new GUIContent("Go", "Scroll to that node"),
                                 EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                _selected     = value;
                _focusRequest = value;
            }
        }
    }

    private void DrawInsertBar(int at)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(34f);
            if (GUILayout.Button("+ insert here", EditorStyles.miniButton, GUILayout.Width(96f)))
                _pending = () => AddNode(at);

            GUILayout.FlexibleSpace();
        }
    }

    // ─────────────────────────── Edits ───────────────────────────

    private void AddNode(int at)
    {
        // Give it a spot to the right of its neighbour so the graph window has something sane to
        // show, instead of stacking every new node on the origin
        Vector2 where = Vector2.zero;
        if (Count > 0)
        {
            int neighbour = Mathf.Clamp(at - 1, 0, Count - 1);
            where = ConversationEditing.Node(_so, neighbour)
                                       .FindPropertyRelative("graphPosition").vector2Value +
                    new Vector2(300f, 0f);
        }

        ConversationEditing.Insert(_so, at, where);

        _selected     = at;
        _focusRequest = at;
    }

    private void DeleteReply(int owner, int index)
    {
        _so.Update();

        ConversationEditing.Replies(ConversationEditing.Node(_so, owner))
                           .DeleteArrayElementAtIndex(index);

        _so.ApplyModifiedProperties();
    }

    // ─────────────────────────── Problems ───────────────────────────

    private void DrawProblems()
    {
        var problems = ConversationEditing.Problems(_so);
        if (problems.Count == 0) return;

        _showProblems = EditorGUILayout.Foldout(_showProblems, $"{problems.Count} problem(s)", true);
        if (!_showProblems) return;

        EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
    }

    // ─────────────────────────── Small helpers ───────────────────────────

    private void ApplyFocusRequest()
    {
        if (_focusRequest < 0 || Event.current.type != EventType.Repaint) return;
        if (!_nodeRects.TryGetValue(_focusRequest, out Rect rect)) return;

        _scroll.y     = Mathf.Max(0f, rect.y - 40f);
        _focusRequest = -1;
        Repaint();
    }

    private static bool MiniButton(string label, bool enabled)
    {
        using (new EditorGUI.DisabledScope(!enabled))
            return GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(32f));
    }

    private static GUIStyle _wrapStyle;

    private static GUIStyle WrapStyle =>
        _wrapStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
}
