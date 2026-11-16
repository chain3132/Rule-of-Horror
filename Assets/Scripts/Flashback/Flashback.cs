using RuleSystem;
using UnityEngine;
using UnityEngine.Playables;

namespace Flashbacks
{
    /// <summary>เล่น flashback ตอนไหน</summary>
    public enum FlashbackTrigger
    {
        [Tooltip("ไม่เล่นเอง — สคริปต์อื่นเรียก FlashbackPlayer.Queue(...) ก่อน blink")]
        Manual,

        [Tooltip("ตอนกะพริบเข้า Tension ของกฎข้อนั้น (อ่านแชทจบ + นั่งแล้ว)")]
        BeforeRuleStarts,

        [Tooltip("ตอนกะพริบกลับ Relax หลังผ่านกฎข้อนั้น (ตายไม่นับ)")]
        AfterRuleCompleted,
    }

    /// <summary>
    /// ฉาก flashback หนึ่งฉาก — เล่นในช่วงตาปิดสนิทตอนเปลี่ยนโหมด ภาพเป็นฟิล์มเก่า
    ///
    /// วิธีจัดฉาก (เมนู Rule of Horror ▸ Flashback ▸ Create Flashback Template สร้างโครงให้):
    ///   Flashback - ชื่อฉาก        ← ตัวนี้ (เปิดอยู่ตลอด) + PlayableDirector
    ///     └ Stage                ← ปิดไว้ — เปิดเองเฉพาะตอนเล่น
    ///         ├ Flashback Camera ← กล้องของฉาก (ปิดไว้ ห้ามติด tag MainCamera ห้ามมี AudioListener)
    ///         └ ตัวละคร / ฉาก / ไฟ ของ flashback
    ///
    /// ทำแอนิเมชันด้วย Timeline บน PlayableDirector ตัวนี้ (Animation Track ของตัวละครและกล้อง)
    /// ไม่ใส่ Timeline = Stage เปิดค้างไว้ตาม Duration (ใช้กับฉากนิ่ง/Animator ที่เล่นเอง)
    ///
    /// ระวัง: อย่าใช้ CinemachineCamera ในฉาก — Brain ของกล้องผู้เล่นจะไปจับมันแล้วกล้องเกมกระโดด
    /// ขยับ Flashback Camera ตรงๆ ด้วย Animation Track แทน
    /// </summary>
    [DisallowMultipleComponent]
    public class Flashback : MonoBehaviour
    {
        [Header("เล่นตอนไหน")]
        [SerializeField] private FlashbackTrigger trigger = FlashbackTrigger.BeforeRuleStarts;

        [Tooltip("กฎที่ผูกกับ flashback นี้ (ไม่ใช้กับ Manual)")]
        [SerializeField] private RuleBase rule;

        [Tooltip("✓ = เล่นครั้งเดียวต่อการเล่นหนึ่งรอบ — ตายแล้วเริ่มกฎใหม่ / คืนวนลูป ไม่เล่นซ้ำ")]
        [SerializeField] private bool playOnce = true;

        [Header("ฉาก")]
        [Tooltip("ทุกอย่างของฉาก flashback — ปิดไว้ในซีน เปิดเฉพาะตอนเล่น (เว้นว่าง = ลูกชื่อ \"Stage\")")]
        [SerializeField] private GameObject stage;

        [Tooltip("กล้องของฉาก — เว้นว่าง = หาใน Stage")]
        [SerializeField] private Camera filmCamera;

        [Tooltip("Timeline ของฉาก — เว้นว่าง = ตัวที่อยู่บน object นี้ (ไม่มีเลยก็ได้ ใช้ Duration แทน)")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("ความยาวฉาก (วินาที) ตอนไม่มี Timeline — มี Timeline จะใช้ความยาวของ Timeline")]
        [Min(0.5f)]
        [SerializeField] private float duration = 8f;

        [Header("ซับ")]
        [Tooltip("บทพูดที่ขึ้นตามเวลา (วินาทีนับจากเริ่มฉาก) — แปลผ่าน L10n เหมือนบทพูดอื่น")]
        [SerializeField] private FlashbackLine[] lines;

