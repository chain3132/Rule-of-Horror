using System.Collections;
using System.Collections.Generic;
using RuleSystem;
using UnityEngine;
using UnityEngine.Animations;
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
    /// ทำแอนิเมชันได้ 2 แบบ (เลือกอย่างใดอย่างหนึ่ง):
    ///   • Shots (ง่าย ไม่ต้องใช้ Timeline) — ไล่เป็นช็อตใน Inspector: ช็อตละ จุดกล้อง + ตัวละครเล่นคลิปไหน
    ///     เดินจากจุดไหนไปจุดไหน + ซับ   (เมนู Create Rule Flashbacks สร้างให้ครบทั้ง 5 กฎ)
    ///   • Timeline บน PlayableDirector ตัวนี้ — มี Timeline จะใช้ Timeline แล้วไม่สน Shots
    /// ไม่มีทั้งคู่ = Stage เปิดค้างไว้ตาม Duration (ใช้กับฉากนิ่ง/Animator ที่เล่นเอง)
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

        [Header("ช็อต (ไม่ต้องใช้ Timeline)")]
        [Tooltip("เล่นเรียงจากบนลงล่าง ความยาวฉาก = ผลรวม Duration ทุกช็อต\n" +
                 "มี Timeline บน PlayableDirector อยู่ = ใช้ Timeline แทน ช็อตจะไม่ถูกเล่น")]
        [SerializeField] private FlashbackShot[] shots;

        [Header("ซับ")]
        [Tooltip("บทพูดที่ขึ้นตามเวลา (วินาทีนับจากเริ่มฉาก) — แปลผ่าน L10n เหมือนบทพูดอื่น\n" +
                 "ใช้ Shots อยู่ ใส่ซับในแต่ละช็อตจะง่ายกว่า (นับเวลาจากต้นช็อต)")]
        [SerializeField] private FlashbackLine[] lines;

        [Header("เสียง")]
        [Tooltip("FMOD event ที่ดังตลอดฉาก เช่น เสียงเครื่องฉายฟิล์ม / บรรยากาศในอดีต — เว้นว่าง = ไม่มี")]
        [SerializeField] private string fmodEvent;

        [Header("แสงระหว่าง flashback")]
        [Tooltip("✓ = เปลี่ยนแสงทั้งโลกชั่วคราวระหว่างฉาก แล้วคืนค่าเดิมตอนจบ\n" +
                 "ฉากอยู่ในโลกเดียวกับเกมซึ่งเป็นกลางคืน (ambient 0.2 + หมอกหนา) ไม่เปลี่ยนจะมืดเกือบดำ\n" +
                 "ปลอดภัย: ระหว่างนั้นผู้เล่นหลับตาอยู่ (จอดำ) ไม่เห็นโลกจริงสว่างขึ้น")]
        [SerializeField] private bool overrideLighting = true;

        [Tooltip("แสงรอบตัว (ambient แบบสีเดียว) — กลางวัน/ความทรงจำ ใช้สีอุ่นสว่างๆ")]
        [ColorUsage(false, true)]
        [SerializeField] private Color ambientColor = new Color(0.55f, 0.52f, 0.46f);

        [Tooltip("✓ = ปิดหมอกของเกมระหว่างฉาก (หมอกกลางคืนกลืนทุกอย่างเกินไม่กี่เมตร)")]
        [SerializeField] private bool disableFog = true;

        [Tooltip("ความแรงของแสงอาทิตย์/จันทร์ (RenderSettings.sun) ระหว่างฉาก — ติดลบ = ไม่แตะ")]
        [SerializeField] private float sunIntensity = 1.2f;

        [SerializeField] private Color sunColor = new Color(1f, 0.93f, 0.8f);

        // ค่าที่จำไว้คืนตอนจบ
        private bool                                     _lightingSaved;
        private UnityEngine.Rendering.AmbientMode        _savedAmbientMode;
        private Color                                    _savedAmbientLight, _savedSky, _savedEquator, _savedGround;
        private float                                    _savedAmbientIntensity;
        private bool                                     _savedFog;
        private Light                                    _savedSun;
        private float                                    _savedSunIntensity;
        private Color                                    _savedSunColor;

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

        /// <summary>ช็อตหนึ่งของฉาก — กล้องอยู่ไหน ใครทำท่าอะไร พูดอะไร</summary>
        [System.Serializable]
        public class FlashbackShot
        {
            [Tooltip("ชื่อไว้ดูเองใน Inspector — ไม่ขึ้นในเกม")]
            public string label;

            [Min(0.1f)] public float duration = 5f;

            [Tooltip("กล้องกระโดดมาที่จุดนี้ตอนเริ่มช็อต (ตำแหน่ง + มุม) — เว้นว่าง = กล้องอยู่ที่เดิม")]
            public Transform cameraFrom;

            [Tooltip("กล้องค่อยๆ เลื่อนไปจุดนี้จนจบช็อต — เว้นว่าง = กล้องนิ่ง")]
            public Transform cameraTo;

            [Tooltip("ตัวละครในช็อตนี้ — ตัวที่ไม่ได้ใส่ ทำท่าเดิมต่ออยู่ที่เดิม")]
            public ShotActor[] actors;

            [Tooltip("ซับในช็อตนี้ — At = วินาทีนับจากต้นช็อต")]
            public FlashbackLine[] lines;
        }

        /// <summary>ตัวละครหนึ่งตัวในช็อต</summary>
        [System.Serializable]
        public class ShotActor
        {
            [Tooltip("ตัวละคร (ต้องอยู่ใน Stage) — ใช้ Animator ของมันเล่นคลิป ไม่ต้องมี Controller")]
            public Animator actor;

            [Tooltip("✓ = ซ่อนตัวนี้ตลอดช็อต (ช่องอื่นไม่ใช้)")]
            public bool hide;

            [Tooltip("ท่าที่เล่น — เว้นว่าง = ท่าเดิมจากช็อตก่อน")]
            public AnimationClip clip;

            [Tooltip("✓ = วนคลิปซ้ำจนจบช็อต (เดิน/วิ่ง) — ✗ = เล่นรอบเดียวแล้วค้างท่าสุดท้าย")]
            public bool loop = true;

            [Tooltip("ความเร็วคลิป — 0 = ค้างท่าเฟรมแรก (ใช้ทำท่านิ่ง/โพส)")]
            public float speed = 1f;

            [Tooltip("ตัวละครไปยืนที่จุดนี้ตอนเริ่มช็อต (ตำแหน่ง + มุม) — เว้นว่าง = อยู่ที่เดิม")]
            public Transform from;

            [Tooltip("ค่อยๆ เคลื่อนไปจุดนี้จนจบช็อต (เดิน/วิ่ง) และหันหน้าไปทางที่เดิน — เว้นว่าง = ยืนที่เดิม")]
            public Transform to;
        }

        public FlashbackTrigger Trigger => trigger;
        public RuleBase         Rule    => rule;
        public bool             PlayOnce => playOnce;
        public string           FmodEvent => fmodEvent;

        private bool UsesTimeline => director != null && director.playableAsset != null;
        private bool UsesShots    => !UsesTimeline && shots != null && shots.Length > 0;

        /// <summary>ซับทั้งฉาก เรียงตามเวลา — รวมซับของทุกช็อต (เลื่อนเวลาตามจุดเริ่มช็อต)</summary>
        public FlashbackLine[] Lines
        {
            get
            {
                var all = new List<FlashbackLine>();
                if (lines != null) all.AddRange(lines);

                if (UsesShots)
                {
                    float start = 0f;
                    foreach (var shot in shots)
                    {
                        if (shot == null) continue;
                        if (shot.lines != null)
                            foreach (var l in shot.lines)
                                if (l != null)
                                    all.Add(new FlashbackLine
                                    {
                                        at = start + l.at, text = l.text,
                                        holdDuration = l.holdDuration, ghostVoice = l.ghostVoice,
                                    });
                        start += shot.duration;
                    }
                }

                all.RemoveAll(l => l == null);
                all.Sort((a, b) => a.at.CompareTo(b.at));
                return all.ToArray();
            }
        }

        /// <summary>ความยาวจริงของฉาก</summary>
        public float Length
        {
            get
            {
                if (UsesTimeline) return (float)director.duration;
                if (!UsesShots)   return duration;

                float total = 0f;
                foreach (var shot in shots) if (shot != null) total += shot.duration;
                return Mathf.Max(0.5f, total);
            }
        }

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
            ApplyLighting();

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

            if (UsesTimeline)
            {
                director.timeUpdateMode = DirectorUpdateMode.GameTime;
                director.time = 0;
                director.Evaluate();   // วางท่าเฟรมแรกก่อนกล้องเรนเดอร์ภาพแรก
                director.Play();
            }
            else if (UsesShots)
            {
                if (cam != null)
                {
                    _savedCamPos = cam.transform.position;
                    _savedCamRot = cam.transform.rotation;
                }
                // ช็อตแรกวางตัวละคร/กล้องเสร็จก่อน return (coroutine รันถึง yield แรกทันที)
                // กล้องจึงไม่เรนเดอร์ภาพแรกเป็นท่า T-pose
                _shotRoutine = StartCoroutine(ShotsRoutine());
            }
        }

        /// <summary>หยุดและปิดฉาก</summary>
        public void End()
        {
            if (UsesTimeline) director.Stop();
            StopShots();

            var cam = FilmCamera;
            if (cam != null)
            {
                cam.enabled       = false;
                cam.targetTexture = null;
            }

            if (Stage != null) Stage.SetActive(false);
            RestoreLighting();
        }

        // ─────────────────────────── แสง ───────────────────────────

        private void ApplyLighting()
        {
            if (!overrideLighting || _lightingSaved) return;
            _lightingSaved = true;

            _savedAmbientMode      = RenderSettings.ambientMode;
            _savedAmbientLight     = RenderSettings.ambientLight;
            _savedSky              = RenderSettings.ambientSkyColor;
            _savedEquator          = RenderSettings.ambientEquatorColor;
            _savedGround           = RenderSettings.ambientGroundColor;
            _savedAmbientIntensity = RenderSettings.ambientIntensity;
            _savedFog              = RenderSettings.fog;

            RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            if (disableFog) RenderSettings.fog = false;

            _savedSun = RenderSettings.sun;
            if (_savedSun != null && sunIntensity >= 0f)
            {
                _savedSunIntensity  = _savedSun.intensity;
                _savedSunColor      = _savedSun.color;
                _savedSun.intensity = sunIntensity;
                _savedSun.color     = sunColor;
            }
        }

        private void RestoreLighting()
        {
            if (!_lightingSaved) return;
            _lightingSaved = false;

            RenderSettings.ambientMode         = _savedAmbientMode;
            RenderSettings.ambientLight        = _savedAmbientLight;
            RenderSettings.ambientSkyColor     = _savedSky;
            RenderSettings.ambientEquatorColor = _savedEquator;
            RenderSettings.ambientGroundColor  = _savedGround;
            RenderSettings.ambientIntensity    = _savedAmbientIntensity;
            RenderSettings.fog                 = _savedFog;

            if (_savedSun != null && sunIntensity >= 0f)
            {
                _savedSun.intensity = _savedSunIntensity;
                _savedSun.color     = _savedSunColor;
            }
            _savedSun = null;
        }

        private void OnDisable()
        {
            StopShots();
            RestoreLighting();   // ถูกปิด/ทำลายกลางฉาก — อย่าทิ้งโลกไว้สว่าง
        }

