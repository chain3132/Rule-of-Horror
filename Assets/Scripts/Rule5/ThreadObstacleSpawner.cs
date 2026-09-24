using System.Collections.Generic;
using Player;
using UnityEngine;

namespace Rule5
{
    /// <summary>
    /// ทยอยปล่อยสิ่งกีดขวางบนสายสิญจน์ "ตอนที่ผู้เล่นมองไม่เห็น"
    ///
    /// ไม่ spawn มาพร้อมกันทีเดียวตอนเริ่มกฎ — เดินไปสักพักจึงจะมีตัวใหม่โผล่ข้างหน้า
    /// และจะโผล่เฉพาะจุดที่เป็น "อับสายตา" ของผู้เล่นตอนนั้น:
    ///   1. อยู่นอกกรวยสายตา (มุมกับทิศที่กล้องมองเกิน sightHalfAngle) — เช่น หลังโค้ง / ข้างหลัง
    ///   2. หรืออยู่ในสายตาแต่มีอะไรบัง (Raycast จากกล้องไปจุดนั้นโดนของขวาง)
    ///   3. หรือไกลเกิน farEnoughDistance (ไกลจนมืด/มองไม่ชัด — กันเคสสายตรงยาวที่ไม่มีอะไรบังเลย)
    /// ถ้าจุดไหนยังไม่เข้าเงื่อนไข ก็รอไปก่อน แล้วลองใหม่ทุก checkInterval วินาที
    ///
    /// แหล่งของสิ่งกีดขวางมี 2 ทาง ใช้ร่วมกันได้:
    ///   • pool  — ThreadObstacle ที่วางไว้ในฉากล่วงหน้า (โรงศพ / ศาลพระภูมิ / ถุงห่อศพ ที่ต้องจัดวางกับฉาก)
    ///             ระบบซ่อนให้ตอนเริ่มกฎ แล้วค่อยเปิดตามจังหวะ
    ///   • prefabs — สุ่มเกิดบนเส้นตรงไหนก็ได้ (เหมาะกับ "สายสิญจน์พันกัน")
    ///
    /// วางตัวนี้ไว้ GameObject ไหนก็ได้ แล้วลาก walker ใส่ — Rule5 เป็นคนสั่ง BeginRule/EndRuleCleanup
    /// </summary>
    public class ThreadObstacleSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SacredThreadWalker walker;

        [Header("Pool (obstacles pre-placed in the scene)")]
        [Tooltip("ThreadObstacles already placed in the scene. They are hidden when the rule starts, then revealed one at a time.\n" +
                 "Leave empty to collect every one in the scene, including disabled ones.")]
        [SerializeField] private ThreadObstacle[] pool;

        [Tooltip("Obstacles that should already be there when the rule starts, instead of being revealed later. Must also be in the pool.")]
        [SerializeField] private ThreadObstacle[] revealAtStart;

        [Header("Prefabs (spawned at random spots on the thread)")]
        [Tooltip("Prefabs carrying a ThreadObstacle, for obstacles that can appear anywhere, such as a tangled thread.")]
        [SerializeField] private ThreadObstacle[] prefabs;

        [Tooltip("Maximum number of prefab obstacles spawned per run of the rule (0 = never use prefabs).")]
        [SerializeField] private int maxPrefabSpawns = 3;

        [Header("Reveal pacing")]
        [Tooltip("Metres the player must have walked before the first obstacle may appear.")]
        [SerializeField] private float firstSpawnAfterWalked = 12f;

        [Tooltip("Extra metres that must be walked between reveals, picked at random in this range.")]
        [SerializeField] private float minWalkBetweenSpawns = 18f;
        [SerializeField] private float maxWalkBetweenSpawns = 30f;

        [Tooltip("How many un-passed obstacles may exist at once. Keeps the player from running into them back to back.")]
        [SerializeField] private int maxActiveAtOnce = 2;

        [Tooltip("How often to retry, in seconds, while a reveal is due but no out-of-sight spot has been found yet.")]
        [SerializeField] private float checkInterval = 0.4f;

