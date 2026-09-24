using System.Collections.Generic;
using UnityEngine;

namespace Rule5
{
    /// <summary>
    /// สายสิญจน์ของ Rule 5 — เส้นทางที่ผู้เล่นต้องจับแล้วเดินตาม
    ///
    /// วิธีวาง / ย้ายตำแหน่ง:
    ///   - จุดหักของสาย = GameObject ลูกของตัวนี้ เรียงตามลำดับใน Hierarchy (P0, P1, P2, …)
    ///     ลากลูกไปไหน เส้นก็วิ่งตามทันทีใน Scene view (LineRenderer อัปเดตเองใน edit mode)
    ///   - ใช้ปุ่มใน Inspector (SacredThreadPathEditor) เพื่อ "เพิ่มจุดต่อท้าย" / "วางทุกจุดลงพื้น"
    ///   - ย้ายทั้งเส้น = ย้าย GameObject แม่ ตัวเดียว
    ///   - ผ้าแดงจุดเริ่ม / จุดจบ / จุดที่ปล่อยมือ ระบบสร้างให้เองตอน runtime จาก prefab ข้างล่าง
    ///   - สิ่งกีดขวาง (ThreadObstacle) วางไว้ที่ไหนก็ได้ใกล้เส้น — มันหาตำแหน่งบนเส้นให้เอง
    ///
    /// ตำแหน่งบนเส้นทั้งหมดวัดเป็น "ระยะทางจากจุดเริ่ม" (เมตร) — ผู้เล่น / ผี / ผ้าแดง ใช้เลขเดียวกันหมด
    /// </summary>
    [ExecuteAlways]
    public class SacredThreadPath : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Close the thread back to the first point (tick for a loop around the pavilion). Unticked: the player lets go automatically at the far end.")]
        [SerializeField] private bool loop = true;

        [Tooltip("Height above each waypoint the thread is drawn at, in metres (hand level). Waypoints themselves can sit on the ground.")]
        [SerializeField] private float threadHeight = 1.1f;

        [Header("Visual")]
        [Tooltip("LineRenderer used to draw the thread. Leave empty to reuse the one on this object, or have one added automatically.")]
        [SerializeField] private LineRenderer line;

        [SerializeField] private float threadWidth = 0.02f;

        [Tooltip("Hide the thread in play mode until the rule starts, so it is not visible during Relax. " +
                 "The line still shows in the Scene view while editing.")]
        [SerializeField] private bool hideUntilRuleStarts = true;

        [Header("Markers (red cloth)")]
        [Tooltip("Red cloth at the start point. Spawned when the rule begins, at waypoint P0.")]
        [SerializeField] private GameObject startMarkerPrefab;

        [Tooltip("Red cloth at the end point. Only used when the thread is not a loop.")]
        [SerializeField] private GameObject endMarkerPrefab;

        [Tooltip("Red cloth marking where the player let go. It moves to that spot, and the thread can only be grabbed again there.")]
        [SerializeField] private GameObject releaseMarkerPrefab;

        [Tooltip("Extra height for every cloth marker, on top of threadHeight. 0 = hangs level with the thread.")]
        [SerializeField] private float markerHeightOffset = 0f;

        // ── Runtime cache ──
        private readonly List<Vector3> _points   = new List<Vector3>();   // world, ยก threadHeight แล้ว
        private readonly List<float>   _cumulative = new List<float>();  // ระยะสะสมถึงจุด i
        private float _totalLength;
        private bool  _dirty = true;

        // ผ้าแดงห้ามเป็นลูกของตัวนี้ (ลูก = จุดหักของสาย) — เก็บไว้ใต้ root แยกต่างหาก
        private GameObject _startMarker, _endMarker, _releaseMarker;
        private Transform  _markerRoot;

        /// <summary>true = ปลายสายต่อกลับจุดเริ่ม</summary>
        public bool Loop => loop;

        /// <summary>ความยาวทั้งเส้น (เมตร) รวมช่วงปิด loop ถ้ามี</summary>
        public float TotalLength { get { EnsureBuilt(); return _totalLength; } }

        public int WaypointCount => transform.childCount;

        // ═══════════════════════════════════════════════════════════════
        #region Lifecycle
        // ═══════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            EnsureLine();
            _dirty = true;

