using FMOD.Studio;
using FMODUnity;
using InputSystem;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace Rule4
{
    /// <summary>
    /// เสียงที่ตุ๊กตาแต่ละตัวส่งออกมา — ผู้เล่นหาตุ๊กตาด้วยการฟังเสียงอย่างเดียว
    /// Rule4 จะสับไพ่แจกให้ตุ๊กตาแต่ละตัวได้เสียงไม่ซ้ำกัน และสุ่มใหม่ทุกรอบ
    /// </summary>
    public enum DollSound
    {
        Cry,   // เสียงคนร้องไห้
        Laugh, // เสียงหัวเราะ
        Hum,   // เสียงฮัมเพลง
        Call,  // เสียงเรียก "มานี่........"
        Cough  // เสียงไอเป็นจังหวะ
    }

    /// <summary>
    /// ตุ๊กตา 1 ตัวใน Rule 4
    ///
    /// - ส่งเสียง loop แบบ 3D ตลอดเวลา (FMOD attenuation ทำให้ "เข้าใกล้ = ดังขึ้น" เอง)
    /// - ผู้เล่นเก็บด้วยปุ่ม E เมื่อเข้ามาในระยะ + หันหน้ามาทางตุ๊กตา
    /// - เก็บได้ทีละตัว — Rule4 เป็นคนตัดสินว่าเก็บได้หรือยัง (ยังถืออยู่ = เก็บเพิ่มไม่ได้)
    /// </summary>
    public class Doll : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("ระยะที่เก็บได้ (เมตร)")]
        [SerializeField] private float pickupRadius = 2.5f;

        [Tooltip("มุมสูงสุดระหว่างทิศที่กล้องมองกับตุ๊กตา ถึงจะนับว่ากำลังมองอยู่ (องศา)")]
        [SerializeField] private float lookAngle = 45f;

        [Tooltip("ข้อความ hint ตอนเข้าใกล้")]
        [SerializeField] private string interactHint = "E   เก็บตุ๊กตา";

        // ── Runtime ──
        private RuleSystem.Rule.Rule4 _rule;
        private InputHandler          _inputHandler;
        private Camera                _playerCam;
        private EventInstance         _voice;
        private bool                  _voiceStarted;
        private bool                  _subscribed;
        private bool                  _collected;

        /// <summary>เสียงที่ตุ๊กตาตัวนี้ใช้ — สุ่มตอน Setup</summary>
        public DollSound Sound { get; private set; }

        // ─────────────────────────── Setup ───────────────────────────

        public void Setup(RuleSystem.Rule.Rule4 rule, InputHandler inputHandler, DollSound sound)
        {
            _rule         = rule;
            _inputHandler = inputHandler;
            Sound         = sound;

            StartVoice();
        }

        private void Start()
        {
            _playerCam = Camera.main;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            StopVoice();
        }

        // ─────────────────────────── Voice (FMOD 3D loop) ───────────────────────────

        private void StartVoice()
        {
            string path = Sound switch
            {
                DollSound.Cry   => "event:/Rule4/DollCry",
                DollSound.Laugh => "event:/Rule4/DollLaugh",
                DollSound.Hum   => "event:/Rule4/DollHum",
                DollSound.Call  => "event:/Rule4/DollCall",
                _               => "event:/Rule4/DollCough"
            };

            // CreateInstance จะ throw ถ้ายังไม่มี event ใน FMOD — ห้ามให้หลุดออกไป
            // ไม่งั้น Rule4.StartGameplay() จะตายกลางคัน แล้วผู้เล่นลุกไม่ได้
            try
            {
                _voice = RuntimeManager.CreateInstance(path);
            }
            catch (EventNotFoundException)
            {
                Debug.LogWarning($"[Rule4] ยังไม่มี event '{path}' ใน FMOD — ตุ๊กตาตัวนี้จะไม่มีเสียง (ยังเก็บได้ปกติ)", this);
                return;
            }

            _voice.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
            _voice.start();
            _voiceStarted = true;
        }

        private void StopVoice()
        {
            if (!_voiceStarted) return;
            _voice.stop(STOP_MODE.ALLOWFADEOUT);
            _voice.release();
            _voiceStarted = false;
        }

        // ─────────────────────────── Update ───────────────────────────

        private void Update()
        {
            if (_collected) return;

            // ตำแหน่ง 3D ต้องอัปเดตเผื่อตุ๊กตาถูกย้าย (เช่นตอนสุ่มใหม่)
            if (_voiceStarted)
                _voice.set3DAttributes(RuntimeUtils.To3DAttributes(transform));

            bool inRange = IsPlayerInRange();

            if (inRange && !_subscribed)
            {
                _subscribed = true;
                if (_inputHandler != null) _inputHandler.OnInteractPressed += TryPickUp;
                if (HorrorTextUI.instance != null) HorrorTextUI.instance.ShowText(interactHint);
            }
            else if (!inRange && _subscribed)
            {
                Unsubscribe();
            }
        }

        private bool IsPlayerInRange()
        {
            if (_playerCam == null) _playerCam = Camera.main;
            if (_playerCam == null) return false;

            Vector3 toDoll = transform.position - _playerCam.transform.position;
            if (toDoll.sqrMagnitude > pickupRadius * pickupRadius) return false;

            return Vector3.Angle(_playerCam.transform.forward, toDoll.normalized) < lookAngle;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_inputHandler != null) _inputHandler.OnInteractPressed -= TryPickUp;
            if (HorrorTextUI.instance != null) HorrorTextUI.instance.HideText();
        }

        // ─────────────────────────── Pickup ───────────────────────────

        private void TryPickUp()
        {
            if (_collected || _rule == null) return;

            // Rule4 ปฏิเสธได้ถ้าผู้เล่นถือตุ๊กตาอยู่แล้ว (เก็บได้ทีละตัว)
            if (!_rule.TryPickUpDoll(this)) return;

            _collected = true;
            Unsubscribe();
            StopVoice();
            AudioManager.instance.PlayDollPickup();
            
            // ซ่อนไว้ก่อน — Rule4 เป็นคน Destroy ตอนวางที่ศาล
            gameObject.SetActive(false);
        }
    }
}
