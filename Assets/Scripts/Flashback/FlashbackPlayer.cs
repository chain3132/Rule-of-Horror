using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using Player;
using RuleSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Flashbacks
{
    /// <summary>
    /// จอฉาย flashback — เล่นฉากในช่วงตาปิดสนิทระหว่างเปลี่ยนโหมด ภาพเป็นฟิล์มเก่า
    ///
    /// ลำดับ (GameModeController.BlinkRoutine เรียก Play ตอนตาปิดสนิท แทนการรอจอดำ 0.8 วิ):
    ///   ตาปิด (จอดำ) → ค่อยๆ สว่างเข้า flashback → เล่นจนจบ / กดข้าม → ค่อยๆ ดำ → ลืมตาเข้าโหมดใหม่
    ///
    /// กล้องของฉากเรนเดอร์ลง RenderTexture เฉพาะตอนขึ้นเฟรมฟิล์มใหม่ (Film FPS) ภาพจึงกระตุกแบบหนังเก่า
    /// แล้ว RawImage ที่ใช้ shader "Old Film (UI)" ใส่ซีเปีย grain รอยขีด ฝุ่น ขอบมืด ภาพสั่นทับอีกชั้น
    ///
    /// Canvas ของตัวนี้ต้องอยู่เหนือเปลือกตา (Canvas ลำดับ 0) แต่ใต้ PlayerDialogueUI (10) ซับจึงขึ้นทับได้
    /// สร้างให้ด้วยเมนู Rule of Horror ▸ Flashback ▸ Create Flashback Screen
    ///
    /// ระหว่างเล่น: นาฬิกายังหยุดอยู่ (blink ถือไว้), IsBlinking ยังเป็น true ระบบที่รอ blink ก็รอต่อ
    /// และผู้เล่นถูกล็อกเดิน/หัน (เฉพาะที่ยังปลดอยู่ — ไม่ไปปลดของคนอื่นตอนจบ)
    /// </summary>
    [DisallowMultipleComponent]
    public class FlashbackPlayer : MonoBehaviour
    {
        public static FlashbackPlayer Instance { get; private set; }

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("จอ")]
        [Tooltip("ทั้งจอ flashback — ใช้ค่อยๆ สว่าง/ดำ")]
        [SerializeField] private CanvasGroup screenGroup;

        [Tooltip("RawImage เต็มจอที่ใส่ material ฟิล์มเก่า")]
        [SerializeField] private RawImage screen;

        [Tooltip("ข้อความบอกปุ่มข้าม (เว้นว่างได้)")]
        [SerializeField] private TMPro.TMP_Text skipLabel;

        [Header("ภาพ")]
        [Tooltip("ความสูงของภาพที่กล้องเรนเดอร์ (pixel) — กว้างตามสัดส่วนจอ ฟิล์มเก่าไม่ต้องคม 720 พอ")]
        [SerializeField] private int renderHeight = 720;

        [Tooltip("เฟรมต่อวินาทีของภาพฟิล์ม — หนังเก่า 16-18 / ใส่ 0 = ลื่นตามเกม")]
        [Range(0, 30)]
        [SerializeField] private int filmFps = 16;

        [Header("จังหวะ")]
        [Tooltip("ค่อยๆ สว่างจากจอดำเข้า flashback (วินาที)")]
        [SerializeField] private float fadeIn = 0.8f;

        [Tooltip("ค่อยๆ ดำกลับก่อนลืมตา (วินาที)")]
        [SerializeField] private float fadeOut = 0.8f;

        [Header("ข้าม")]
        [SerializeField] private bool allowSkip = true;
        [SerializeField] private Key skipKey = Key.Space;

        [Tooltip("ห้ามข้ามในช่วงแรก (วินาที) — กันกด Space ค้างมาจากก่อนหน้าแล้วข้ามทันที")]
        [SerializeField] private float skipGrace = 0.75f;

        [Tooltip("ข้อความบอกปุ่มข้าม (แปลผ่าน L10n)")]
        [SerializeField] private string skipHint = "Space   Skip";

        [Header("เสียงเกมระหว่าง flashback")]
        [Tooltip("FMOD bus ที่ลดเสียงลงระหว่าง flashback เช่น bus:/Ambience — เว้นว่าง = ไม่ลด\n" +
                 "อย่าใส่ bus:/ (master) เพราะเสียงของ flashback เองก็จะเบาตามไปด้วย")]
        [SerializeField] private string[] duckBuses;

        [Range(0f, 1f)]
        [SerializeField] private float duckVolume = 0.15f;

        // ─────────────────────────── คิว ───────────────────────────

        // flashback ที่รอเล่นใน blink ถัดไป — ไม่มี blink มาใช้ภายในเวลานี้ = ทิ้ง (กันไปโผล่ผิดจังหวะทีหลัง)
        private const float QueueLifetime = 15f;

        private static Flashback _queued;
        private static float     _queuedAt;
        private static readonly HashSet<int> _played = new HashSet<int>();

        // กฎที่ปุ่มเทสเพิ่งพาเข้ามา — flashback "ก่อนเริ่มกฎ" ของรอบนั้นเล่นแม้เคยเล่นไปแล้ว (Play Once)
        // เทสกฎเดิมซ้ำกี่รอบก็ได้เห็นฉากทุกรอบ
        private static RuleBase _devStartOf;

        /// <summary>ปุ่มเทสกำลังจบกฎ (End / ข้ามกฎ) — flashback "หลังผ่านกฎ" ที่เข้าคิวตอนนี้ไม่สน Play Once</summary>
        public static bool DevEnding { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            _queued     = null;
            _devStartOf = null;
            DevEnding   = false;
            _played.Clear();
        }

        /// <summary>ให้ flashback นี้เล่นใน blink เปลี่ยนโหมดครั้งถัดไป (ตอนตาปิดสนิท)</summary>
        /// <param name="ignorePlayOnce">✓ = เล่นแม้เคยเล่นไปแล้ว (ใช้ตอนเทส)</param>
        public static void Queue(Flashback flashback, bool ignorePlayOnce = false)
        {
            if (flashback == null) return;
            if (!ignorePlayOnce && flashback.PlayOnce && _played.Contains(flashback.GetInstanceID())) return;

            _queued   = flashback;
            _queuedAt = Time.realtimeSinceStartup;
        }

        // ─────────────────────────── ปุ่มเทส (RuleDevTools) ───────────────────────────

        /// <summary>
        /// ปุ่มเทสกดซ้อนตอน flashback กำลังเล่นอยู่ — ตัดตัวที่เล่นอยู่ทิ้งทันที (ไม่ค่อยๆ ดำ)
        /// ฉากของกฎที่ข้ามไปจะเข้าคิวแล้วเล่นต่อตามปกติ ตัวที่ถูกตัดไม่นับว่าเล่นแล้ว
        /// </summary>
        public static void AbortPlaying()
        {
            if (Instance != null && Instance.IsPlaying) Instance._abort = true;
        }

        /// <summary>กฎนี้ถูกปุ่มเทสพาเข้ามา — flashback "ก่อนเริ่มกฎ" ครั้งถัดไปเล่นแม้เคยเล่นแล้ว</summary>
        public static void MarkDevStart(RuleBase rule) => _devStartOf = rule;

        /// <summary>กฎนี้มี flashback ของจังหวะนี้ไหม</summary>
        public static bool HasFlashbackFor(RuleBase rule, FlashbackTrigger trigger) => Find(rule, trigger) != null;

        /// <summary>หา flashback ที่ผูกกับกฎนี้ + จังหวะนี้ แล้วเข้าคิว — RuleBase / ตัวนี้เรียกเอง</summary>
        public static void QueueFor(RuleBase rule, FlashbackTrigger trigger, bool ignorePlayOnce = false)
        {
            if (rule == null) return;

            if (trigger == FlashbackTrigger.BeforeRuleStarts && rule == _devStartOf)
            {
                _devStartOf    = null;
                ignorePlayOnce = true;
            }

            var fb = Find(rule, trigger);
            if (fb != null) Queue(fb, ignorePlayOnce);
        }

        private static Flashback Find(RuleBase rule, FlashbackTrigger trigger)
        {
            if (rule == null) return null;
            foreach (var fb in FindObjectsByType<Flashback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (fb.Trigger == trigger && fb.Rule == rule) return fb;
            return null;
        }

        /// <summary>BlinkRoutine เรียกตอนตาปิดสนิท — มีอะไรต้องเล่นไหม</summary>
        public static bool TryTakeQueued(out Flashback flashback)
        {
            flashback = _queued;
            _queued   = null;

            if (flashback == null || Instance == null) return false;
            if (Time.realtimeSinceStartup - _queuedAt > QueueLifetime) { flashback = null; return false; }
            return true;
        }

        // ─────────────────────────── Runtime ───────────────────────────

        private RenderTexture _rt;
        private Material      _material;
        private Flashback     _current;
        private bool          _abort;
        private bool          _lockedMove, _lockedLook;
        private EventInstance _sound;
        private readonly List<(Bus bus, float volume)> _ducked = new List<(Bus, float)>();

        private static readonly int FilmFrameId = Shader.PropertyToID("_FilmFrame");

        public bool IsPlaying => _current != null;

        private void Awake()
        {
            Instance = this;
            if (screenGroup != null)
            {
                screenGroup.alpha          = 0f;
                screenGroup.blocksRaycasts = false;
                screenGroup.interactable   = false;
            }
            if (skipLabel != null) skipLabel.gameObject.SetActive(false);

            // material ของตัวเอง — เขียน _FilmFrame ทุกเฟรมฟิล์ม ไม่ให้ไปแก้ asset
            if (screen != null && screen.material != null)
            {
                _material       = new Material(screen.material);
                screen.material = _material;
            }
        }

        private void OnEnable()
        {
            RuleBase.OnRuleCompleted -= HandleRuleCompleted;
            RuleBase.OnRuleCompleted += HandleRuleCompleted;
        }

        private void OnDisable()
        {
            RuleBase.OnRuleCompleted -= HandleRuleCompleted;
            Cleanup();   // coroutine ถูกตัดกลางคัน (เปลี่ยนซีน / ปิด object) — คืนทุกอย่าง
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_material != null) Destroy(_material);
        }

        private static void HandleRuleCompleted(RuleBase rule) =>
            QueueFor(rule, FlashbackTrigger.AfterRuleCompleted, ignorePlayOnce: DevEnding);

        // ─────────────────────────── เล่น ───────────────────────────

        /// <summary>เล่นจนจบ (หรือถูกข้าม) — เรียกระหว่างตาปิดสนิท จอเริ่มและจบที่สีดำ</summary>
        public IEnumerator Play(Flashback flashback)
        {
            if (flashback == null || screen == null) yield break;

            _current = flashback;
            _abort   = false;
            if (flashback.PlayOnce) _played.Add(flashback.GetInstanceID());

            LockPlayer();
            Duck(true);
            StartSound(flashback.FmodEvent);

            EnsureRenderTexture();
            screen.texture = _rt;
            flashback.Begin(_rt);

            float length    = flashback.Length;
            float elapsed   = 0f;
            float frameTime = filmFps > 0 ? 1f / filmFps : 0f;
            float sinceFrame = float.MaxValue;   // เฟรมแรกเรนเดอร์ทันที
            int   filmFrame = 0;
            int   nextLine  = 0;
            bool  skipped   = false;

            var lines = flashback.Lines;
            if (skipLabel != null)
            {
                skipLabel.text = L10n.T(skipHint);
                skipLabel.gameObject.SetActive(allowSkip && !string.IsNullOrWhiteSpace(skipHint));
            }

            while (elapsed < length && !skipped && !_abort)
            {
                float dt = Time.deltaTime;   // หยุดตามเมนู Esc (timeScale 0) เหมือน Timeline
                elapsed    += dt;
                sinceFrame += dt;

                // ── ภาพฟิล์ม: เรนเดอร์เฉพาะตอนขึ้นเฟรมใหม่ RenderTexture ค้างภาพเดิมไว้ระหว่างนั้น ──
                var cam = flashback.FilmCamera;
                bool newFrame = frameTime <= 0f || sinceFrame >= frameTime;
                if (cam != null) cam.enabled = newFrame;
                if (newFrame)
                {
                    sinceFrame = 0f;
                    filmFrame++;
                    if (_material != null) _material.SetFloat(FilmFrameId, filmFrame);
                }

                // ── ค่อยๆ สว่างเข้า / ค่อยๆ ดำออก (จอข้างหลังคือเปลือกตาที่ปิดสนิท = ดำ) ──
                if (screenGroup != null)
                {
                    float a = 1f;
                    if (fadeIn  > 0f) a = Mathf.Min(a, elapsed / fadeIn);
                    if (fadeOut > 0f) a = Mathf.Min(a, (length - elapsed) / fadeOut);
                    screenGroup.alpha = Mathf.Clamp01(a);
                }

                // ── ซับตามเวลา ──
                while (lines != null && nextLine < lines.Length && elapsed >= lines[nextLine].at)
                {
                    var line = lines[nextLine++];
                    if (line != null && !string.IsNullOrWhiteSpace(line.text) && PlayerDialogueUI.instance != null)
                        PlayerDialogueUI.instance.ShowSequence(
                            new[] { new DialogueLine { text = line.text, holdDuration = line.holdDuration } },
                            line.ghostVoice);
                }

                // ── ข้าม ──
                if (allowSkip && elapsed > skipGrace && !InGameLanguageMenu.IsOpen &&
                    Keyboard.current != null && Keyboard.current[skipKey].wasPressedThisFrame)
                    skipped = true;

                yield return null;
            }

            // ปุ่มเทสตัดทิ้ง — ไม่จาง ไม่นับว่าเล่นแล้ว
            if (_abort)
            {
                _played.Remove(flashback.GetInstanceID());
            }
            // ผู้เล่นกดข้าม — ยังค่อยๆ ดำออก ไม่ตัดฉับ
            else if (skipped && screenGroup != null && fadeOut > 0f)
            {
                var cam = flashback.FilmCamera;
                if (cam != null) cam.enabled = false;   // ภาพค้างเฟรมสุดท้ายระหว่างจาง

                float start = screenGroup.alpha;
                for (float t = 0f; t < fadeOut && !_abort; t += Time.deltaTime)
                {
                    screenGroup.alpha = Mathf.Lerp(start, 0f, t / fadeOut);
                    yield return null;
                }
            }

            if (PlayerDialogueUI.instance != null && lines != null && lines.Length > 0)
                PlayerDialogueUI.instance.Hide();

            Cleanup();
        }

        /// <summary>คืนทุกอย่าง — เรียกได้ซ้ำ</summary>
        private void Cleanup()
        {
            if (screenGroup != null) screenGroup.alpha = 0f;
            if (skipLabel != null) skipLabel.gameObject.SetActive(false);

            if (_current != null)
            {
                _current.End();
                _current = null;
            }
            _abort = false;

            StopSound();
            Duck(false);
            ReleasePlayer();
        }

        private void EnsureRenderTexture()
        {
            int h = Mathf.Max(64, renderHeight);
            int w = Mathf.Max(64, Mathf.RoundToInt(h * (Screen.width / (float)Mathf.Max(1, Screen.height))));
            if (_rt != null && _rt.width == w && _rt.height == h) return;

            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Flashback Film" };
            _rt.Create();
        }

        // ─────────────────────────── ผู้เล่น ───────────────────────────

        // ล็อกเฉพาะที่ยังปลดอยู่ — blink ไม่ได้ล็อกตัวผู้เล่น แต่ flashback ยาวหลายวินาที เดิน/หันได้ไม่ได้
        private void LockPlayer()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;
            if (pc.CanMove) { pc.SetMovement(false); _lockedMove = true; }
            if (pc.CanLook) { pc.SetLook(false);     _lockedLook = true; }
        }

        private void ReleasePlayer()
        {
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                if (_lockedMove) pc.SetMovement(true);
                if (_lockedLook) pc.SetLook(true);
            }
            _lockedMove = _lockedLook = false;
        }

        // ─────────────────────────── เสียง ───────────────────────────

        private void StartSound(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                _sound = RuntimeManager.CreateInstance(path);
                _sound.start();
            }
            catch (EventNotFoundException)
            {
                Debug.LogWarning($"[Flashback] ยังไม่มี event '{path}' ใน FMOD", this);
            }
        }

        private void StopSound()
        {
            if (!_sound.isValid()) return;
            _sound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _sound.release();
            _sound = default;
        }

        private void Duck(bool on)
        {
            if (on)
            {
                if (duckBuses == null) return;
                foreach (var path in duckBuses)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    try
                    {
                        Bus bus = RuntimeManager.GetBus(path);
                        bus.getVolume(out float volume);
                        bus.setVolume(volume * duckVolume);
                        _ducked.Add((bus, volume));
                    }
                    catch (BusNotFoundException)
                    {
                        Debug.LogWarning($"[Flashback] ไม่มี FMOD bus '{path}'", this);
                    }
                }
                return;
            }

            foreach (var (bus, volume) in _ducked)
                if (bus.isValid()) bus.setVolume(volume);
            _ducked.Clear();
        }
    }
}