            // ตอนเล่นจริงต้องซ่อนไว้ก่อน — ไม่งั้นเส้นสายสิญจน์โชว์ตั้งแต่โหมด Relax
            // (edit mode ปล่อยให้เห็นตามปกติ ไม่งั้นจัดวางจุดไม่ได้)
            if (Application.isPlaying && hideUntilRuleStarts) SetThreadVisible(false);
        }

        /// <summary>เปิด/ปิดเส้นสาย — Walker เรียกตอนเริ่ม/จบกฎ</summary>
        public void SetThreadVisible(bool visible)
        {
            EnsureLine();
            if (line != null) line.enabled = visible;
        }

        private void OnValidate()
        {
            _dirty = true;
        }

        private void OnTransformChildrenChanged() => _dirty = true;

        private void Update()
        {
            // edit mode: ตรวจว่ามีจุดลูกขยับไหม จะได้วาดเส้นใหม่ให้เห็นทันทีตอนลาก
            if (!Application.isPlaying)
            {
                foreach (Transform c in transform)
                {
                    if (!c.hasChanged) continue;
                    c.hasChanged = false;
                    _dirty = true;
                }
                if (transform.hasChanged) { transform.hasChanged = false; _dirty = true; }
            }

            if (_dirty) Rebuild();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Build
        // ═══════════════════════════════════════════════════════════════

        private void EnsureLine()
        {
            if (line == null) line = GetComponent<LineRenderer>();
            if (line == null)
            {
                line = gameObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.textureMode   = LineTextureMode.Tile;
            }
        }

        private void EnsureBuilt()
        {
            if (_dirty) Rebuild();
        }

        /// <summary>อ่านตำแหน่งลูกทุกตัว → คำนวณระยะสะสม + อัปเดต LineRenderer (บังคับทำใหม่)</summary>
        public void Rebuild()
        {
            _dirty = false;
            _points.Clear();
            _cumulative.Clear();
            _totalLength = 0f;

            foreach (Transform c in transform)
            {
                if (!c.gameObject.activeInHierarchy && Application.isPlaying) continue;
                _points.Add(c.position + Vector3.up * threadHeight);
            }

            if (_points.Count == 0)
            {
                if (line != null) line.positionCount = 0;
                return;
            }

            _cumulative.Add(0f);
            for (int i = 1; i < _points.Count; i++)
            {
                _totalLength += Vector3.Distance(_points[i - 1], _points[i]);
                _cumulative.Add(_totalLength);
            }
            if (loop && _points.Count > 1)
                _totalLength += Vector3.Distance(_points[_points.Count - 1], _points[0]);

            EnsureLine();
            if (line != null)
            {
                line.loop          = loop && _points.Count > 1;
                line.startWidth    = threadWidth;
                line.endWidth      = threadWidth;
                line.positionCount = _points.Count;
                line.SetPositions(_points.ToArray());
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Queries (ระยะทางบนเส้น ↔ ตำแหน่งโลก)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>ทำระยะทางให้อยู่ในช่วง [0, TotalLength] — loop จะวน, ไม่ loop จะ clamp</summary>
        public float WrapDistance(float d)
        {
            EnsureBuilt();
            if (_totalLength <= 0f) return 0f;
            if (loop)
            {
                d %= _totalLength;
                if (d < 0f) d += _totalLength;
                return d;
            }
            return Mathf.Clamp(d, 0f, _totalLength);
        }

        /// <summary>ตำแหน่งโลกบนเส้น (ที่ระดับสาย) ณ ระยะ d จากจุดเริ่ม</summary>
        public Vector3 GetPoint(float d)
        {
            EnsureBuilt();
            if (_points.Count == 0) return transform.position;
            if (_points.Count == 1) return _points[0];

            d = WrapDistance(d);
            FindSegment(d, out int a, out int b, out float t);
            return Vector3.Lerp(_points[a], _points[b], t);
        }

        /// <summary>ตำแหน่งบนพื้น (ลบ threadHeight ออก) — ใช้วางเท้าผู้เล่น / ผี</summary>
        public Vector3 GetGroundPoint(float d) => GetPoint(d) - Vector3.up * threadHeight;

        /// <summary>ทิศทางเดินหน้า (แนวราบ, normalized) ณ ระยะ d</summary>
        public Vector3 GetForward(float d)
        {
            EnsureBuilt();
            if (_points.Count < 2) return transform.forward;

            d = WrapDistance(d);
            FindSegment(d, out int a, out int b, out _);
            Vector3 dir = _points[b] - _points[a];
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        }

        /// <summary>ทิศขวาของเส้น ณ ระยะ d (แนวราบ)</summary>
        public Vector3 GetRight(float d) => Vector3.Cross(Vector3.up, GetForward(d));

        /// <summary>
        /// หาระยะบนเส้นที่ใกล้ตำแหน่งโลกที่สุด (ฉายลงบนทุก segment เอาอันใกล้สุด)
        /// ใช้ให้สิ่งกีดขวาง / จุด spawn หาตัวเองบนเส้นโดยไม่ต้องกรอกเลขเอง
        /// </summary>
        public float ClosestDistanceAlong(Vector3 worldPos, out float distanceFromLine)
        {
            EnsureBuilt();
            distanceFromLine = float.MaxValue;
            if (_points.Count == 0) return 0f;
            if (_points.Count == 1)
            {
                distanceFromLine = Vector3.Distance(worldPos, _points[0]);
                return 0f;
            }

            float best = 0f;
            int   segCount = loop ? _points.Count : _points.Count - 1;

            for (int i = 0; i < segCount; i++)
            {
                Vector3 a = _points[i];
                Vector3 b = _points[(i + 1) % _points.Count];
                Vector3 ab = b - a;
                float   len2 = ab.sqrMagnitude;
                float   t = len2 > 0f ? Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / len2) : 0f;
                Vector3 p = a + ab * t;

                // วัดแค่แนวราบ — ความสูงของสายกับความสูงของวัตถุไม่เกี่ยวกัน
                Vector3 diff = worldPos - p; diff.y = 0f;
                float   dist = diff.magnitude;
                if (dist >= distanceFromLine) continue;

                distanceFromLine = dist;
                best = _cumulative[i] + Mathf.Sqrt(len2) * t;
            }
            return best;
        }

        /// <summary>ระยะทางตามเส้นจาก from → to ในทิศเดินหน้า (คิด loop ให้)</summary>
        public float ForwardGap(float from, float to)
        {
            EnsureBuilt();
            float gap = to - from;
            if (loop && _totalLength > 0f)
            {
                gap %= _totalLength;
                if (gap < 0f) gap += _totalLength;
            }
            return gap;
        }

        private void FindSegment(float d, out int a, out int b, out float t)
        {
            int n = _points.Count;

            // ช่วงปิด loop (จุดสุดท้าย → จุดแรก)
            if (loop && d >= _cumulative[n - 1])
            {
                a = n - 1; b = 0;
                float segLen = Vector3.Distance(_points[a], _points[b]);
                t = segLen > 0f ? (d - _cumulative[a]) / segLen : 0f;
                return;
            }

            a = 0;
            for (int i = 1; i < n; i++)
            {
                if (d < _cumulative[i]) { a = i - 1; break; }
                a = i;
            }
            b = Mathf.Min(a + 1, n - 1);
            if (a == b) { t = 0f; return; }

            float len = _cumulative[b] - _cumulative[a];
            t = len > 0f ? (d - _cumulative[a]) / len : 0f;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Markers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>สร้างผ้าแดงจุดเริ่ม / จุดจบ — เรียกตอนเริ่มกฎ</summary>
        public void ShowEndpointMarkers()
        {
            EnsureBuilt();
            SetThreadVisible(true);
            if (_startMarker == null && startMarkerPrefab != null)
                _startMarker = Instantiate(startMarkerPrefab, MarkerRoot);
            PlaceMarker(_startMarker, 0f);

            if (!loop)
            {
                if (_endMarker == null && endMarkerPrefab != null)
                    _endMarker = Instantiate(endMarkerPrefab, MarkerRoot);
                PlaceMarker(_endMarker, _totalLength);
            }
        }

        private Transform MarkerRoot
        {
            get
            {
                if (_markerRoot == null) _markerRoot = new GameObject(name + "_Markers").transform;
                return _markerRoot;
            }
        }

        /// <summary>ย้ายผ้าแดง "จุดปล่อยมือ" ไปที่ระยะ d — ผู้เล่นต้องกลับมาจับตรงนี้</summary>
        public void ShowReleaseMarker(float d)
        {
            if (_releaseMarker == null && releaseMarkerPrefab != null)
                _releaseMarker = Instantiate(releaseMarkerPrefab, MarkerRoot);
            if (_releaseMarker == null) return;

            _releaseMarker.SetActive(true);
            PlaceMarker(_releaseMarker, d);
        }

        public void HideReleaseMarker()
        {
            if (_releaseMarker != null) _releaseMarker.SetActive(false);
        }

        /// <summary>ลบผ้าแดงทั้งหมด — ตอนจบกฎ / ตาย</summary>
        public void ClearMarkers()
        {
            if (hideUntilRuleStarts) SetThreadVisible(false);

            DestroyMarker(ref _startMarker);
            DestroyMarker(ref _endMarker);
            DestroyMarker(ref _releaseMarker);
            if (_markerRoot != null)
            {
                var go = _markerRoot.gameObject;
                _markerRoot = null;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
        }

        private void PlaceMarker(GameObject marker, float d)
        {
            if (marker == null) return;
            marker.transform.position = GetPoint(d) + Vector3.up * markerHeightOffset;
            marker.transform.rotation = Quaternion.LookRotation(GetForward(d), Vector3.up);
        }

        private static void DestroyMarker(ref GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            go = null;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Gizmos
        // ═══════════════════════════════════════════════════════════════

        private void OnDrawGizmos()
        {
            EnsureBuilt();
            if (_points.Count == 0) return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            for (int i = 0; i < _points.Count; i++)
            {
                Gizmos.DrawSphere(_points[i], 0.08f);
                if (i + 1 < _points.Count) Gizmos.DrawLine(_points[i], _points[i + 1]);
            }
            if (loop && _points.Count > 1) Gizmos.DrawLine(_points[_points.Count - 1], _points[0]);

            // จุดเริ่ม = แดง, ทิศเดินหน้า = ลูกศรเล็ก
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_points[0], 0.14f);
            Gizmos.DrawRay(_points[0], GetForward(0f) * 1f);
        }

        #endregion
    }
}
