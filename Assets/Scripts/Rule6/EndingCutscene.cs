using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using Player;
using Sirenix.OdinInspector;
using TMPro;
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

            [Tooltip("กล้องสั่น (และเอียง) — เช่น ตอนรถโดนชน")]
            Shake,

            [Tooltip("ไฟค่อยๆ สว่าง/มืดไปถึงค่าที่กำหนด — เช่น ไฟหน้ารถที่พุ่งเข้ามา")]
            LightRamp,

            [Tooltip("ให้หลายตัว (เช่น เงาคนในรถ) ค่อยๆ หันมามองเรา ทีละตัว")]
            TurnToward,

            [Tooltip("แถบดำบน-ล่างแบบหนัง เลื่อนเข้า/ออก — ปกติเข้ามาเองตอนคัทซีนเริ่ม ใช้ขั้นนี้ถ้าอยากเอาออกกลางทาง")]
            Letterbox,
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
            [Tooltip("To Black = ค่อยๆ คลุมจอด้วยสีที่กำหนด\nFrom Black = ค่อยๆ เอาแผ่นที่คลุมจอออก")]
            public FadeDirection fade = FadeDirection.ToBlack;

            [ShowIf(nameof(IsFadeIn))]
            [Tooltip("สีที่คลุมจอ — ดำ = ตัดดำ, ขาว = แสงจ้าจนตาพร่า")]
            public Color fadeColor = Color.black;

            [ShowIf(nameof(IsFadeIn))]
            [Tooltip("คลุมแค่ไหน — 1 = ทึบทั้งจอ, 0.4 = จางๆ ยังเห็นข้างหลัง")]
            [Range(0f, 1f)]
            public float fadeAlpha = 1f;

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

            // ── Shake ──

            [ShowIf(nameof(IsShake))]
            [Tooltip("สั่นแรงแค่ไหน (องศา)")]
            public float shakeAmplitude = 4f;

            [ShowIf(nameof(IsShake))]
            [Tooltip("ภาพเอียงไปข้างหนึ่งเท่านี้ (องศา) ช่วงที่สั่น — 0 = ไม่เอียง")]
            public float shakeTilt = 10f;

            [ShowIf(nameof(IsShake))]
            [Tooltip("✓ = กล้องค้างท่าเอียงไว้ตอนจบ (เช่น ตัดดำต่อทันที)  ✗ = กลับมาตรงเหมือนเดิม")]
            public bool shakeHoldAtEnd = true;

            // ── Light Ramp ──

            [ShowIf(nameof(IsLightRamp))]
            public Light light;

            [ShowIf(nameof(IsLightRamp))]
            [Tooltip("ความสว่างปลายทาง — ไฟที่ปิดอยู่จะถูกเปิดให้เอง")]
            public float lightIntensity = 20f;

            // ── Turn Toward ──

            [ShowIf(nameof(IsTurn))]
            [Tooltip("ตัวที่จะหัน — ใส่ตัวแม่ของแต่ละคน (หรือแค่หัว ถ้าโมเดลมีหัวแยก)")]
            public Transform[] turners;

            [ShowIf(nameof(IsTurn))]
            [Tooltip("หันไปมองอะไร — เว้นว่าง = ตาของผู้เล่น")]
            public Transform turnTarget;

            [ShowIf(nameof(IsTurn))]
            [Tooltip("ตัวถัดไปเริ่มหันหลังตัวก่อนหน้าเท่านี้ (วินาที) — 0 = หันพร้อมกันหมด")]
            public float stagger = 0.4f;

            [ShowIf(nameof(IsTurn))]
            [Tooltip("✓ = หันแค่ซ้ายขวา (ไม่ก้มเงย) — ตัวที่นั่งอยู่จะไม่เอนตัวแปลกๆ")]
            public bool yawOnly = true;

            // ── Letterbox ──

            [ShowIf(nameof(IsLetterbox))]
            [Tooltip("✓ = แถบดำเลื่อนเข้ามา  ✗ = เลื่อนออก")]
            public bool barsOn = true;

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
            private bool IsFadeIn         => IsFade && fade == FadeDirection.ToBlack;
            private bool IsShake          => action == StepAction.Shake;
            private bool IsLightRamp      => action == StepAction.LightRamp;
            private bool IsTurn           => action == StepAction.TurnToward;
            private bool IsLetterbox      => action == StepAction.Letterbox;

            private bool UsesSeconds => action == StepAction.Wait || IsLook || IsDrive || IsCameraCut || IsFade || IsMoveTo
                                        || IsShake || IsLightRamp || IsTurn || IsLetterbox;

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
                        case StepAction.Shake:      return "Shake Seconds";
                        case StepAction.LightRamp:  return "Ramp Seconds";
                        case StepAction.TurnToward: return "Turn Seconds (each)";
                        case StepAction.Letterbox:  return "Slide Seconds";
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

        [Header("แถบดำบน-ล่าง (Letterbox)")]
        [Tooltip("✓ = แถบดำเลื่อนเข้ามาเองตอนคัทซีนเริ่ม\n" +
                 "หน้าแชทเต็มจอ (Set Active ที่เปิดหน้าแชท / ขั้น Chat) จะเอาแถบออกให้ทันที ไม่งั้นบังหัวกับท้ายแชท")]
        [SerializeField] private bool letterbox = true;

        [Tooltip("ความสูงของแถบแต่ละข้าง เทียบกับความสูงจอ — 0.1 = บน 10% ล่าง 10% (ใกล้ๆ จอหนัง 2.35:1)")]
        [Range(0f, 0.25f)]
        [SerializeField] private float letterboxHeight = 0.1f;

        [Tooltip("ใช้เวลาเลื่อนเข้ากี่วินาทีตอนคัทซีนเริ่ม")]
        [SerializeField] private float letterboxSeconds = 0.8f;

        [Tooltip("ลำดับ canvas ของแถบ — สูงกว่าจอดำ/จอขาว (500) แถบจะได้ดำสนิทตลอด แต่ต่ำกว่ารอยแตก (600)")]
        [SerializeField] private int letterboxSortOrder = 550;

        [Header("จอจบ (Thank you for playing)")]
        [Tooltip("✓ = เล่นทุกขั้นจบแล้ว จอค่อยๆ ดำ แล้วตัวอักษรค่อยๆ ขึ้นมา — ค้างไว้อย่างนั้น (จบเกม)")]
        [SerializeField] private bool endCard = true;

        [Tooltip("ข้อความหลัก — เป็นต้นฉบับภาษาอังกฤษ คำแปลไทยมาจาก Translations.tsv")]
        [SerializeField] private string endCardTitle = "Thank you for playing";

        [Tooltip("บรรทัดเล็กใต้ข้อความหลัก — เว้นว่าง = ไม่มี")]
        [SerializeField] private string endCardSubtitle = "Rule of Horror";

        [Tooltip("ฟอนต์ตอนเล่นภาษาอังกฤษ — เว้นว่าง = ใส่ Special Elite ให้เอง")]
        [SerializeField] private TMP_FontAsset endCardFont;

        [Tooltip("ฟอนต์ตอนเล่นภาษาไทย — เว้นว่าง = ใส่ Torsilp Chettana ให้เอง\n" +
                 "ถ้ายังว่างอยู่ตอนเล่น จะขึ้นข้อความอังกฤษแทน ไม่งั้นตัวไทยขึ้นเป็นสี่เหลี่ยม")]
        [SerializeField] private TMP_FontAsset endCardThaiFont;

        [Tooltip("รอหลังขั้นสุดท้ายจบกี่วินาทีก่อนเริ่มดำ — ให้ภาพสุดท้าย/เสียงสุดท้ายได้ค้างนิดนึง")]
        [SerializeField] private float endCardDelay = 1f;

        [Tooltip("จอค่อยๆ ดำกี่วินาที")]
        [SerializeField] private float endCardFadeSeconds = 2f;

        [Tooltip("ดำสนิทแล้วเงียบไว้กี่วินาทีก่อนตัวอักษรขึ้น")]
        [SerializeField] private float endCardPause = 1f;

        [Tooltip("ตัวอักษรค่อยๆ ขึ้นกี่วินาที")]
        [SerializeField] private float endCardTextSeconds = 3f;

        [Tooltip("ลำดับ canvas — สูงกว่าทุกอย่างของคัทซีน (แถบดำ 550, รอยแตก 600) จอจบต้องดำสนิททั้งจอ")]
        [SerializeField] private int endCardSortOrder = 700;

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

        // แถบดำบน-ล่าง — สร้างตอนใช้ครั้งแรก
        private RectTransform _barTop;
        private RectTransform _barBottom;
        private Canvas        _barCanvas;
        private float         _barAmount;    // 0 = ไม่มีแถบ, 1 = เต็มความสูงที่ตั้งไว้
        private Coroutine     _barRoutine;

        // จอจบ — สร้างตอนใช้ครั้งแรก
        private Canvas      _cardCanvas;
        private CanvasGroup _cardBlack;
        private CanvasGroup _cardText;

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

            if (letterbox) SetBars(true, letterboxSeconds);

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

            if (endCard) yield return EndCard();

            _playing = false;
        }

        /// <summary>
        /// หยุดทุกอย่างที่ค้างอยู่ — เรียกตอนกฎ 6 ถูกปิดกลางคัน
        /// ไม่คืนกล้อง: คัทซีนจบที่ภาพสุดท้ายของมันโดยตั้งใจ (บทจบต่อจากตรงนั้น)
        /// แต่เอาแถบดำออก — ถูกตัดกลางคันแล้วแถบค้างอยู่ จะบังเกมไปตลอด
        /// จอจบก็เอาออกเฉพาะตอนถูกตัดกลางคัน — เล่นจบแล้วมันคือจอสุดท้ายของเกม ต้องค้างไว้
        /// </summary>
        public void Stop()
        {
            if (_playing && _cardCanvas != null) _cardCanvas.enabled = false;

            StopAllCoroutines();
            _driveRoutine = null;
            _barRoutine   = null;
            _looking      = false;
            _playing      = false;
            ApplyBars(0f);
        }

        private void OnDisable() => Stop();

