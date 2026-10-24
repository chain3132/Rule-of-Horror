using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using Player;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace Rule6
{
    /// <summary>
    /// คัทซีนบทจบของรถคันที่ 1-3 ที่เล่นในเกม — ไม่ใช่วิดีโอ ไม่ใช่ Timeline
    ///
    /// คัทซีนคือ "ลิสต์ขั้น" เรียงจากบนลงล่าง แต่ละขั้นทำอย่างเดียว (หันกล้อง / รถออก / ตัดกล้อง
    /// / จอดำ / เสียง / บทพูด / แชท ...) ตรงกับวิธีเขียน storyboard: shot หนึ่งคือขั้นไม่กี่ขั้น
    /// แก้จังหวะใน Inspector ได้เลยโดยไม่ต้องแตะโค้ด
    ///
    /// ทุกขั้นมีช่อง "รอให้จบ" — ✗ = สั่งแล้วไปขั้นถัดไปทันที ใช้ทำของที่เกิดพร้อมกัน เช่น
    /// รถเริ่มออก (✗) แล้วกล้องหันตามแป้งไปพร้อมกัน (✓)
    ///
    /// กฎ 6 เป็นคนเรียก Play() หลังผู้เล่นนั่งลงในรถแล้ว และเรียก FinishEnding ต่อเองตอนลิสต์จบ
    /// ตัวนี้จึงไม่ต้องรู้เรื่องบทจบเลย รู้แค่ว่าต้องเล่นอะไรบ้าง
    ///
    /// การหันกล้อง POV เขียน cameraPivot ตรงๆ ใน LateUpdate ไม่ผ่าน LookAtWorldPoint เพราะตัวนั้น
    /// จำกัดการหันตอนนั่งไว้ที่ ±60° ซึ่งหันไปมองนอกหน้าต่างข้างตัวไม่ถึง และต้องเป็น LateUpdate
    /// เพราะรถขยับตัวผู้เล่นทุกเฟรม ถ้าหันกล้องก่อนรถขยับ ภาพจะสั่นตามเฟรม
    /// </summary>
    public class EndingCutscene : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════
        #region ขั้นของคัทซีน
        // ════════════════════════════════════════════════════════════════

        public enum StepAction
        {
            [Tooltip("รอเฉยๆ")]
            Wait = 0,

            [Tooltip("หันกล้องผู้เล่น (POV) ไปมองเป้า — ไม่ใส่เป้า = หันกลับมองหน้ารถ")]
            PlayerLook,

            [Tooltip("รถออกจากป้ายพร้อมผู้เล่นที่นั่งอยู่")]
            BusDrive,

            [Tooltip("ตัดไปกล้องหนัง (CinemachineCamera) — ไม่ใส่กล้อง = กลับมาเป็นสายตาผู้เล่น")]
            CameraCut,

            [Tooltip("จอดำ / จอกลับมาสว่าง")]
            Fade,

            [Tooltip("เล่นเสียง FMOD ครั้งเดียว")]
            Sound,

            [Tooltip("ขึ้นบทพูด/ซับไตเติล")]
            Dialogue,

            [Tooltip("ยิง Trigger ของ Animator (เช่น แป้งร้องไห้)")]
            AnimatorTrigger,

            [Tooltip("เปิด/ปิด GameObject (เช่น หน้าจอโทรศัพท์)")]
            SetActive,

            [Tooltip("เล่นแชทในโทรศัพท์ตามบท (EndingChat)")]
            Chat,

            [Tooltip("ยกโทรศัพท์จริงขึ้นมาในมุมสายตา ฉากหลังดำ จอสว่าง / เก็บลง (EndingPhone)")]
            Phone,

            [Tooltip("ให้ตัวละคร (เช่น แป้ง) เดินไปจุดที่กำหนด แล้วหันตามทิศของจุดนั้น")]
            MoveTo,

            [Tooltip("ให้ object ไปเกาะกับอีกตัว (เช่น แป้งเกาะรถ จะได้ไปกับรถตอนรถออก)")]
            Parent,
        }

        public enum FadeDirection
        {
            ToBlack,
            FromBlack,
        }

        [Serializable]
        public class Step
        {
            [Tooltip("โน้ตไว้อ่านเอง เช่น \"SHOT 3 · มองแป้งผ่านหน้าต่าง\" — ไม่มีผลกับเกม")]
            public string note;

            public StepAction action;

            [Tooltip("✓ = รอขั้นนี้จบก่อนไปขั้นถัดไป\n✗ = สั่งแล้วไปต่อทันที (ทำพร้อมกับขั้นถัดไป)")]
            public bool waitToFinish = true;

            // ── เวลา ──

            [ShowIf(nameof(UsesSeconds))]
            [LabelText("@" + nameof(SecondsLabel))]
            [Tooltip("Wait: รอกี่วินาที\nPlayer Look: ใช้เวลาหันกี่วินาที\nBus Drive: รถวิ่งถึงปลายทางในกี่วินาที\n" +
                     "Camera Cut: เวลา blend (0 = ตัดทันที)\nFade: เวลาที่จอค่อยๆ ดำ/สว่าง (0 = ทันที)")]
            public float seconds = 1f;

            // ── Player Look ──

            [ShowIf(nameof(IsLook))]
            [Tooltip("สิ่งที่จะมอง — ใส่จุดที่หัว/หน้า ไม่ใช่เท้า\nเว้นว่าง = หันกลับมองตรงไปหน้ารถ")]
            public Transform lookTarget;

            [ShowIf(nameof(IsLook))]
            [Tooltip("✓ = หันเสร็จแล้วมองค้างไว้จนเป้า \"พ้นสายตา\" (หลุดเฟรม หรือมีอะไรบัง เช่น ขอบหน้าต่าง)\n" +
                     "ใช้ตอนรถเริ่มวิ่งแล้วแป้งค่อยๆ เลื่อนออกจากหน้าต่าง")]
            public bool holdUntilHidden;

            [ShowIf(nameof(HoldsUntilHidden))]
            [Tooltip("มองค้างได้นานสุดเท่านี้ แม้เป้ายังไม่พ้นสายตา (วินาที) — กันคัทซีนค้างถ้าเป้าไม่เคยหลุดเฟรม")]
            public float maxHoldSeconds = 8f;

            // ── Bus Drive ──

            [ShowIf(nameof(IsDrive))]
            [Tooltip("ปลายทางของรถ — เว้นว่าง = จุด Depart ของกฎ 6")]
            public Transform driveTo;

            // ── Camera Cut ──

            [ShowIf(nameof(IsCameraCut))]
            [Tooltip("กล้องของ shot นี้ — วางไว้ในซีนแล้วปิด GameObject ไว้ คัทซีนจะเปิดเอง\n" +
                     "เว้นว่าง = กลับมาเป็นสายตาผู้เล่น")]
            public CinemachineCamera vcam;

            // ── Fade ──

            [ShowIf(nameof(IsFade))]
            public FadeDirection fade = FadeDirection.ToBlack;

            // ── Sound ──

            [ShowIf(nameof(IsSound))]
            [Tooltip("path ของ event ใน FMOD เช่น event:/Rule6/DoorClose — เว้นว่าง = ข้ามขั้นนี้")]
            public string fmodEvent;

            [ShowIf(nameof(IsSound))]
            [Tooltip("ดังจากตรงไหน — เว้นว่าง = ดังที่ตัวผู้ฟัง (เสียงไม่มีทิศทาง)")]
            public Transform soundAt;

            // ── Dialogue ──

            [ShowIf(nameof(IsDialogue))]
            public DialogueLine[] lines;

            // ── Animator Trigger ──

            [ShowIf(nameof(IsAnimator))]
            public Animator animator;

            [ShowIf(nameof(IsAnimator))]
            public string trigger;

            // ── Set Active ──

            [ShowIf(nameof(IsSetActive))]
            public GameObject target;

            [ShowIf(nameof(IsSetActive))]
            [Tooltip("✓ = เปิด  ✗ = ปิด")]
            public bool active = true;

            // ── Chat ──

            [ShowIf(nameof(IsChat))]
            public EndingChat chat;

            // ── Phone ──

            [ShowIf(nameof(IsPhone))]
            public EndingPhone phone;

            [ShowIf(nameof(IsPhone))]
            [Tooltip("✓ = ยกขึ้นมา  ✗ = เก็บลงแล้วคืนกล้องกับระบบโทรศัพท์")]
            public bool show = true;

            // ── Move To / Parent ──

            [ShowIf(nameof(UsesMover))]
            [Tooltip("ตัวที่จะขยับ — ใส่ตัวแม่ของตัวละคร (เช่น object \"Paeng\") ไม่ใช่โมเดลข้างใน")]
            public Transform mover;

            [ShowIf(nameof(IsMoveTo))]
            [Tooltip("จุดปลายทาง — ทิศที่จุดนี้หัน (ลูกศรน้ำเงิน) คือทิศที่ตัวละครจะหันตอนถึง")]
            public Transform moveTo;

            [ShowIf(nameof(IsMoveTo))]
            [Tooltip("✓ = ถึงแล้วค่อยๆ หันตามทิศของจุดปลายทาง  ✗ = หันตามทางที่เดินมา")]
            public bool matchRotation = true;

            [ShowIf(nameof(IsMoveTo))]
            [Tooltip("Animator ของตัวที่เดิน — เว้นว่างได้ (ไหลไปเฉยๆ ซึ่งสำหรับผีก็ดูน่ากลัวดี)")]
            public Animator walkAnimator;

            [ShowIf(nameof(IsMoveTo))]
            [Tooltip("ชื่อ Bool ใน Animator ที่เปิดตอนเดิน ปิดตอนถึง — เว้นว่าง = ไม่ใช้")]
            public string walkBool;

            [ShowIf(nameof(IsParent))]
            [Tooltip("ตัวแม่ใหม่ — เว้นว่าง = หลุดออกมาอยู่ระดับบนสุดของซีน\n" +
                     "ตำแหน่งในโลกไม่เปลี่ยน แค่หลังจากนี้จะขยับตามตัวแม่")]
            public Transform parentTo;

            // ── ตัวช่วยของ Inspector ──

            private bool IsLook           => action == StepAction.PlayerLook;
            private bool HoldsUntilHidden => IsLook && holdUntilHidden;
            private bool IsDrive          => action == StepAction.BusDrive;
            private bool IsCameraCut      => action == StepAction.CameraCut;
            private bool IsFade           => action == StepAction.Fade;
            private bool IsSound          => action == StepAction.Sound;
            private bool IsDialogue       => action == StepAction.Dialogue;
            private bool IsAnimator       => action == StepAction.AnimatorTrigger;
            private bool IsSetActive      => action == StepAction.SetActive;
            private bool IsChat           => action == StepAction.Chat;
            private bool IsPhone          => action == StepAction.Phone;
            private bool IsMoveTo         => action == StepAction.MoveTo;
            private bool IsParent         => action == StepAction.Parent;
            private bool UsesMover        => IsMoveTo || IsParent;

            private bool UsesSeconds => action == StepAction.Wait || IsLook || IsDrive || IsCameraCut || IsFade || IsMoveTo;

            private string SecondsLabel
            {
                get
                {
                    switch (action)
                    {
                        case StepAction.PlayerLook: return "Turn Seconds";
                        case StepAction.BusDrive:   return "Drive Seconds";
                        case StepAction.CameraCut:  return "Blend Seconds";
                        case StepAction.Fade:       return "Fade Seconds";
                        case StepAction.MoveTo:     return "Walk Seconds";
                        default:                    return "Seconds";
                    }
                }
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Inspector
        // ════════════════════════════════════════════════════════════════

        [Header("ลำดับขั้น — เล่นจากบนลงล่าง")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [SerializeField] private List<Step> steps = new List<Step>();

        [Header("กล้อง")]
        [Tooltip("GameObject ที่มี Camera + CinemachineBrain — ใช้ตัวเดียวกับฉากเปิดได้ (Cutscene Camera)\n" +
                 "ต้องใส่ถ้ามีขั้น Camera Cut")]
        [SerializeField] private GameObject cinematicCamera;

        [Tooltip("การหันหัวได้สูงสุดซ้าย/ขวาจากทิศที่นั่งหัน (องศา) — คนหันคอได้ราวๆ 90-110°\n" +
                 "ตอนเล่นปกติจำกัดไว้ที่ 60° ซึ่งหันไปมองนอกหน้าต่างข้างตัวไม่ถึง")]
        [SerializeField] private float maxHeadYaw = 105f;

        [Tooltip("ก้ม/เงยได้สูงสุด (องศา)")]
        [SerializeField] private float maxHeadPitch = 60f;

        [Tooltip("ของที่นับว่า \"บังสายตา\" ตอนเช็กว่าเป้าพ้นสายตาแล้วหรือยัง (ขั้น Player Look + Hold Until Hidden)\n" +
                 "ปกติ Everything ได้ — ตัวผู้เล่นกับตัวเป้าถูกตัดออกให้แล้ว")]
        [SerializeField] private LayerMask occluders = ~0;

        [Header("จอดำ")]
        [Tooltip("CanvasGroup ของแผ่นดำเต็มจอ — เว้นว่าง = สร้างให้เองตอนเล่น")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Tooltip("ลำดับของแผ่นดำที่สร้างเอง — ต้องสูงกว่า HUD ของเกม")]
        [SerializeField] private int fadeSortOrder = 500;

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Runtime
        // ════════════════════════════════════════════════════════════════

        private Rule6Bus  _bus;
        private BusRide   _ride;
        private Transform _defaultDriveTo;
        private Camera    _playerCamera;
        private Coroutine _driveRoutine;
        private CinemachineCamera _currentVcam;
        private bool      _playing;

        // สถานะการหันกล้อง POV — ใช้ใน LateUpdate
        private bool       _looking;
        private Transform  _lookTarget;
        private Quaternion _lookFromLocal;
        private float      _lookTurnSeconds;
        private float      _lookElapsed;

        public bool IsPlaying => _playing;

#if UNITY_EDITOR
        /// <summary>ให้ตัวสร้างคัทซีนใน Editor เติมขั้นเริ่มต้นตาม storyboard ได้</summary>
        public List<Step> EditorSteps => steps;
        public GameObject EditorCinematicCamera { get => cinematicCamera; set => cinematicCamera = value; }
#endif

        /// <summary>
        /// เล่นคัทซีนทั้งลิสต์ — กฎ 6 yield return ตัวนี้แล้วค่อยจบบทจบต่อเอง
        ///
        /// ride ต้องเป็นตัวเดียวกับที่กฎ 6 ใช้ เพราะคัทซีนพาผู้เล่นไปกับรถด้วยตัวนั้น
        /// defaultDriveTo = จุด Depart ของกฎ 6 สำหรับขั้น Bus Drive ที่ไม่ได้ใส่ปลายทาง
        /// </summary>
        public IEnumerator Play(Rule6Bus bus, BusRide ride, Transform defaultDriveTo)
        {
            _bus            = bus;
            _ride           = ride;
            _defaultDriveTo = defaultDriveTo;
            _playing        = true;

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                // หันกล้องเองทั้งคัทซีน — ปล่อยเมาส์ไว้มันจะแย่ง cameraPivot กับเรา
                pc.SetLook(false);
                pc.SetMovement(false);
                _playerCamera = pc.CameraPivot != null ? pc.CameraPivot.GetComponentInChildren<Camera>(true) : null;
            }
            if (_playerCamera == null) _playerCamera = Camera.main;

            if (_ride != null && _bus != null && !_ride.IsCarrying) _ride.BeginCarry(_bus.transform);

            if (steps.Count == 0)
                Debug.LogWarning($"[EndingCutscene] '{name}' ยังไม่มีขั้นอะไรเลย — คัทซีนจะจบทันที", this);

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null) continue;

                IEnumerator run = RunStep(step, i);
                if (run == null) continue;

                if (step.waitToFinish) yield return run;
                else StartCoroutine(run);
            }

            _playing = false;
        }

        /// <summary>
        /// หยุดทุกอย่างที่ค้างอยู่ — เรียกตอนกฎ 6 ถูกปิดกลางคัน
        /// ไม่คืนกล้อง: คัทซีนจบที่ภาพสุดท้ายของมันโดยตั้งใจ (บทจบต่อจากตรงนั้น)
        /// </summary>
        public void Stop()
        {
            StopAllCoroutines();
            _driveRoutine = null;
            _looking      = false;
            _playing      = false;
        }

        private void OnDisable() => Stop();

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region แต่ละขั้น
        // ════════════════════════════════════════════════════════════════

        private IEnumerator RunStep(Step step, int index)
        {
            switch (step.action)
            {
                case StepAction.Wait:
                    return WaitSeconds(step.seconds);

                case StepAction.PlayerLook:
                    return Look(step);

                case StepAction.BusDrive:
                    return Drive(step);

                case StepAction.CameraCut:
                    CutTo(step.vcam, step.seconds);
                    return null;

                case StepAction.Fade:
                    return Fade(step.fade == FadeDirection.ToBlack ? 1f : 0f, step.seconds);

                case StepAction.Sound:
                    PlaySound(step.fmodEvent, step.soundAt);
                    return null;

                case StepAction.Dialogue:
                    return ShowLines(step.lines);

                case StepAction.AnimatorTrigger:
                    if (step.animator != null && !string.IsNullOrWhiteSpace(step.trigger))
                        step.animator.SetTrigger(step.trigger);
                    else
                        Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Animator หรือ Trigger", this);
                    return null;

                case StepAction.SetActive:
                    if (step.target != null) step.target.SetActive(step.active);
                    else Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Target", this);
                    return null;

                case StepAction.Chat:
                    if (step.chat != null) return step.chat.Play();
                    Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Chat", this);
                    return null;

                case StepAction.Phone:
                    if (step.phone == null)
                    {
                        Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Phone", this);
                        return null;
                    }
                    if (step.show) return step.phone.Show();
                    step.phone.Hide();
                    return null;

                case StepAction.MoveTo:
                    if (step.mover != null && step.moveTo != null) return MoveTo(step);
                    Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Mover หรือ Move To", this);
                    return null;

                case StepAction.Parent:
                    if (step.mover != null) step.mover.SetParent(step.parentTo, worldPositionStays: true);
                    else Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Mover", this);
                    return null;
            }

            return null;
        }

        // ─────────────────────────── Move To ───────────────────────────

        /// <summary>
        /// เดินเป็นเส้นตรงด้วยความเร็วคงที่ หันหน้าตามทางที่เดิน แล้วช่วงท้ายค่อยๆ หันตามทิศของ
        /// จุดปลายทาง — จุดปลายทางจึงเป็นทั้ง "ยืนตรงไหน" และ "หันไปทางไหน" ในตัวเดียว
        ///
        /// อ่านตำแหน่งปลายทางใหม่ทุกเฟรม ไม่จำไว้ตอนเริ่ม: จุดเป็นลูกของรถ ถ้ารถขยับระหว่างเดิน
        /// ตัวละครต้องเดินตามจุดที่ขยับไปด้วย ไม่ใช่เดินไปหาที่ว่างที่รถเคยจอด
        /// </summary>
        private IEnumerator MoveTo(Step step)
        {
            Transform t    = step.mover;
            Transform goal = step.moveTo;

            bool walking = step.walkAnimator != null && !string.IsNullOrWhiteSpace(step.walkBool);
            if (walking) step.walkAnimator.SetBool(step.walkBool, true);

            Vector3    from      = t.position;
            Quaternion startRot  = t.rotation;
            float      seconds   = Mathf.Max(0f, step.seconds);
            const float turnTail = 0.35f;   // วินาทีท้ายที่ใช้หันตามทิศของจุดปลายทาง

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / seconds);

                Vector3 next = Vector3.Lerp(from, goal.position, k);
                Vector3 flat = next - t.position;
                flat.y = 0f;

                Quaternion rot = t.rotation;
                if (flat.sqrMagnitude > 0.000001f)
                    rot = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(flat.normalized, Vector3.up),
                                                   360f * Time.deltaTime);

                float left = seconds - elapsed;
                if (step.matchRotation && left < turnTail)
                    rot = Quaternion.Slerp(rot, goal.rotation, 1f - left / turnTail);

                t.SetPositionAndRotation(next, rot);
                yield return null;
            }

            t.SetPositionAndRotation(goal.position, step.matchRotation ? goal.rotation : (seconds > 0f ? t.rotation : startRot));
            if (walking) step.walkAnimator.SetBool(step.walkBool, false);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
        }

        // ─────────────────────────── Player Look ───────────────────────────

        /// <summary>
        /// เริ่มหันไปมองเป้า แล้วรอตามที่ขั้นนั้นบอก
        ///
        /// การมองค้างอยู่หลังขั้นจบ (กล้องยังจ้องเป้าตามไปขณะรถวิ่ง) จนกว่าจะมีขั้น Player Look
        /// ขั้นใหม่มาแทน — "มองแป้ง" จึงเป็นขั้นเดียว ไม่ต้องสั่งมองซ้ำทุกเฟรม
        /// </summary>
        private IEnumerator Look(Step step)
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.CameraPivot == null) yield break;

            _looking         = true;
            _lookTarget      = step.lookTarget;
            _lookFromLocal   = pc.CameraPivot.localRotation;
            _lookTurnSeconds = Mathf.Max(0f, step.seconds);
            _lookElapsed     = 0f;

            Transform myTarget = step.lookTarget;

            if (_lookTurnSeconds > 0f) yield return new WaitForSeconds(_lookTurnSeconds);

            if (!step.holdUntilHidden || myTarget == null) yield break;

            float held   = 0f;
            float hidden = 0f;

            // ต้อง "พ้นสายตาต่อเนื่อง" สักนิด ไม่ใช่แค่เฟรมเดียว — เสาหน้าต่างบางๆ ที่วิ่งผ่าน
            // หน้าเป้าแวบเดียวไม่ควรนับว่าพ้นหน้าต่างไปแล้ว
            const float hiddenFor = 0.2f;

            while (held < step.maxHoldSeconds && _lookTarget == myTarget)
            {
                hidden = IsHidden(myTarget) ? hidden + Time.deltaTime : 0f;
                if (hidden >= hiddenFor) yield break;

                held += Time.deltaTime;
                yield return null;
            }
        }

        private void LateUpdate()
        {
            if (!_looking) return;

            var pc = PlayerController.Instance;
            if (pc == null || pc.CameraPivot == null) return;

            Transform pivot  = pc.CameraPivot;
            Transform parent = pivot.parent;

            Quaternion desiredLocal = Quaternion.identity;   // ไม่มีเป้า = มองตรงไปหน้ารถ

            if (_lookTarget != null)
            {
                Vector3 dir = _lookTarget.position - pivot.position;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion world = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    desiredLocal = parent != null ? Quaternion.Inverse(parent.rotation) * world : world;
                }
            }

            desiredLocal = ClampHead(desiredLocal);

            _lookElapsed += Time.deltaTime;
            float t = _lookTurnSeconds > 0f ? Mathf.SmoothStep(0f, 1f, _lookElapsed / _lookTurnSeconds) : 1f;

            // ต้นทางเก็บเป็น local (เทียบกับตัวผู้เล่น) — รถเลี้ยวระหว่างหัน ต้นทางต้องเลี้ยวตาม
            pivot.localRotation = Quaternion.Slerp(_lookFromLocal, desiredLocal, t);
        }

        /// <summary>
        /// จำกัดการหันหัวไว้ในช่วงที่คนหันได้จริง — เป้าที่เลยไปข้างหลังจะค้างอยู่ที่ขอบ
        /// แล้วเป้าก็จะเลื่อนหลุดเฟรมไปเอง ซึ่งคือภาพ "แป้งค่อยๆ เลื่อนออกจากหน้าต่าง"
        /// </summary>
        private Quaternion ClampHead(Quaternion local)
        {
            Vector3 e = local.eulerAngles;
            float yaw   = Mathf.Clamp(Mathf.DeltaAngle(0f, e.y), -maxHeadYaw,   maxHeadYaw);
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -maxHeadPitch, maxHeadPitch);
            return Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>เป้าหลุดจากเฟรมกล้องผู้เล่น หรือมีอะไรบังอยู่</summary>
        private bool IsHidden(Transform target)
        {
            Camera cam = _playerCamera != null ? _playerCamera : Camera.main;
            if (cam == null) return false;

            Vector3 vp = cam.WorldToViewportPoint(target.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return true;

            Vector3 from = cam.transform.position;
            Vector3 to   = target.position;
            var pc = PlayerController.Instance;

            foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to),
                                                   occluders, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(target.root)) continue;
                if (pc != null && hit.transform.IsChildOf(pc.transform)) continue;
                return true;
            }

            return false;
        }

        // ─────────────────────────── Bus Drive ───────────────────────────

        /// <summary>
        /// รถวิ่งจากตำแหน่งตอนนี้ไปปลายทาง พร้อมพาผู้เล่นไปด้วย — ออกตัวช้าแล้วค่อยเร่ง
        /// เหมือน RideAway ของกฎ 6 รถคันเดียวกันจะได้ออกตัวด้วยท่าเดียวกันไม่ว่าจะมีคัทซีนหรือไม่
        ///
        /// ขั้น Bus Drive ใหม่มาแทนขั้นเก่าที่ยังวิ่งอยู่ (รถวิ่งไปสองทางพร้อมกันไม่ได้)
        /// </summary>
        private IEnumerator Drive(Step step)
        {
            if (_bus == null) yield break;

            Transform to = step.driveTo != null ? step.driveTo : _defaultDriveTo;
            if (to == null)
            {
                Debug.LogWarning($"[EndingCutscene] ขั้น Bus Drive ({step.note}) ไม่มีปลายทาง — " +
                                 "ใส่ Drive To หรือใส่ Depart ของกฎ 6", this);
                yield break;
            }

            if (_driveRoutine != null) StopCoroutine(_driveRoutine);

            if (AudioManager.instance != null) AudioManager.instance.PlayRule6BusDepart(_bus.transform.position);

            var routine = DriveRoutine(_bus.transform, to, step.seconds);
            _driveRoutine = StartCoroutine(routine);
            yield return _driveRoutine;
        }

        private IEnumerator DriveRoutine(Transform bus, Transform to, float seconds)
        {
            Vector3    fromPos = bus.position;
            Quaternion fromRot = bus.rotation;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;

                float k = Mathf.Clamp01(elapsed / seconds);
                k *= k;

                bus.SetPositionAndRotation(Vector3.Lerp(fromPos, to.position, k),
                                           Quaternion.Slerp(fromRot, to.rotation, k));
                _ride?.Step();

                yield return null;
            }

            bus.SetPositionAndRotation(to.position, to.rotation);
            _ride?.Step();
            _driveRoutine = null;
        }

        // ─────────────────────────── Camera Cut ───────────────────────────

        /// <summary>
        /// ตัดไปกล้องหนัง หรือกลับมาสายตาผู้เล่น (vcam = null)
        ///
        /// เปิดกล้องหนังก่อนปิดกล้องผู้เล่น ไม่งั้นจะมีเฟรมที่ไม่มีกล้องไหนเปิดอยู่ = จอดำแวบ
        /// </summary>
        private void CutTo(CinemachineCamera vcam, float blendSeconds)
        {
            if (vcam == null)
            {
                if (_currentVcam != null) _currentVcam.gameObject.SetActive(false);
                _currentVcam = null;

                if (_playerCamera != null) _playerCamera.enabled = true;
                if (cinematicCamera != null) cinematicCamera.SetActive(false);
                return;
            }

            if (cinematicCamera == null)
            {
                Debug.LogError($"[EndingCutscene] '{name}' มีขั้น Camera Cut แต่ยังไม่ได้ใส่ Cinematic Camera " +
                               "(GameObject ที่มี Camera + CinemachineBrain) — ภาพจะยังเป็นสายตาผู้เล่น", this);
                return;
            }

            var brain = cinematicCamera.GetComponent<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = blendSeconds > 0f
                    ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendSeconds)
                    : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            if (_currentVcam != null && _currentVcam != vcam) _currentVcam.gameObject.SetActive(false);

            vcam.gameObject.SetActive(true);
            _currentVcam = vcam;

            cinematicCamera.SetActive(true);
            if (_playerCamera != null) _playerCamera.enabled = false;
        }

        // ─────────────────────────── Fade ───────────────────────────

        private IEnumerator Fade(float to, float seconds)
        {
            var group = EnsureFadeGroup();
            float from = group.alpha;

            if (seconds > 0f)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed    += Time.deltaTime;
                    group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                    yield return null;
                }
            }

            group.alpha = to;
        }

        /// <summary>
        /// แผ่นดำเต็มจอ — สร้างเองถ้าไม่ได้ใส่ไว้ ไม่บังเมาส์ (blocksRaycasts = false)
        /// </summary>
        private CanvasGroup EnsureFadeGroup()
        {
            if (fadeGroup != null) return fadeGroup;

            var go = new GameObject("Ending Fade", typeof(RectTransform), typeof(Canvas),
                                    typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = fadeSortOrder;

            var image = go.GetComponent<Image>();
            image.color         = Color.black;
            image.raycastTarget = false;

            fadeGroup = go.GetComponent<CanvasGroup>();
            fadeGroup.alpha          = 0f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable   = false;

            return fadeGroup;
        }

        // ─────────────────────────── Sound / Dialogue ───────────────────────────

        private static readonly HashSet<string> MissingEvents = new HashSet<string>();

        private void PlaySound(string path, Transform at)
        {
            if (string.IsNullOrWhiteSpace(path) || MissingEvents.Contains(path)) return;

            Vector3 position = at != null ? at.position
                             : _playerCamera != null ? _playerCamera.transform.position
                             : Vector3.zero;

            // event ที่ยังไม่มีใน FMOD ต้องไม่ทำคัทซีนพัง — เตือนครั้งเดียวแล้วเล่นต่อแบบเงียบ
            try
            {
                RuntimeManager.PlayOneShot(path, position);
            }
            catch (EventNotFoundException)
            {
                MissingEvents.Add(path);
                Debug.LogWarning($"[EndingCutscene] ยังไม่มี event '{path}' ใน FMOD — ขั้นนี้จะเงียบไป", this);
            }
        }

        private static IEnumerator ShowLines(DialogueLine[] lines)
        {
            if (lines == null || lines.Length == 0 || PlayerDialogueUI.instance == null) yield break;

            bool done = false;
            void Handler() => done = true;

            PlayerDialogueUI.instance.OnSequenceComplete += Handler;
            PlayerDialogueUI.instance.ShowSequence(lines);

            yield return new WaitUntil(() => done);
            PlayerDialogueUI.instance.OnSequenceComplete -= Handler;
        }

        #endregion
    }
}