        [Header("Allowed reveal positions")]
        [Tooltip("Minimum distance ahead of the player along the thread, in metres. Closer than this risks the player seeing it appear.")]
        [SerializeField] private float minAheadDistance = 14f;

        [Tooltip("Maximum distance ahead, in metres. Too far and the player may catch sight of it from another angle on the way there.")]
        [SerializeField] private float maxAheadDistance = 60f;

        [Header("Out-of-sight test")]
        [Tooltip("Half-angle of the view cone, in degrees. A spot further off the camera direction than this counts as out of sight.")]
        [SerializeField] private float sightHalfAngle = 65f;

        [Tooltip("Beyond this distance a spot counts as too far to make out, so a reveal is allowed even in plain view (0 = disable this test).")]
        [SerializeField] private float farEnoughDistance = 45f;

        [Tooltip("Layers that count as blocking line of sight (walls, buildings, large trees).")]
        [SerializeField] private LayerMask occluderMask = ~0;

        [Tooltip("Height above the ground the raycast target is raised to, in metres. Roughly chest height on the obstacle.")]
        [SerializeField] private float sightCheckHeight = 0.8f;

        [Tooltip("Untick to let obstacles appear regardless of line of sight. For debugging.")]
        [SerializeField] private bool requireOutOfSight = true;

        [Header("Debug")]
        [SerializeField] private bool logSpawns = true;

        // ── Runtime ──
        private readonly List<ThreadObstacle> _pending  = new List<ThreadObstacle>();   // ตัวใน pool ที่ยังไม่โผล่
        private readonly List<ThreadObstacle> _spawned  = new List<ThreadObstacle>();   // ที่โผล่แล้ว (รวม prefab)
        private readonly List<ThreadObstacle> _instances = new List<ThreadObstacle>();  // ที่ Instantiate มา ต้องทำลายตอนจบ

        private bool  _active;
        private float _walkedTotal;       // ระยะที่ผู้เล่นเดินไปแล้วรวม (เมตร)
        private float _lastDistance;
        private float _nextSpawnAtWalked;
        private float _checkTimer;
        private int   _prefabSpawnCount;

        /// <summary>จำนวนสิ่งกีดขวางที่ยังรออยู่ในคิว (debug)</summary>
        public int PendingCount => _pending.Count;

        // ═══════════════════════════════════════════════════════════════
        #region Begin / End
        // ═══════════════════════════════════════════════════════════════

        /// <summary>เริ่มระบบ — Rule5 เรียกตอนเริ่มเล่นจริง</summary>
        public void BeginRule()
        {
            if (walker == null || walker.Thread == null)
            {
                Debug.LogError("[Rule5] ThreadObstacleSpawner ยังไม่ได้ใส่ walker (หรือ walker ไม่มี thread)", this);
                return;
            }

            _active            = true;
            _walkedTotal       = 0f;
            _lastDistance      = walker.Distance;
            _nextSpawnAtWalked = firstSpawnAfterWalked;
            _checkTimer        = 0f;
            _prefabSpawnCount  = 0;

            DestroyInstances();
            _spawned.Clear();
            _pending.Clear();

            // pool: ถ้าไม่ได้ระบุ ให้กวาดหาทั้งฉาก (ตัวที่ถูกซ่อนไว้ตอนรอบก่อนก็ต้องเจอด้วย)
            var candidates = pool != null && pool.Length > 0
                ? pool
                : FindObjectsByType<ThreadObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var o in candidates)
            {
                if (o == null) continue;

                // ต้องเปิด GameObject ชั่วคราวก่อน Bind — ตัวที่ถูกซ่อนแบบปิดทั้ง GameObject
                // จะอ่าน transform.position ได้อยู่ แต่เปิดไว้ก่อนแล้วค่อยซ่อนทำให้สภาพเริ่มต้นแน่นอน
                bool wasActive = o.gameObject.activeSelf;
                if (!wasActive) o.gameObject.SetActive(true);

                bool ok = o.Bind(walker.Thread);
                o.Hide();

                if (ok) _pending.Add(o);
            }

