using System.Collections.Generic;
using ScriptableObject;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// A node graph for ConversationData: nodes you drag around, and jumps you wire by dragging a
/// line from an exit onto the node it should lead to.
///
/// Why a graph rather than a better list: a branching script is a shape, and the shape is what a
/// writer needs to see. A list can show where one line goes; only a graph shows that two replies
/// rejoin three lines later, or that a whole limb is stranded.
///
/// Node positions are stored on the asset (ChatNode.graphPosition), so the layout travels with
/// the file instead of living in one person's EditorPrefs.
///
/// Zoom scales the geometry and the font sizes rather than applying a GUI matrix. That keeps
/// every hit test in one coordinate space, which is the part that usually breaks in hand-rolled
/// node editors, and it lets the body text drop out cleanly when it gets too small to read.
/// </summary>
public class ConversationGraphWindow : EditorWindow
{
    // ─────────────────────────── Geometry (graph space, before zoom) ───────────────────────────

    private const float NodeWidth   = 230f;
    private const float HeaderHeight = 22f;
    private const float BodyHeight   = 44f;
    private const float ExitHeight   = 18f;
    private const float NodePadding  = 6f;
    private const float PortSize     = 10f;

    private const float ColumnSpacing = 300f;
    private const float RowSpacing    = 40f;

    /// <summary>Below this zoom the message text is unreadable, so the nodes collapse to headers.</summary>
    private const float BodyZoomCutoff = 0.65f;

    private const float SidePanelWidth = 310f;

    // ─────────────────────────── State ───────────────────────────

    private ConversationData _data;
    private SerializedObject _so;

    private Vector2 _pan  = new Vector2(40f, 40f);
    private float   _zoom = 1f;

    private int _selected = -1;
    private Vector2 _sidePanelScroll;

    private enum Drag { None, Canvas, Node, Wire }

    private Drag    _drag;
    private int     _dragNode   = -1;
    private Vector2 _dragOffset;
    private int     _wireNode   = -1;
    private int     _wireExit   = -1;

    private Rect _canvasRect;

    // Structural edits run at the top of the next OnGUI — reshaping the array mid-layout
    // desynchronises IMGUI's layout and repaint passes
    private System.Action _pending;

    private int[]  _inbound;
    private bool[] _reachable;

    private int Count => _so != null ? ConversationEditing.Count(_so) : 0;

    // ─────────────────────────── Opening ───────────────────────────

    [MenuItem("Rule of Horror/Chat/Conversation Graph", false, 89)]
    private static void Open()
    {
        var window = GetWindow<ConversationGraphWindow>("Conversation Graph");
        window.minSize = new Vector2(720f, 420f);
        window.TryAdoptSelection();
    }

    [MenuItem("CONTEXT/ConversationData/Open Conversation Graph")]
    private static void OpenFromContext(MenuCommand command)
    {
        var window = GetWindow<ConversationGraphWindow>("Conversation Graph");
        window.Bind(command.context as ConversationData);
        window.Focus();
    }

    /// <summary>Double-clicking a conversation asset opens the graph.</summary>
    [OnOpenAsset]
    private static bool OnOpenConversation(int instanceId, int line)
    {
        if (EditorUtility.InstanceIDToObject(instanceId) is not ConversationData data) return false;

        var window = GetWindow<ConversationGraphWindow>("Conversation Graph");
        window.Bind(data);
        window.Focus();
        return true;
    }

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
        _drag     = Drag.None;