#if UNITY_EDITOR
        /// <summary>ใช้โดยเมนู Create Rule Flashbacks เท่านั้น — ตั้งค่าฉากที่เพิ่งสร้าง</summary>
        public void EditorSetup(FlashbackTrigger newTrigger, RuleBase newRule, FlashbackShot[] newShots)
        {
            trigger = newTrigger;
            rule    = newRule;
            shots   = newShots;
        }

        /// <summary>ใช้โดยเมนู Update Clips — แก้คลิปในช็อตที่สร้างไปแล้วโดยไม่ต้องสร้างฉากใหม่</summary>
        public FlashbackShot[] EditorShots => shots;
#endif

        // ─────────────────────────── ช็อต ───────────────────────────

        private class ActorState
        {
            public PlayableGraph         graph;
            public AnimationClipPlayable playable;
            public bool                  loop;
            public float                 clipLength;
            public Vector3               moveStart;   // จุดเริ่มเดินของช็อตปัจจุบัน

            // ค่าเดิมก่อนเล่น — คืนตอนจบ เทสซ้ำจะได้เริ่มที่เดิม
            public Vector3    savedPos;
            public Quaternion savedRot;
            public bool       savedActive;
            public bool       savedRootMotion;
        }

        private readonly Dictionary<Animator, ActorState> _actors = new Dictionary<Animator, ActorState>();
        private Coroutine  _shotRoutine;
        private Vector3    _savedCamPos, _camMoveStart;
        private Quaternion _savedCamRot, _camRotStart;

        private IEnumerator ShotsRoutine()
        {
            foreach (var shot in shots)
            {
                if (shot == null) continue;

                StartShot(shot);
                ApplyShot(shot, 0f);

                for (float t = 0f; t < shot.duration; )
                {
                    yield return null;
                    t += Time.deltaTime;   // หยุดตามเมนู Esc (timeScale 0) เหมือน FlashbackPlayer
                    ApplyShot(shot, Mathf.Clamp01(t / shot.duration));
                }
            }
        }

        private void StartShot(FlashbackShot shot)
        {
            var cam = FilmCamera;
            if (cam != null)
            {
                if (shot.cameraFrom != null)
                    cam.transform.SetPositionAndRotation(shot.cameraFrom.position, shot.cameraFrom.rotation);
                _camMoveStart = cam.transform.position;
                _camRotStart  = cam.transform.rotation;
            }

            if (shot.actors == null) return;
            foreach (var entry in shot.actors)
            {
                if (entry == null || entry.actor == null) continue;
                var state = GetState(entry.actor);
                var go    = entry.actor.gameObject;

                if (entry.hide)
                {
                    go.SetActive(false);
                    continue;
                }
                if (!go.activeSelf) go.SetActive(true);

                if (entry.from != null)
                    entry.actor.transform.SetPositionAndRotation(entry.from.position, entry.from.rotation);
                state.moveStart = entry.actor.transform.position;

                if (entry.clip != null) PlayClip(entry.actor, state, entry.clip);
                state.loop = entry.loop;
                if (state.playable.IsValid()) state.playable.SetSpeed(entry.speed);
            }
        }

        private void ApplyShot(FlashbackShot shot, float k)
        {
            var cam = FilmCamera;
            if (cam != null && shot.cameraTo != null)
            {
                float s = Mathf.SmoothStep(0f, 1f, k);
                cam.transform.SetPositionAndRotation(
                    Vector3.Lerp(_camMoveStart, shot.cameraTo.position, s),
                    Quaternion.Slerp(_camRotStart, shot.cameraTo.rotation, s));
            }

            if (shot.actors != null)
            {
                foreach (var entry in shot.actors)
                {
                    if (entry == null || entry.actor == null || entry.hide || entry.to == null) continue;
                    if (!_actors.TryGetValue(entry.actor, out var state)) continue;

                    Vector3 target = entry.to.position;
                    Vector3 dir    = target - state.moveStart;
                    dir.y = 0f;

                    var t = entry.actor.transform;
                    t.position = Vector3.Lerp(state.moveStart, target, k);
                    t.rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : entry.to.rotation;
                }
            }

            // คลิปในโมเดลหลายตัวตั้งไว้ไม่วน (Loop Time ปิด) — วนเองตรงนี้แทนการไปแก้ import ทุกคลิป
            foreach (var state in _actors.Values)
            {
                if (!state.loop || state.clipLength <= 0f || !state.playable.IsValid()) continue;
                double time = state.playable.GetTime();
                if (time >= state.clipLength) state.playable.SetTime(time % state.clipLength);
            }
        }

        private ActorState GetState(Animator actor)
        {
            if (_actors.TryGetValue(actor, out var state)) return state;

            state = new ActorState
            {
                savedPos        = actor.transform.position,
                savedRot        = actor.transform.rotation,
                savedActive     = actor.gameObject.activeSelf,
                savedRootMotion = actor.applyRootMotion,
            };
            _actors[actor] = state;
            return state;
        }

        /// <summary>
        /// เล่นคลิปตรงๆ ผ่าน PlayableGraph — ไม่ต้องมี Animator Controller ไม่ต้องทำ state ใดๆ
        /// ปิด Foot IK เสมอ: เปิดไว้แป้ง (Humanoid) จะย่อเข่าเดิน — เคสเดียวกับที่เจอใน Timeline
        /// ปิด Root Motion: ตำแหน่งตัวละครคุมด้วยจุด From/To ของช็อต ไม่ให้คลิปลากตัวไปเอง
        /// </summary>
        private static void PlayClip(Animator actor, ActorState state, AnimationClip clip)
        {
            if (state.graph.IsValid()) state.graph.Destroy();

            actor.applyRootMotion = false;

            var graph = PlayableGraph.Create($"Flashback {actor.name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            var output   = AnimationPlayableOutput.Create(graph, "Animation", actor);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            output.SetSourcePlayable(playable);

            graph.Play();
            graph.Evaluate(0f);   // ท่าแรกลงทันที ก่อนกล้องเรนเดอร์

            state.graph      = graph;
            state.playable   = playable;
            state.clipLength = clip.length;
        }

        private void StopShots()
        {
            if (_shotRoutine != null)
            {
                StopCoroutine(_shotRoutine);
                _shotRoutine = null;

                var cam = FilmCamera;
                if (cam != null) cam.transform.SetPositionAndRotation(_savedCamPos, _savedCamRot);
            }

            foreach (var pair in _actors)
            {
                var state = pair.Value;
                if (state.graph.IsValid()) state.graph.Destroy();
                if (pair.Key == null) continue;

                pair.Key.applyRootMotion = state.savedRootMotion;
                pair.Key.transform.SetPositionAndRotation(state.savedPos, state.savedRot);
                pair.Key.gameObject.SetActive(state.savedActive);
            }
            _actors.Clear();
        }
    }
}