            // ตัวที่ผู้ทำฉากอยากให้มีตั้งแต่แรก
            if (revealAtStart != null)
                foreach (var o in revealAtStart)
                    if (o != null && _pending.Contains(o)) Spawn(o, "ตั้งแต่เริ่มกฎ");

            if (logSpawns)
                Debug.Log($"[Rule5] ThreadObstacleSpawner พร้อม — มีในคิว {_pending.Count} ตัว " +
                          $"(+prefab สุ่มได้อีก {maxPrefabSpawns})", this);
        }

        /// <summary>ปิดระบบ + เก็บของ — Rule5 เรียกตอนจบกฎ / ตาย</summary>
        public void EndRuleCleanup()
        {
            _active = false;

            foreach (var o in _pending) if (o != null) o.Hide();
            foreach (var o in _spawned) if (o != null) o.Hide();

            DestroyInstances();
            _pending.Clear();
            _spawned.Clear();
        }

        private void DestroyInstances()
        {
            foreach (var o in _instances)
                if (o != null) Destroy(o.gameObject);
            _instances.Clear();
        }

        private void OnDisable() => EndRuleCleanup();

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Update
        // ═══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!_active || walker == null || walker.Thread == null) return;

            TrackWalkedDistance();

            if (_walkedTotal < _nextSpawnAtWalked) return;
            if (CountUnpassed() >= maxActiveAtOnce) return;

            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = checkInterval;

            TrySpawnOne();
        }

        /// <summary>
        /// นับระยะที่ผู้เล่น "เดินไปแล้วจริง" — ใช้ delta ของตำแหน่งบนเส้น ไม่ใช่ค่าดิบ
        /// เพราะสายเป็น loop ค่าจะวนกลับ 0 และการปล่อย/จับใหม่ทำให้ตำแหน่งกระโดดได้
        /// </summary>
        private void TrackWalkedDistance()
        {
            float d = walker.Distance;

            if (walker.IsWalking)
            {
                float delta = walker.Thread.ForwardGap(_lastDistance, d);

                // ก้าวเดียวไม่มีทางไกลเกินครึ่งเส้น — ถ้าเกินคือกระโดด (จับใหม่ที่อื่น) ไม่นับ
                if (delta > 0f && delta < walker.Thread.TotalLength * 0.5f) _walkedTotal += delta;
            }

            _lastDistance = d;
        }

        private int CountUnpassed()
        {
            int n = 0;
            foreach (var o in _spawned) if (o != null && o.IsBlocking) n++;
            return n;
        }

        /// <summary>เลือกตัวที่อยู่ข้างหน้า + อับสายตา แล้วปล่อยออกมา 1 ตัว</summary>
        private void TrySpawnOne()
        {
            // pool ก่อนเสมอ (เป็นตัวที่จัดวางกับฉากไว้ สวยกว่าตัวสุ่ม)
            var thread = walker.Thread;
            float here = walker.Distance;

            ThreadObstacle best     = null;
            float          bestGap  = float.MaxValue;

            foreach (var o in _pending)
            {
                if (o == null) continue;

                float gap = thread.ForwardGap(here, o.BlockCenter);
                if (gap < minAheadDistance || gap > maxAheadDistance) continue;
                if (!IsOutOfSight(thread.GetGroundPoint(o.BlockCenter))) continue;

                // เอาตัวที่ใกล้ที่สุดในช่วงที่ยอมรับได้ — ผู้เล่นจะได้เจอเร็ว ไม่ต้องเดินอีกครึ่งรอบ
                if (gap >= bestGap) continue;
                bestGap = gap;
                best    = o;
            }

            if (best != null) { Spawn(best, $"ข้างหน้า {bestGap:0} ม."); return; }

            TrySpawnPrefab(here);
        }

        /// <summary>สุ่มจุดบนเส้นข้างหน้าแล้วเกิด prefab ที่นั่น (สำหรับสายพันกัน ฯลฯ)</summary>
        private void TrySpawnPrefab(float here)
        {
            if (prefabs == null || prefabs.Length == 0 || _prefabSpawnCount >= maxPrefabSpawns) return;

            var thread = walker.Thread;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float gap = Random.Range(minAheadDistance, maxAheadDistance);
                float d   = thread.WrapDistance(here + gap);
                Vector3 pos = thread.GetGroundPoint(d);

                if (!IsOutOfSight(pos)) continue;
                if (IsTooCloseToExisting(d)) continue;

                var prefab = prefabs[Random.Range(0, prefabs.Length)];
                if (prefab == null) continue;

                var inst = Instantiate(prefab, pos, Quaternion.LookRotation(thread.GetForward(d), Vector3.up));
                _instances.Add(inst);

                if (!inst.Bind(thread))
                {
                    Destroy(inst.gameObject);
                    _instances.Remove(inst);
                    continue;
                }

                inst.Hide();
                _prefabSpawnCount++;
                Spawn(inst, $"สุ่มเกิดข้างหน้า {gap:0} ม.");
                return;
            }
        }

        /// <summary>กันสิ่งกีดขวาง 2 ตัวโผล่ทับ/ชิดกันเกินไปบนเส้น</summary>
        private bool IsTooCloseToExisting(float d)
        {
            var thread = walker.Thread;

            foreach (var o in _spawned)
            {
                if (o == null || o.Cleared) continue;
                if (Mathf.Abs(thread.ForwardGap(o.BlockCenter, d)) < minAheadDistance * 0.5f) return true;
            }
            foreach (var o in _pending)
            {
                if (o == null) continue;
                if (Mathf.Abs(thread.ForwardGap(o.BlockCenter, d)) < minAheadDistance * 0.5f) return true;
            }
            return false;
        }

        private void Spawn(ThreadObstacle obstacle, string why)
        {
            _pending.Remove(obstacle);
            _spawned.Add(obstacle);

            obstacle.Reveal();
            walker.RegisterObstacle(obstacle);

            _nextSpawnAtWalked = _walkedTotal + Random.Range(minWalkBetweenSpawns, maxWalkBetweenSpawns);

            if (logSpawns)
                Debug.Log($"[Rule5] สิ่งกีดขวาง '{obstacle.name}' ({obstacle.Kind}) โผล่ — {why} " +
                          $"| เดินมาแล้ว {_walkedTotal:0} ม. | ตัวถัดไปที่ {_nextSpawnAtWalked:0} ม.", obstacle);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Sight test
        // ═══════════════════════════════════════════════════════════════

        /// <summary>true = ตำแหน่งนี้ผู้เล่นมองไม่เห็นตอนนี้ (นอกกรวยสายตา / มีของบัง / ไกลเกินไป)</summary>
        private bool IsOutOfSight(Vector3 groundPos)
        {
            if (!requireOutOfSight) return true;

            var cam = Camera.main;
            if (cam == null)
            {
                var pc = PlayerController.Instance;
                if (pc == null) return true;
                cam = pc.GetComponentInChildren<Camera>();
                if (cam == null) return true;
            }

            Vector3 target = groundPos + Vector3.up * sightCheckHeight;
            Vector3 eye    = cam.transform.position;
            Vector3 toTgt  = target - eye;
            float   dist   = toTgt.magnitude;
            if (dist < 0.01f) return false;

            // ไกลจนมองไม่ชัด — ยอมให้โผล่แม้อยู่ตรงหน้า (กันสายตรงยาวที่ไม่มีอะไรบังเลยแล้วไม่ spawn ได้)
            if (farEnoughDistance > 0f && dist >= farEnoughDistance) return true;

            // นอกกรวยสายตา (รวมกรณีอยู่ข้างหลัง)
            if (Vector3.Angle(cam.transform.forward, toTgt) > sightHalfAngle) return true;

            // อยู่ในสายตา — มีอะไรบังไหม
            return Physics.Raycast(eye, toTgt.normalized, dist - 0.3f, occluderMask, QueryTriggerInteraction.Ignore);
        }

        #endregion
    }
}