#if UNITY_EDITOR
        // ใส่ฟอนต์ให้เองถ้ายังว่าง — คัทซีนมีหลายตัว (คันที่ 1-3) จะได้ไม่ต้องลากใส่ทีละตัว
        private const string EndCardFontPath     = "Assets/Fonts/SpecialElite-Regular SDF.asset";
        private const string EndCardThaiFontPath = "Assets/Fonts/Thai/TorsilpChettana-Medium SDF.asset";

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            bool changed = false;
            if (endCardFont == null)
            {
                endCardFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EndCardFontPath);
                changed |= endCardFont != null;
            }
            if (endCardThaiFont == null)
            {
                endCardThaiFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EndCardThaiFontPath);
                changed |= endCardThaiFont != null;
            }
            if (changed) UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

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
                    return step.fade == FadeDirection.ToBlack
                        ? Fade(step.fadeAlpha, step.seconds, step.fadeColor)
                        : Fade(0f, step.seconds, null);

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
                    if (step.target != null)
                    {
                        // เปิดหน้าแชทเต็มจอ = ตัดไปหน้า UI แถบต้องหายพร้อมกัน ไม่งั้นบังหัว/ท้ายแชท
                        if (step.active && step.target.GetComponentInChildren<EndingChat>(true) != null)
                            SetBars(false, 0f);
                        step.target.SetActive(step.active);
                    }
                    else Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Target", this);
                    return null;

                case StepAction.Chat:
                    SetBars(false, 0f);
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

                case StepAction.Shake:
                    return Shake(step);

                case StepAction.LightRamp:
                    if (step.light != null) return LightRamp(step.light, step.lightIntensity, step.seconds);
                    Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Light", this);
                    return null;

                case StepAction.TurnToward:
                    if (step.turners != null && step.turners.Length > 0) return TurnAll(step);
                    Debug.LogWarning($"[EndingCutscene] ขั้น {index} ({step.note}) ยังไม่ได้ใส่ Turners", this);
                    return null;

                case StepAction.Letterbox:
                    SetBars(step.barsOn, step.seconds);
                    return WaitSeconds(step.seconds);
            }

            return null;
        }

        // ─────────────────────────── Letterbox ───────────────────────────

        /// <summary>เลื่อนแถบเข้า/ออก — สั่งใหม่ระหว่างที่ยังเลื่อนอยู่ได้ จะเลื่อนต่อจากจุดที่ค้างอยู่</summary>
        private void SetBars(bool on, float seconds)
        {
            if (_barRoutine != null) StopCoroutine(_barRoutine);
            _barRoutine = null;

            if (seconds <= 0f) { ApplyBars(on ? 1f : 0f); return; }
            _barRoutine = StartCoroutine(SlideBars(on ? 1f : 0f, seconds));
        }

        private IEnumerator SlideBars(float to, float seconds)
        {
            float from = _barAmount;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                ApplyBars(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds)));
                yield return null;
            }

            ApplyBars(to);
            _barRoutine = null;
        }

        /// <summary>
        /// ใช้ anchor แทนพิกเซล — แถบสูงเป็นสัดส่วนของจอเสมอ ไม่ว่าจอจะละเอียดแค่ไหน
        /// </summary>
        private void ApplyBars(float amount)
        {
            _barAmount = amount;
            if (amount <= 0f && _barCanvas == null) return;   // ยังไม่เคยใช้ ไม่ต้องสร้าง

            EnsureBars();
            float h = letterboxHeight * amount;

            _barTop.anchorMin    = new Vector2(0f, 1f - h);
            _barBottom.anchorMax = new Vector2(1f, h);
            _barCanvas.enabled   = amount > 0f;
        }

        private void EnsureBars()
        {
            if (_barCanvas != null) return;

            var go = new GameObject("Ending Letterbox", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);

            _barCanvas = go.GetComponent<Canvas>();
            _barCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _barCanvas.sortingOrder = letterboxSortOrder;

            _barTop    = Bar(go.transform, "Top",    new Vector2(0f, 1f), Vector2.one);
            _barBottom = Bar(go.transform, "Bottom", Vector2.zero,        new Vector2(1f, 0f));
        }

        private static RectTransform Bar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color         = Color.black;
            image.raycastTarget = false;
            return rt;
        }

        // ─────────────────────────── Shake ───────────────────────────

        /// <summary>
        /// สั่นที่ตัวกล้อง ไม่ใช่ที่ cameraPivot — pivot เป็นของการหันมอง (LateUpdate เขียนทุกเฟรม)
        /// ถ้าสั่นที่ pivot สองอย่างจะแย่งกันเขียน แล้วการสั่นจะหายไปครึ่งหนึ่ง
        ///
        /// ใช้ Perlin noise ไม่ใช่ Random — สุ่มใหม่ทุกเฟรมจะกระตุกเป็นจุดๆ ดูเหมือนเฟรมเรตตก
        /// ความแรงพุ่งขึ้นเร็วแล้วค่อยๆ ลด ส่วนการเอียงค่อยๆ เอนไปจนสุดตอนท้าย (รถกำลังพลิก)
        /// </summary>
        private IEnumerator Shake(Step step)
        {
            Camera cam = _currentVcam != null ? null : _playerCamera;
            Transform t = cam != null ? cam.transform : (_currentVcam != null ? _currentVcam.transform : null);
            if (t == null) yield break;

            Quaternion baseRot = t.localRotation;
            Vector3    basePos = t.localPosition;
            float seconds = Mathf.Max(0.01f, step.seconds);
            float seed    = UnityEngine.Random.value * 100f;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float k     = Mathf.Clamp01(elapsed / seconds);
                // แรงกระแทกช่วงแรกแรงกว่าปกติสามเท่า แล้วค่อยๆ ลดลงเหลือแค่สั่น
                float kick  = 1f + 2f * Mathf.Exp(-elapsed * 6f);
                float power = step.shakeAmplitude * kick * Mathf.Lerp(1f, 0.6f, k) * Mathf.Clamp01(elapsed / 0.05f);
                float n     = elapsed * 22f;

                // Perlin ส่วนใหญ่ออกมาแค่ 0.3–0.7 — คูณ 4 ให้ shakeAmplitude ใกล้เคียงองศาที่สั่นจริง
                float x = (Mathf.PerlinNoise(seed,      n) - 0.5f) * 4f * power;
                float y = (Mathf.PerlinNoise(seed + 10, n) - 0.5f) * 4f * power;
                float z = (Mathf.PerlinNoise(seed + 20, n) - 0.5f) * 4f * power + step.shakeTilt * Mathf.SmoothStep(0f, 1f, k);

                t.localRotation = baseRot * Quaternion.Euler(x, y, z);
                t.localPosition = basePos + new Vector3(x, y, 0f) * 0.01f;
                yield return null;
            }

            if (step.shakeHoldAtEnd)
                t.localRotation = baseRot * Quaternion.Euler(0f, 0f, step.shakeTilt);
            else
                t.localRotation = baseRot;
            t.localPosition = basePos;
        }

        // ─────────────────────────── Light Ramp ───────────────────────────

        /// <summary>
        /// ความสว่างไล่แบบเร่งขึ้นเรื่อยๆ (k²) — ไฟของรถที่วิ่งเข้าหาเราสว่างช้าๆ ตอนอยู่ไกล
        /// แล้วจ้าขึ้นพรวดตอนใกล้ ไล่แบบเส้นตรงจะดูเหมือนคนค่อยๆ หมุนสวิตช์หรี่ไฟ
        /// </summary>
        private static IEnumerator LightRamp(Light light, float to, float seconds)
        {
            if (!light.gameObject.activeSelf) light.gameObject.SetActive(true);
            if (!light.enabled) { light.enabled = true; light.intensity = 0f; }

            float from = light.intensity;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / seconds);
                light.intensity = Mathf.Lerp(from, to, k * k);
                yield return null;
            }

            light.intensity = to;
        }

        // ─────────────────────────── Turn Toward ───────────────────────────

        private IEnumerator TurnAll(Step step)
        {
            int running = 0;

            for (int i = 0; i < step.turners.Length; i++)
            {
                if (step.turners[i] == null) continue;

                running++;
                StartCoroutine(TurnOne(step.turners[i], step, () => running--));

                if (step.stagger > 0f && i < step.turners.Length - 1)
                    yield return new WaitForSeconds(step.stagger);
            }

            while (running > 0) yield return null;
        }

        /// <summary>
        /// หันช้าๆ แบบเร่งแล้วผ่อน (SmoothStep) — อ่านเป้าใหม่ทุกเฟรม ตัวที่หันอยู่จะมองตามเรา
        /// ถึงรถจะขยับหรือกล้องจะสั่นก็ตาม
        /// </summary>
        private IEnumerator TurnOne(Transform t, Step step, Action done)
        {
            Quaternion from    = t.rotation;
            float      seconds = Mathf.Max(0.01f, step.seconds);

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                t.rotation = Quaternion.Slerp(from, FaceRotation(t, step), Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                yield return null;
            }

            t.rotation = FaceRotation(t, step);
            done();
        }

        private Quaternion FaceRotation(Transform t, Step step)
        {
            Vector3 target = step.turnTarget != null ? step.turnTarget.position
                           : _playerCamera != null   ? _playerCamera.transform.position
                           : t.position + t.forward;

            Vector3 dir = target - t.position;
            if (step.yawOnly) dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return t.rotation;

            Vector3 up = step.yawOnly ? Vector3.up : (t.parent != null ? t.parent.up : Vector3.up);
            return Quaternion.LookRotation(dir.normalized, up);
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

        /// <summary>
        /// color = null คือใช้สีเดิมของแผ่น (ตอนเอาแผ่นออก) — เปลี่ยนสีแผ่นกลางทางได้ เช่น
        /// จอขาวจ้าแล้วตัดเป็นดำทันทีตอนรถชน
        /// </summary>
        private IEnumerator Fade(float to, float seconds, Color? color)
        {
            var group = EnsureFadeGroup();
            if (color.HasValue)
            {
                var image = group.GetComponent<Image>();
                if (image != null) image.color = new Color(color.Value.r, color.Value.g, color.Value.b, 1f);
            }

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

        // ─────────────────────────── จอจบ ───────────────────────────

        /// <summary>
        /// จอดำค่อยๆ มา → เงียบไว้ครู่หนึ่ง → "Thank you for playing" ค่อยๆ ขึ้น แล้วค้างไว้
        ///
        /// ใช้ canvas ของตัวเอง ไม่ใช้แผ่นดำของขั้น Fade: แผ่นนั้นอยู่ใต้แถบดำกับรอยแตก (คันที่ 3)
        /// ถ้าใช้แผ่นเดิม รอยแตกจะค้างทับตัวอักษร
        /// </summary>
        private IEnumerator EndCard()
        {
            EnsureEndCard();

            _cardBlack.alpha    = 0f;
            _cardText.alpha     = 0f;
            _cardCanvas.enabled = true;

            if (endCardDelay > 0f) yield return new WaitForSeconds(endCardDelay);
            yield return FadeGroup(_cardBlack, 1f, endCardFadeSeconds);

            if (endCardPause > 0f) yield return new WaitForSeconds(endCardPause);
            yield return FadeGroup(_cardText, 1f, endCardTextSeconds);
        }

        private static IEnumerator FadeGroup(CanvasGroup group, float to, float seconds)
        {
            float from    = group.alpha;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed    += Time.deltaTime;
                group.alpha = Mathf.SmoothStep(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }
            group.alpha = to;
        }

        private void EnsureEndCard()
        {
            if (_cardCanvas == null)
            {
                var go = new GameObject("Ending Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false);

                _cardCanvas = go.GetComponent<Canvas>();
                _cardCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
                _cardCanvas.sortingOrder = endCardSortOrder;

                // ขนาดตัวอักษรตามความสูงจอ — จอ 4K กับ 720p เห็นเท่ากัน
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight  = 1f;

                var black = Bar(go.transform, "Black", Vector2.zero, Vector2.one);
                _cardBlack = black.gameObject.AddComponent<CanvasGroup>();

                var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
                text.transform.SetParent(go.transform, false);
                var textRt = (RectTransform)text.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;
                _cardText = text.GetComponent<CanvasGroup>();

                Label(text.transform, "Title",    0.52f, 0.62f, 64f, new Color(0.92f, 0.92f, 0.92f));
                Label(text.transform, "Subtitle", 0.43f, 0.50f, 30f, new Color(0.55f, 0.55f, 0.55f));

                foreach (var group in new[] { _cardBlack, _cardText })
                {
                    group.blocksRaycasts = false;
                    group.interactable   = false;
                }
            }

            // ตั้งข้อความทุกครั้งที่ขึ้น — ภาษาอาจถูกเปลี่ยนไปแล้วตั้งแต่ครั้งก่อน
            // ภาษาไทยแต่ไม่มีฟอนต์ไทย = ขึ้นต้นฉบับอังกฤษ ดีกว่าขึ้นเป็นสี่เหลี่ยม
            bool thai = L10n.IsThai && endCardThaiFont != null;
            TMP_FontAsset font = thai ? endCardThaiFont : endCardFont;

            var labels = _cardText.GetComponentsInChildren<TextMeshProUGUI>(true);
            SetLabel(labels[0], endCardTitle,    thai, font);
            SetLabel(labels[1], endCardSubtitle, thai, font);
        }

        private static void SetLabel(TextMeshProUGUI label, string source, bool thai, TMP_FontAsset font)
        {
            if (font != null) label.font = font;
            // ระยะห่างตัวอักษรใช้กับไทยไม่ได้ — สระ/วรรณยุกต์ก็นับเป็นตัวอักษร จะถูกดันหลุดไปข้างหลัง
            label.characterSpacing = thai ? 0f : 6f;
            label.text = string.IsNullOrWhiteSpace(source) ? "" : thai ? L10n.T(source) : source;
            label.gameObject.SetActive(label.text.Length > 0);
        }

        private static void Label(Transform parent, string name, float yMin, float yMax, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.05f, yMin);
            rt.anchorMax = new Vector2(0.95f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var label = go.GetComponent<TextMeshProUGUI>();
            label.alignment        = TextAlignmentOptions.Center;
            label.fontSize         = size;
            label.color            = color;
            label.raycastTarget    = false;
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