        [Header("เสียง")]
        [Tooltip("FMOD event ที่ดังตลอดฉาก เช่น เสียงเครื่องฉายฟิล์ม / บรรยากาศในอดีต — เว้นว่าง = ไม่มี")]
        [SerializeField] private string fmodEvent;

        [System.Serializable]
        public class FlashbackLine
        {
            [Min(0f)] public float at;
            [TextArea(1, 3)] public string text;
            [Tooltip("วินาทีที่ค้างหลังพิมพ์ครบ (-1 = คำนวณจากความยาว)")]
            public float holdDuration = -1f;
            [Tooltip("✓ = ตัวอักษรสีเสียงผี (แดง)")]
            public bool ghostVoice;
        }

        public FlashbackTrigger Trigger => trigger;
        public RuleBase         Rule    => rule;
        public bool             PlayOnce => playOnce;
        public FlashbackLine[]  Lines   => lines;
        public string           FmodEvent => fmodEvent;

        /// <summary>ความยาวจริงของฉาก</summary>
        public float Length =>
            director != null && director.playableAsset != null ? (float)director.duration : duration;

        public Camera FilmCamera
        {
            get
            {
                if (filmCamera == null && Stage != null) filmCamera = Stage.GetComponentInChildren<Camera>(true);
                return filmCamera;
            }
        }

        private GameObject Stage
        {
            get
            {
                if (stage == null)
                {
                    var child = transform.Find("Stage");
                    if (child != null) stage = child.gameObject;
                }
                return stage;
            }
        }

        private void Awake()
        {
            if (director == null) director = GetComponent<PlayableDirector>();
            if (director != null) director.playOnAwake = false;

            // ฉากต้องไม่โผล่ในเกม และกล้องต้องไม่ render เอง (FlashbackPlayer สั่ง render ทีละเฟรมฟิล์ม)
            if (Stage != null) Stage.SetActive(false);
            if (FilmCamera != null) FilmCamera.enabled = false;
        }

        /// <summary>
        /// เทสฉากนี้ตอนกด Play อยู่ — กะพริบตาไปโหมดเดิม แล้วเล่นในช่วงตาปิด (เล่นซ้ำได้ ไม่สน Play Once)
        /// คลิกขวาที่หัว component ▸ "Test Play (Play mode)"
        /// </summary>
        [ContextMenu("Test Play (Play mode)")]
        private void TestPlay()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Flashback] กด Play ก่อน แล้วค่อยเทส", this);
                return;
            }

            var mode = GameModeController.instance;
            if (mode == null || FlashbackPlayer.Instance == null)
            {
                Debug.LogWarning("[Flashback] ไม่มี GameModeController หรือ Flashback Screen ในซีน " +
                                 "(Rule of Horror ▸ Flashback ▸ Create Flashback Screen)", this);
                return;
            }

            FlashbackPlayer.Queue(this, ignorePlayOnce: true);
            mode.BlinkToMode(mode.CurrentMode);
        }

        /// <summary>เปิดฉากแล้วเริ่มเล่น — กล้องเรนเดอร์ลง target ที่ให้มา</summary>
        public void Begin(RenderTexture target)
        {
            if (Stage != null) Stage.SetActive(true);

            var cam = FilmCamera;
            if (cam != null)
            {
                cam.targetTexture = target;
                cam.enabled       = false;
            }
            else
            {
                Debug.LogWarning($"[Flashback] '{name}' ไม่มีกล้อง — ภาพจะว่าง ใส่ Camera ไว้ใน Stage", this);
            }

            if (director != null && director.playableAsset != null)
            {
                director.timeUpdateMode = DirectorUpdateMode.GameTime;
                director.time = 0;
                director.Evaluate();   // วางท่าเฟรมแรกก่อนกล้องเรนเดอร์ภาพแรก
                director.Play();
            }
        }

        /// <summary>หยุดและปิดฉาก</summary>
        public void End()
        {
            if (director != null && director.playableAsset != null) director.Stop();

            var cam = FilmCamera;
            if (cam != null)
            {
                cam.enabled       = false;
                cam.targetTexture = null;
            }

            if (Stage != null) Stage.SetActive(false);
        }
    }
}