        if (_so != null && NeedsLayout()) AutoLayout();
        FrameAll();
    }

    /// <summary>Every node sitting at the origin means nobody has ever arranged this script.</summary>
    private bool NeedsLayout()
    {
        for (int i = 0; i < Count; i++)
            if (ConversationEditing.Node(_so, i).FindPropertyRelative("graphPosition").vector2Value !=
                Vector2.zero) return false;

        return Count > 0;
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

        _so.Update();

        _inbound   = ConversationEditing.Inbound(_so);
        _reachable = ConversationEditing.Reachable(_so);

        _canvasRect = new Rect(0f, EditorStyles.toolbar.fixedHeight,
                               position.width - SidePanelWidth,
                               position.height - EditorStyles.toolbar.fixedHeight);

        DrawCanvas();
        DrawSidePanel();

        HandleCanvasEvents();

        // A node drag writes a position every mouse-move, so it skips the undo stack to avoid
        // burying real edits under hundreds of one-pixel entries
        if (_drag == Drag.Node) _so.ApplyModifiedPropertiesWithoutUndo();
        else                    _so.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var picked = EditorGUILayout.ObjectField(_data, typeof(ConversationData), false,
                                                     GUILayout.Width(200f)) as ConversationData;
            if (picked != _data) Bind(picked);

            using (new EditorGUI.DisabledScope(_data == null))
            {
                if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                    _pending = AutoLayout;

                if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    FrameAll();

                if (GUILayout.Button("Add Node", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    Vector2 where = ScreenToGraph(_canvasRect.center);
                    _pending = () => AddNode(Count, where);
                }
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(34f));
            _zoom = GUILayout.HorizontalSlider(_zoom, 0.4f, 1.4f, GUILayout.Width(90f));

            if (_data != null)
            {
                var problems = ConversationEditing.Problems(_so);
                if (problems.Count > 0 &&
                    GUILayout.Button($"⚠ {problems.Count}", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                    Debug.LogWarning($"[Conversation] {_data.name}\n  " + string.Join("\n  ", problems), _data);
            }
        }
    }

    // ─────────────────────────── Canvas ───────────────────────────

    private void DrawCanvas()
    {
        EditorGUI.DrawRect(_canvasRect, EditorGUIUtility.isProSkin
            ? new Color(0.17f, 0.17f, 0.19f)
            : new Color(0.55f, 0.55f, 0.58f));

        GUI.BeginClip(_canvasRect);
        {
            // The clip shifts the origin, so everything inside works in canvas-local coordinates
            Rect local = new Rect(0f, 0f, _canvasRect.width, _canvasRect.height);

            DrawGrid(local, 20f, new Color(1f, 1f, 1f, 0.04f));
            DrawGrid(local, 100f, new Color(1f, 1f, 1f, 0.07f));

            DrawWires();
            DrawDraggedWire();

            for (int i = 0; i < Count; i++) DrawNode(i);
        }
        GUI.EndClip();
    }

    private void DrawGrid(Rect area, float step, Color color)
    {
        float spacing = step * _zoom;
        if (spacing < 6f) return;                       // too dense to read, and expensive to draw

        float startX = Mathf.Repeat(_pan.x * _zoom, spacing);
        float startY = Mathf.Repeat(_pan.y * _zoom, spacing);

        for (float x = startX; x < area.width; x += spacing)
            EditorGUI.DrawRect(new Rect(x, 0f, 1f, area.height), color);

        for (float y = startY; y < area.height; y += spacing)
            EditorGUI.DrawRect(new Rect(0f, y, area.width, 1f), color);
    }

    private void DrawWires()
    {
        for (int i = 0; i < Count; i++)
        {
            var node  = ConversationEditing.Node(_so, i);
            int exits = ConversationEditing.ExitCount(node);

            for (int e = 0; e < exits; e++)
            {
                int target = ConversationEditing.ExitTarget(node, e).intValue;
                if (target < 0 || target >= Count) continue;          // END draws no wire

                bool hot = i == _selected || target == _selected;

                Color color = target < i
                    ? new Color(0.95f, 0.65f, 0.25f)                  // jumps backwards
                    : new Color(0.55f, 0.75f, 0.95f);

                if (hot) color = new Color(1f, 0.92f, 0.45f);

                DrawWire(OutputPort(i, e).center, InputPort(target).center, color, hot ? 3.5f : 2.5f);
            }
        }
    }

    private void DrawDraggedWire()
    {
        if (_drag != Drag.Wire || _wireNode < 0) return;

        // This runs inside GUI.BeginClip, which already rebases Event.current.mousePosition onto
        // the clip's origin — subtracting the canvas position again would offset it twice
        DrawWire(OutputPort(_wireNode, _wireExit).center,
                 Event.current.mousePosition,
                 Color.white, 2.5f);

        Repaint();
    }

    private static void DrawWire(Vector2 from, Vector2 to, Color color, float width)
    {
        // The horizontal tangent grows with the gap so short hops stay tight and long ones sweep
        float tangent = Mathf.Clamp(Mathf.Abs(to.x - from.x) * 0.6f, 30f, 160f);

        Handles.DrawBezier(from, to,
                           from + Vector2.right * tangent,
                           to   + Vector2.left  * tangent,
                           color, null, width);
    }

    private void DrawNode(int index)
    {
        Rect rect = NodeScreenRect(index);
        if (!rect.Overlaps(new Rect(0f, 0f, _canvasRect.width, _canvasRect.height))) return;

        var node    = ConversationEditing.Node(_so, index);
        bool player = ConversationEditing.IsPlayer(node);

        // ── body ──
        bool isStart = index == ConversationEditing.StartIndex(_so);

        Color fill = player ? new Color(0.22f, 0.29f, 0.36f) : new Color(0.26f, 0.26f, 0.29f);
        if (_reachable != null && !_reachable[index]) fill = new Color(0.38f, 0.22f, 0.22f);

        EditorGUI.DrawRect(rect, fill);
        DrawBorder(rect, index == _selected ? new Color(1f, 0.85f, 0.3f) : new Color(0f, 0f, 0f, 0.5f),
                   index == _selected ? 2f : 1f);

        // ── header ──
        float h = HeaderHeight * _zoom;
        Rect header = new Rect(rect.x, rect.y, rect.width, h);
        EditorGUI.DrawRect(header, isStart ? new Color(0.24f, 0.50f, 0.32f)
                                 : player  ? new Color(0.30f, 0.44f, 0.56f)
                                           : new Color(0.36f, 0.36f, 0.40f));

        string flags = "";
        if (player && node.FindPropertyRelative("offersReward").boolValue) flags += " ★";
        if (player && node.FindPropertyRelative("scriptedLine").boolValue) flags += " ▸";
        if (node.FindPropertyRelative("recordsRule").objectReferenceValue != null) flags += " ▣";

        GUI.Label(Inset(header, 6f * _zoom),
                  $"{(isStart ? "START  " : "")}#{index}  {(player ? "You" : "Them")}{flags}",
                  Scaled(EditorStyles.boldLabel, 11));

        float y = rect.y + h;

        // ── message ──
        if (_zoom >= BodyZoomCutoff)
        {
            Rect body = new Rect(rect.x, y, rect.width, BodyHeight * _zoom);
            GUI.Label(Inset(body, 6f * _zoom),
                      ConversationEditing.Condense(node.FindPropertyRelative("message").stringValue, 120),
                      Wrapped(10));

            y += BodyHeight * _zoom;
        }

        // ── exits ──
        int exits = ConversationEditing.ExitCount(node);
        for (int e = 0; e < exits; e++)
        {
            Rect row = new Rect(rect.x, y, rect.width, ExitHeight * _zoom);

            int target = ConversationEditing.ExitTarget(node, e).intValue;
            string label = ConversationEditing.ExitLabel(node, e);
            if (target < 0 || target >= Count) label += "  → END";

            GUI.Label(Inset(row, 8f * _zoom), label, Scaled(EditorStyles.miniLabel, 9));

            Rect port = OutputPort(index, e);
            EditorGUI.DrawRect(port, target >= 0 && target < Count
                ? new Color(0.6f, 0.8f, 1f)
                : new Color(0.5f, 0.5f, 0.5f));

            y += ExitHeight * _zoom;
        }

        // ── input port ──
        Rect input = InputPort(index);
        EditorGUI.DrawRect(input, _inbound != null && !isStart && _inbound[index] == 0
            ? new Color(0.95f, 0.45f, 0.45f)                 // nothing leads here
            : new Color(0.8f, 0.8f, 0.8f));
    }

    private static void DrawBorder(Rect rect, Color color, float width)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
    }

    // ─────────────────────────── Coordinates ───────────────────────────

    private Vector2 NodeGraphPosition(int index)
        => ConversationEditing.Node(_so, index).FindPropertyRelative("graphPosition").vector2Value;

    private float NodeGraphHeight(int index)
    {
        float height = HeaderHeight + ExitHeight * ConversationEditing.ExitCount(ConversationEditing.Node(_so, index));
        if (_zoom >= BodyZoomCutoff) height += BodyHeight;

        return height + NodePadding;
    }

    /// <summary>Canvas-local rect of a node, i.e. relative to the clip, not to the window.</summary>
    private Rect NodeScreenRect(int index)
    {
        Vector2 p = (NodeGraphPosition(index) + _pan) * _zoom;
        return new Rect(p.x, p.y, NodeWidth * _zoom, NodeGraphHeight(index) * _zoom);
    }

    private Rect InputPort(int index)
    {
        Rect rect = NodeScreenRect(index);
        float s = PortSize * _zoom;

        return new Rect(rect.x - s * 0.5f, rect.y + HeaderHeight * _zoom * 0.5f - s * 0.5f, s, s);
    }

    private Rect OutputPort(int index, int exit)
    {
        Rect rect = NodeScreenRect(index);
        float s = PortSize * _zoom;

        float y = rect.y + HeaderHeight * _zoom
                + (_zoom >= BodyZoomCutoff ? BodyHeight * _zoom : 0f)
                + (exit + 0.5f) * ExitHeight * _zoom;

        return new Rect(rect.xMax - s * 0.5f, y - s * 0.5f, s, s);
    }

    private Vector2 ScreenToGraph(Vector2 windowPoint)
        => (windowPoint - _canvasRect.position) / _zoom - _pan;

    // ─────────────────────────── Events ───────────────────────────

    private void HandleCanvasEvents()
    {
        Event e = Event.current;
        Vector2 local = e.mousePosition - _canvasRect.position;

        bool inCanvas = _canvasRect.Contains(e.mousePosition);

        switch (e.type)
        {
            case EventType.ScrollWheel when inCanvas:
            {
                // Zoom toward the cursor, so the thing under the mouse stays under the mouse
                Vector2 before = ScreenToGraph(e.mousePosition);
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.03f), 0.4f, 1.4f);
                _pan += ScreenToGraph(e.mousePosition) - before;

                e.Use();
                Repaint();
                break;
            }

            case EventType.MouseDown when inCanvas && e.button == 1:
                ShowContextMenu(local, e.mousePosition);
                e.Use();
                break;

            case EventType.MouseDown when inCanvas && e.button == 0:
                BeginLeftDrag(local);
                e.Use();
                break;

            case EventType.MouseDown when inCanvas && e.button == 2:
                _drag = Drag.Canvas;
                e.Use();
                break;

            case EventType.MouseDrag:
                if (_drag == Drag.Canvas)
                {
                    _pan += e.delta / _zoom;
                    e.Use();
                    Repaint();
                }
                else if (_drag == Drag.Node && _dragNode >= 0)
                {
                    ConversationEditing.Node(_so, _dragNode)
                                       .FindPropertyRelative("graphPosition")
                                       .vector2Value = ScreenToGraph(e.mousePosition) - _dragOffset;
                    e.Use();
                    Repaint();
                }
                else if (_drag == Drag.Wire)
                {
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (_drag == Drag.Wire) FinishWire(local, e.mousePosition);
                _drag     = Drag.None;
                _dragNode = -1;
                Repaint();
                break;

            // Only when no text field has focus, or Delete would eat a node while the writer is
            // editing the message in the side panel
            case EventType.KeyDown when _selected >= 0 && e.keyCode == KeyCode.Delete &&
                                        GUIUtility.keyboardControl == 0:
            {
                int victim = _selected;
                _selected = -1;
                _pending  = () => ConversationEditing.Delete(_so, victim);

                e.Use();
                break;
            }
        }
    }

    private void BeginLeftDrag(Vector2 local)
    {
        // Drop focus out of the side panel's text fields, so typing does not keep going into a
        // box the user has visually left behind
        GUIUtility.keyboardControl = 0;

        // Ports take priority over the node body — they sit on the border and overlap it
        for (int i = 0; i < Count; i++)
        {
            int exits = ConversationEditing.ExitCount(ConversationEditing.Node(_so, i));

            for (int exit = 0; exit < exits; exit++)
            {
                if (!Grow(OutputPort(i, exit), 4f).Contains(local)) continue;

                _drag     = Drag.Wire;
                _wireNode = i;
                _wireExit = exit;
                _selected = i;
                return;
            }
        }

        for (int i = Count - 1; i >= 0; i--)                 // topmost first
        {
            if (!NodeScreenRect(i).Contains(local)) continue;

            _drag       = Drag.Node;
            _dragNode   = i;
            _selected   = i;
            _dragOffset = ScreenToGraph(local + _canvasRect.position) - NodeGraphPosition(i);

            Undo.RecordObject(_data, "Move Conversation Node");
            return;
        }

        _drag     = Drag.Canvas;
        _selected = -1;
    }

    /// <summary>
    /// Dropping a wire: on a node it rewires the jump, on empty canvas it offers to end there or
    /// to spawn a new node already connected.
    /// </summary>
    private void FinishWire(Vector2 local, Vector2 windowPoint)
    {
        int from = _wireNode, exit = _wireExit;
        _wireNode = _wireExit = -1;

        if (from < 0) return;

        for (int i = 0; i < Count; i++)
        {
            if (!NodeScreenRect(i).Contains(local)) continue;

            ConversationEditing.ExitTarget(ConversationEditing.Node(_so, from), exit).intValue = i;
            _so.ApplyModifiedProperties();
            return;
        }

        Vector2 where = ScreenToGraph(windowPoint);
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("End the conversation here"), false, () =>
        {
            _so.Update();
            ConversationEditing.ExitTarget(ConversationEditing.Node(_so, from), exit).intValue =
                ConversationEditing.EndTarget;
            _so.ApplyModifiedProperties();
        });

        menu.AddItem(new GUIContent("Create a node here and connect"), false, () =>
        {
            // Inserting right after the source keeps reading order close to play order, and the
            // renumbering in Insert moves the stored exit along with it
            int at = from + 1;
            ConversationEditing.Insert(_so, at, where);

            _so.Update();
            ConversationEditing.ExitTarget(ConversationEditing.Node(_so, from), exit).intValue = at;
            _so.ApplyModifiedProperties();

            _selected = at;
        });

        menu.AddItem(new GUIContent("Leave it as it is"), false, () => { });
        menu.ShowAsContext();
    }

    private void ShowContextMenu(Vector2 local, Vector2 windowPoint)
    {
        var menu  = new GenericMenu();
        Vector2 where = ScreenToGraph(windowPoint);

        int hit = -1;
        for (int i = Count - 1; i >= 0; i--)
            if (NodeScreenRect(i).Contains(local)) { hit = i; break; }

        if (hit >= 0)
        {
            int node = hit;
            _selected = node;

            Vector2 anchor = NodeGraphPosition(node);
            bool branches  = ConversationEditing.Branches(ConversationEditing.Node(_so, node)) &&
                             ConversationEditing.Replies(ConversationEditing.Node(_so, node)).arraySize > 0;

            // Putting a line in front of another one used to mean renumbering the file by hand;
            // this is the whole reason the renumbering lives in ConversationEditing
            menu.AddItem(new GUIContent("Insert node before this"), false, () =>
            {
                ConversationEditing.InsertBefore(_so, node, anchor - new Vector2(ColumnSpacing, 0f));
                _selected = node;
            });

            if (branches)
                menu.AddDisabledItem(new GUIContent("Insert node after this  (node branches)"));
            else
                menu.AddItem(new GUIContent("Insert node after this"), false, () =>
                {
                    ConversationEditing.InsertAfter(_so, node, anchor + new Vector2(ColumnSpacing, 0f));
                    _selected = node + 1;
                });

            menu.AddSeparator("");

            if (node == ConversationEditing.StartIndex(_so))
                menu.AddDisabledItem(new GUIContent("Already the start"));
            else
                menu.AddItem(new GUIContent("Make this the start"), false,
                             () => ConversationEditing.SetStart(_so, node));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Add reply"), false, () =>
            {
                _so.Update();
                var target = ConversationEditing.Node(_so, node);
                target.FindPropertyRelative("isPlayer").boolValue = true;
                ConversationEditing.AddReply(target, ConversationEditing.EndTarget);
                _so.ApplyModifiedProperties();
            });

            menu.AddItem(new GUIContent("Duplicate"), false, () => ConversationEditing.Duplicate(_so, node));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                _selected = -1;
                ConversationEditing.Delete(_so, node);
            });
        }
        else
        {
            menu.AddItem(new GUIContent("Add node here"), false, () => AddNode(Count, where));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Auto layout"), false, AutoLayout);
            menu.AddItem(new GUIContent("Frame all"), false, FrameAll);
        }

        menu.ShowAsContext();
    }

    private void AddNode(int at, Vector2 where)
    {
        ConversationEditing.Insert(_so, at, where);
        _selected = Mathf.Min(at, Count - 1);
    }

    // ─────────────────────────── Side panel ───────────────────────────

    private void DrawSidePanel()
    {
        Rect panel = new Rect(position.width - SidePanelWidth, EditorStyles.toolbar.fixedHeight,
                              SidePanelWidth, position.height - EditorStyles.toolbar.fixedHeight);

        EditorGUI.DrawRect(panel, EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f)
            : new Color(0.76f, 0.76f, 0.76f));

        GUILayout.BeginArea(Inset(panel, 8f));
        {
            if (_selected < 0 || _selected >= Count)
            {
                EditorGUILayout.LabelField("No node selected", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Click a node to edit its text.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(10f);
                DrawLegend();

                GUILayout.EndArea();
                return;
            }

            _sidePanelScroll = EditorGUILayout.BeginScrollView(_sidePanelScroll);
            DrawNodeEditor(_selected);
            EditorGUILayout.EndScrollView();
        }
        GUILayout.EndArea();
    }

    private void DrawNodeEditor(int index)
    {
        var node     = ConversationEditing.Node(_so, index);
        var message  = node.FindPropertyRelative("message");
        var isPlayer = node.FindPropertyRelative("isPlayer");
        var scripted = node.FindPropertyRelative("scriptedLine");
        var offers   = node.FindPropertyRelative("offersReward");
        var rule     = node.FindPropertyRelative("recordsRule");
        var coop     = node.FindPropertyRelative("recordsAsCooperative");

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Node #{index}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"in {(_inbound != null ? _inbound[index] : 0)}", EditorStyles.miniLabel);
        }

        isPlayer.boolValue = EditorGUILayout.Popup("Speaker", isPlayer.boolValue ? 1 : 0,
                                                   new[] { "Them", "You" }) == 1;

        if (isPlayer.boolValue)
        {
            scripted.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent("Scripted line", "Shows and moves on by itself, with no choice to make"),
                scripted.boolValue);

            offers.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent("Offers a reward", "The options are rolled at runtime by RewardSystem"),
                offers.boolValue);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Message");
        message.stringValue = EditorGUILayout.TextArea(message.stringValue, SideText,
                                                       GUILayout.MinHeight(70f));

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(rule, new GUIContent("Records rule"));
        if (rule.objectReferenceValue != null)
            coop.boolValue = EditorGUILayout.ToggleLeft("Cooperative wording", coop.boolValue);

        EditorGUILayout.Space(6f);

        if (ConversationEditing.Branches(node)) DrawReplyEditor(index, node);
        else                                     DrawSingleExit(node);

        EditorGUILayout.Space(10f);

        bool isStart = index == ConversationEditing.StartIndex(_so);
        using (new EditorGUI.DisabledScope(isStart))
        {
            if (GUILayout.Button(isStart ? "This is the start of the conversation"
                                         : "Make this the start"))
            {
                int n = index;
                _pending = () => ConversationEditing.SetStart(_so, n);
            }
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Duplicate")) { int n = index; _pending = () => ConversationEditing.Duplicate(_so, n); }
            if (GUILayout.Button("Delete"))
            {
                int n = index;
                _selected = -1;
                _pending  = () => ConversationEditing.Delete(_so, n);
            }
        }
    }

    private void DrawReplyEditor(int index, SerializedProperty node)
    {
        var replies = ConversationEditing.Replies(node);

        EditorGUILayout.LabelField($"Replies ({replies.arraySize})", EditorStyles.boldLabel);

        if (replies.arraySize == 0)
            EditorGUILayout.HelpBox("A player node with no replies stalls the chat — add one, or " +
                                    "tick Scripted line.", MessageType.Warning);

        for (int r = 0; r < replies.arraySize; r++)
        {
            var reply = replies.GetArrayElementAtIndex(r);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{r + 1}", EditorStyles.miniLabel, GUILayout.Width(16f));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        int at = r, owner = index;
                        _pending = () =>
                        {
                            _so.Update();
                            ConversationEditing.Replies(ConversationEditing.Node(_so, owner))
                                               .DeleteArrayElementAtIndex(at);
                            _so.ApplyModifiedProperties();
                        };
                    }
                }

                var text = reply.FindPropertyRelative("replyText");
                text.stringValue = EditorGUILayout.TextArea(text.stringValue, SideText,
                                                            GUILayout.MinHeight(34f));

                DrawTargetPicker("Goes to", reply.FindPropertyRelative("nextNode"));
            }
        }

        if (GUILayout.Button("Add reply"))
            ConversationEditing.AddReply(node, ConversationEditing.EndTarget);
    }

    private void DrawSingleExit(SerializedProperty node)
    {
        if (node.FindPropertyRelative("offersReward").boolValue &&
            ConversationEditing.IsPlayer(node))
            EditorGUILayout.LabelField("Every rolled option goes to the same place.",
                                       EditorStyles.wordWrappedMiniLabel);

        DrawTargetPicker("Next", node.FindPropertyRelative("nextNode"));
    }

    private void DrawTargetPicker(string label, SerializedProperty target)
    {
        string[] options = ConversationEditing.TargetOptions(_so);

        int value   = target.intValue;
        bool inList = value >= 0 && value < Count;

        using (new EditorGUILayout.HorizontalScope())
        {
            int picked = EditorGUILayout.Popup(label, inList ? value + 1 : 0, options);
            if (picked != (inList ? value + 1 : 0))
                target.intValue = picked == 0 ? ConversationEditing.EndTarget : picked - 1;

            using (new EditorGUI.DisabledScope(!inList))
                if (GUILayout.Button("Go", EditorStyles.miniButton, GUILayout.Width(28f)))
                {
                    _selected = value;
                    CenterOn(value);
                }
        }
    }

    private void DrawLegend()
    {
        EditorGUILayout.LabelField("Reading the graph", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Green header = where the conversation begins.\n" +
            "Blue node = the player speaks.\n" +
            "Red node = unreachable from the start.\n" +
            "Red input dot = nothing leads here.\n" +
            "Orange wire = jumps backwards.\n" +
            "★ offers a reward   ▸ scripted   ▣ records a rule",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Drag a node to move it.\n" +
            "Drag from a right-hand dot onto a node to wire them.\n" +
            "Drop a wire on empty space to end there or make a new node.\n" +
            "Drag empty space to pan, wheel to zoom, right-click for the menu.\n" +
            "Delete removes the selected node and reconnects around it.\n" +
            "Right-click a node to insert one before or after it, or to make it the start.",
            EditorStyles.wordWrappedMiniLabel);
    }

    // ─────────────────────────── Layout ───────────────────────────

    /// <summary>
    /// Arranges the script left to right by how far each node is from the opening line.
    ///
    /// Depth from the start node rather than list order, because list order is only how the asset
    /// stores things while depth is the shape the writer is actually reading. Nodes the walk never
    /// reaches are parked in a column of their own, which makes stranded limbs obvious at a glance.
    /// </summary>
    private void AutoLayout()
    {
        if (_so == null || Count == 0) return;

        _so.Update();

        int count = Count;
        int start = ConversationEditing.StartIndex(_so);

        var depth = new int[count];
        for (int i = 0; i < count; i++) depth[i] = -1;

        var queue = new Queue<int>();
        queue.Enqueue(start);
        depth[start] = 0;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();

            foreach (int target in ConversationEditing.Targets(ConversationEditing.Node(_so, current)))
            {
                if (target < 0 || target >= count || depth[target] >= 0) continue;

                depth[target] = depth[current] + 1;
                queue.Enqueue(target);
            }
        }

        int orphanColumn = 0;
        for (int i = 0; i < count; i++) orphanColumn = Mathf.Max(orphanColumn, depth[i]);
        orphanColumn += 2;

        var rowInColumn = new Dictionary<int, int>();

        for (int i = 0; i < count; i++)
        {
            int column = depth[i] >= 0 ? depth[i] : orphanColumn;

            rowInColumn.TryGetValue(column, out int row);
            rowInColumn[column] = row + 1;

            float height = HeaderHeight + BodyHeight + NodePadding +
                           ExitHeight * ConversationEditing.ExitCount(ConversationEditing.Node(_so, i));

            ConversationEditing.Node(_so, i).FindPropertyRelative("graphPosition").vector2Value =
                new Vector2(column * ColumnSpacing, row * (height + RowSpacing));
        }

        _so.ApplyModifiedProperties();
        FrameAll();
    }

    private void FrameAll()
    {
        if (_so == null || Count == 0) return;

        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;

        for (int i = 0; i < Count; i++)
        {
            Vector2 p = NodeGraphPosition(i);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p + new Vector2(NodeWidth, NodeGraphHeight(i)));
        }

        float width  = Mathf.Max(1f, position.width - SidePanelWidth);
        float height = Mathf.Max(1f, position.height - EditorStyles.toolbar.fixedHeight);

        _zoom = Mathf.Clamp(Mathf.Min((width - 60f) / Mathf.Max(1f, max.x - min.x),
                                      (height - 60f) / Mathf.Max(1f, max.y - min.y)), 0.4f, 1f);

        Vector2 centre = (min + max) * 0.5f;
        _pan = new Vector2(width, height) * 0.5f / _zoom - centre;

        Repaint();
    }

    private void CenterOn(int index)
    {
        if (index < 0 || index >= Count) return;

        float width  = Mathf.Max(1f, position.width - SidePanelWidth);
        float height = Mathf.Max(1f, position.height - EditorStyles.toolbar.fixedHeight);

        Vector2 centre = NodeGraphPosition(index) + new Vector2(NodeWidth, NodeGraphHeight(index)) * 0.5f;
        _pan = new Vector2(width, height) * 0.5f / _zoom - centre;

        Repaint();
    }

    // ─────────────────────────── Small helpers ───────────────────────────

    private static Rect Inset(Rect rect, float by)
        => new Rect(rect.x + by, rect.y + by, rect.width - by * 2f, rect.height - by * 2f);

    private static Rect Grow(Rect rect, float by)
        => new Rect(rect.x - by, rect.y - by, rect.width + by * 2f, rect.height + by * 2f);

    // Styles are rebuilt whenever the zoom changes, since scaling the font is what stands in for
    // a GUI matrix here
    private readonly Dictionary<int, GUIStyle> _scaled = new Dictionary<int, GUIStyle>();
    private float _styleZoom = -1f;

    private GUIStyle Scaled(GUIStyle source, int baseSize)
    {
        if (!Mathf.Approximately(_styleZoom, _zoom)) { _scaled.Clear(); _styleZoom = _zoom; }

        int key = baseSize * 2 + (source == EditorStyles.boldLabel ? 1 : 0);
        if (_scaled.TryGetValue(key, out var style)) return style;

        style = new GUIStyle(source)
        {
            fontSize  = Mathf.Max(7, Mathf.RoundToInt(baseSize * _zoom)),
            alignment = TextAnchor.MiddleLeft,
            clipping  = TextClipping.Clip,
        };
        style.normal.textColor = Color.white;

        _scaled[key] = style;
        return style;
    }

    // The side panel sits on the normal editor background, so its text fields keep the normal
    // editor colours — unlike the node bodies, which are drawn on dark boxes of our own
    private static GUIStyle _sideText;

    private static GUIStyle SideText =>
        _sideText ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    private GUIStyle Wrapped(int baseSize)
    {
        if (!Mathf.Approximately(_styleZoom, _zoom)) { _scaled.Clear(); _styleZoom = _zoom; }

        int key = 1000 + baseSize;
        if (_scaled.TryGetValue(key, out var style)) return style;

        style = new GUIStyle(EditorStyles.label)
        {
            fontSize  = Mathf.Max(7, Mathf.RoundToInt(baseSize * _zoom)),
            wordWrap  = true,
            alignment = TextAnchor.UpperLeft,
            clipping  = TextClipping.Clip,
        };
        style.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

        _scaled[key] = style;
        return style;
    }
}
