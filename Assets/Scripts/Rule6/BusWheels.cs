using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rule6
{
    /// <summary>
    /// ล้อรถหมุนตอนรถวิ่ง หยุดตอนรถจอด — เล่นคลิป Armature|BusRunning ที่มากับ FBX ของรถ (Models/Wk10/Bus)
    ///
    /// ใส่ไว้ที่ "รากของโมเดลรถ" (ตัวที่มีลูกชื่อ Armature) ไม่ใช่ที่ Rule6Bus — path ในคลิปเริ่มจาก
    /// Armature/... จึงต้องเล่นจากตัวที่อยู่เหนือ Armature ชั้นเดียว
    /// ใส่ให้ครบทุกคันด้วยเมนู Rule of Horror ▸ Rule 6 ▸ Setup Bus Wheel Animation
    ///
    /// วัดความเร็วจากตำแหน่งของตัวเองทุกเฟรม ไม่ได้ให้คนขับรถมาบอก — เหตุผลเดียวกับเสียงเครื่องใน
    /// Rule6Bus: รถถูกขยับจากหลายที่ (คิวรถ Rule6, ฉากเปิด, บทจบ) วัดที่นี่ที่เดียวถูกทุกที่
    ///
    /// ความเร็วล้อแปรตามความเร็วรถ (Match Speed) — รถค่อยๆ ชะลอเข้าป้าย ล้อก็ค่อยๆ ช้าลงจนหยุด
    /// ถ้าล้อหมุนเร็วคงที่แล้วหยุดกึกตอนจอด จะเห็นเป็นล้อลื่นไถลบนถนน
    ///
    /// เล่นด้วย PlayableGraph แบบ Manual แล้วเดินเวลาเองใน LateUpdate (หลัง coroutine ที่ขับรถ
    /// ในเฟรมเดียวกัน) — ไม่ต้องมี Animator Controller, คลิปไม่ได้ติ๊ก Loop Time ก็วนได้,
    /// และตอนหยุดเกม (Esc / timeScale 0) ล้อก็หยุดตาม
    /// </summary>
    [DisallowMultipleComponent]
    public class BusWheels : MonoBehaviour
    {
        [Tooltip("คลิปล้อหมุน — Armature|BusRunning ใน FBX ของรถคันนี้ (หรือ Armature_BusRunning.anim)")]
        [SerializeField] private AnimationClip runningClip;

        [Tooltip("Animator ที่รากของโมเดล — เว้นว่าง = ใช้/ใส่ที่ตัวนี้เอง")]
        [SerializeField] private Animator animator;

        [Header("วิ่ง / จอด")]
        [Tooltip("เร็วกว่านี้ (เมตร/วินาที) = นับว่ารถวิ่ง ล้อหมุน — ช้ากว่านี้ล้อหยุด")]
        [SerializeField] private float movingSpeed = 0.3f;

        [Tooltip("✓ = ล้อหมุนเร็วตามความเร็วรถ (ชะลอเข้าป้ายล้อก็ช้าลง)\n✗ = หมุนความเร็วคงที่ตอนวิ่ง หยุดทันทีตอนจอด")]
        [SerializeField] private bool matchSpeed = true;

        [Tooltip("รถวิ่งเร็วเท่านี้ (เมตร/วินาที) = คลิปเล่นความเร็วปกติ 1 เท่า\n" +
                 "ล้อดูหมุนช้ากว่ารถวิ่ง = ลดค่านี้ / ล้อหมุนเร็วเกิน = เพิ่มค่านี้\n" +
                 "ดูความเร็วจริงของรถได้ที่ช่อง Measured Speed ตอนกด Play")]
        [SerializeField] private float fullSpeed = 8f;

        [Tooltip("ความเร็วคลิปตอน Match Speed ปิด (1 = ปกติ)")]
        [SerializeField] private float playbackSpeed = 1f;

        [Tooltip("ความเร็วคลิปสูงสุด (กันล้อหมุนเป็นใบพัดตอนรถเร็วมาก)")]
        [SerializeField] private float maxPlaybackSpeed = 3f;

        [Tooltip("ล้อเร่ง/ชะลอได้เร็วแค่ไหน (เท่าของความเร็วคลิป ต่อวินาที) — ค่าน้อย = ออกตัว/หยุดนุ่มขึ้น")]
        [SerializeField] private float spinChangeRate = 3f;

        [Tooltip("ขยับเร็วกว่านี้ในเฟรมเดียว (เมตร/วินาที) = ถูกย้ายตำแหน่ง ไม่ใช่วิ่ง — ไม่นับเฟรมนั้น\n" +
                 "(ตอนจอดำย้ายรถไปจุดใหม่ ล้อไม่ควรหมุนพรืดหนึ่งเฟรม)")]
        [SerializeField] private float teleportSpeed = 60f;

        [Header("แสดงอย่างเดียว (ตอน Play)")]
        [Tooltip("ความเร็วรถที่วัดได้ตอนนี้ (เมตร/วินาที) — ใช้ตั้ง Full Speed")]
        [SerializeField] private float measuredSpeed;

        private PlayableGraph         _graph;
        private AnimationClipPlayable _playable;
        private double                _phase;       // เวลาในคลิป (วินาที) — เก็บข้ามการเปิด/ปิดรถ
        private float                 _rate;        // ความเร็วคลิปตอนนี้ (หลังค่อยๆ ปรับ)
        private Vector3               _lastPos;
        private bool                  _hasLastPos;
        private bool                  _needsPose;

        /// <summary>ล้อกำลังหมุนอยู่ไหม</summary>
        public bool IsSpinning => _rate > 0.001f;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (runningClip == null)
            {
                Debug.LogWarning($"[BusWheels] '{name}' ยังไม่ได้ใส่ Running Clip — ล้อจะไม่หมุน " +
                                 "(ใช้เมนู Rule of Horror ▸ Rule 6 ▸ Setup Bus Wheel Animation)", this);
                return;
            }

            if (animator == null && !TryGetComponent(out animator))
                animator = gameObject.AddComponent<Animator>();   // โมเดลไม่มี Animator มา — ไม่ต้องมี Controller

            animator.applyRootMotion = false;

            _graph = PlayableGraph.Create($"{name} Wheels");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);  // เดินเวลาเองใน LateUpdate

            var output = AnimationPlayableOutput.Create(_graph, "Wheels", animator);
            _playable  = AnimationClipPlayable.Create(_graph, runningClip);
            _playable.SetApplyFootIK(false);
            _playable.SetSpeed(0);
            output.SetSourcePlayable(_playable);
            _graph.Play();

            // เพิ่งโผล่มา — ตำแหน่งเฟรมแรกมักเป็นจุดที่ถูกย้ายมา ไม่ใช่การวิ่ง เริ่มวัดเฟรมถัดไป
            _hasLastPos = false;
            _rate       = 0f;
            _needsPose  = true;
        }

        private void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
            measuredSpeed = 0f;
        }

        private void LateUpdate()
        {
            if (!_graph.IsValid()) return;

            float   dt  = Time.deltaTime;
            Vector3 pos = transform.position;

            if (dt > 0f)
            {
                if (_hasLastPos)
                {
                    float speed = (pos - _lastPos).magnitude / dt;
                    if (speed <= teleportSpeed) measuredSpeed = speed;   // ถูกย้ายตำแหน่ง = ใช้ค่าเดิมไปก่อน
                }
                _lastPos    = pos;
                _hasLastPos = true;

                float target = 0f;
                if (measuredSpeed > movingSpeed)
                    target = matchSpeed ? measuredSpeed / Mathf.Max(0.01f, fullSpeed) : playbackSpeed;
                target = Mathf.Clamp(target, 0f, maxPlaybackSpeed);

                _rate = Mathf.MoveTowards(_rate, target, spinChangeRate * dt);
            }

            // จอดอยู่ / เกมหยุด = ไม่ต้องคำนวณท่าใหม่ ล้อค้างมุมเดิม (เฟรมแรกต้องวางท่าครั้งหนึ่งก่อน)
            if (_rate <= 0f && !_needsPose) return;
            _needsPose = false;

            float length = runningClip.length;
            _phase += _rate * dt;
            if (length > 0f && _phase >= length) _phase %= length;   // คลิปไม่ได้ติ๊ก Loop Time — วนเอง

            _playable.SetTime(_phase);
            _graph.Evaluate(0f);
        }

        private void OnValidate()
        {
            movingSpeed      = Mathf.Max(0f, movingSpeed);
            fullSpeed        = Mathf.Max(0.01f, fullSpeed);
            playbackSpeed    = Mathf.Max(0f, playbackSpeed);
            maxPlaybackSpeed = Mathf.Max(0f, maxPlaybackSpeed);
            spinChangeRate   = Mathf.Max(0.01f, spinChangeRate);
        }
    }
}
