using System;
using System.Collections;
using System.Collections.Generic;
using InputSystem;
using Manager;
using Player;
using Rule2;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RuleSystem.Rule
{
    public enum Rule2State
    {
        None,
        FirstBlackout,        // ไฟดับครั้งแรก – เปิดวิทยุได้ยินเสียงคลื่น
        FirstBlackoutReturn,  // ผู้เล่นกลับมานั่ง → สุ่ม event 1-5
        SecondBlackout,       // ไฟดับครั้งที่ 2 – มืดสนิท, ไฟฉายกระพริบ
        FixPanel,             // ซ่อมตู้ไฟ + เงาข้างตู้ + random sounds ทุก 5 วิ
        ReturnToSeat,         // เดินกลับศาลา + random footstep
        RadioWaiting,         // ไฟกระพริบ + random ghost sighting → EndRule
        Completed
    }

    public class Rule2 : RuleBase
    {
        // ── Timing ──────────────────────────────────────────────────────
        [Header("Timing")]
        [SerializeField] private float hourFirstBlackout   = 0f;
        [SerializeField] private float minuteFirstBlackout = 0f;

        // ── Core References ─────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private RadioControl     radioControl;
        [SerializeField] private InputHandler     inputHandler;
        [SerializeField] private SwitchLight      switchLight;
        [SerializeField] private PlayerController playerController;
        [SerializeField] public Rule2State       currentState;

        // ── Lights ──────────────────────────────────────────────────────
        [Header("Lights")]
        [SerializeField] private Light   saraLight;
        [SerializeField] private Light[] streetLights;  // ไฟตรงถนน ดับตอน Second Blackout
        [SerializeField] private Light   flashlight;    // ไฟฉายมือถือ (กระพริบระหว่างมืด)

        // ── Panels ──────────────────────────────────────────────────────
        [Header("Panels")]
        [SerializeField] private LightPanel  panelPrefab;
        [SerializeField] private Transform[] panelSpawnPoints;

        // ── Ghost & Shadows ─────────────────────────────────────────────
        [Header("Ghost & Shadows")]
        [SerializeField] private GameObject runningGhost;          // ผีวิ่งช่วง ReturnToSeat

        [Tooltip("Prefab เงาดำๆ ที่ปรากฏหน้าผู้เล่น (First Blackout Return event 3)")]
        [SerializeField] private GameObject shadowPrefab;
        [SerializeField] private Transform  shadowSpawnPoint;      // จุด spawn เงาดำหน้าผู้เล่น

        [Tooltip("Prefab เงาข้างตู้ไฟ ระหว่างซ่อม")]
        [SerializeField] private GameObject panelGhostShadowPrefab;

        [Tooltip("Prefab ร่างผีในศาลา (Radio Waiting ghost sighting)")]
        [SerializeField] private GameObject pavilionGhostPrefab;
        [SerializeField] private Transform  pavilionGhostPoint;    // จุดยืนผีในศาลา

        // ── Random Sound Timing ──────────────────────────────────────────
        [Header("Fix Panel – Random Sound Interval (sec)")]
        [SerializeField] private float randomSoundMinInterval = 4f;
        [SerializeField] private float randomSoundMaxInterval = 6f;

        // ── Runtime ─────────────────────────────────────────────────────
        private readonly List<LightPanel>   activePanels   = new List<LightPanel>();
        private readonly List<GameObject>   spawnedShadows = new List<GameObject>();
        private int                         fixedPanels;
        private bool                        radioPlaying;
        private bool                        _lightTurnedOn;   // ผู้เล่นกดสวิตช์ไฟแล้วในรอบ First Blackout
        private Coroutine                   _randomSoundCoroutine;
        private Coroutine                   _flashlightFlickerCoroutine;

        // ════════════════════════════════════════════════════════════════
        #region Lifecycle
        // ════════════════════════════════════════════════════════════════

        public override void StartRule()
        {
            base.StartRule();
            StartCoroutine(RuleFlow());
        }

        void StartGameplay()
        {
            ResetRuntime();
            SpawnRandomPanels();
            TimeManager.instance.IsPauseTime(false);
        }

        bool PlayerIsSitting()  => PlayerController.Instance.IsSitting();
        bool PlayerEyesOpened() => GameModeController.instance.IsEyesOpen;

        IEnumerator RuleFlow()
        {
            TimeManager.instance.IsPauseTime(true);
            yield return new WaitUntil(PlayerIsSitting);
            PlayerController.Instance.isBlockStanding = true;
            GameModeController.instance.BlinkToMode(GameMode.Tension);
            yield return new WaitUntil(PlayerEyesOpened);
            StartGameplay();
        }

        protected override void UpdateRule()
        {
            // ตรวจเวลา → trigger First Blackout
            if (TimeManager.instance.CheckTime((int)hourFirstBlackout, (int)minuteFirstBlackout)
                && currentState == Rule2State.None)
            {
                ChangeState(Rule2State.FirstBlackout);
            }

            switch (currentState)
            {
                case Rule2State.FirstBlackout: UpdateFirstBlackout(); break;
                case Rule2State.FixPanel:      UpdateFixPanel();      break;
                case Rule2State.ReturnToSeat:  UpdateReturnToSeat();  break;
                // FirstBlackoutReturn / SecondBlackout / RadioWaiting driven by coroutines
            }
        }

        public override void EndRule()
        {
            TimeManager.instance.SetTime(20, 39);
            TimeManager.instance.IsPauseTime(false);
            GameModeController.instance.BlinkToMode(GameMode.Relax);
            PlayerController.Instance.isBlockStanding = false;
            StopHelperCoroutines();
            AudioManager.instance.StopAllRule2Sounds(); // หยุดทุก loop ของ Rule 2

            // ลบ ElectricBox ทั้งหมดที่ spawn ไว้
            foreach (var panel in activePanels)
                if (panel != null) Destroy(panel.gameObject);
            activePanels.Clear();

            base.EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region State Machine Helpers
        // ════════════════════════════════════════════════════════════════

        void ChangeState(Rule2State next)
        {
            currentState = next;
            switch (next)
            {
                case Rule2State.FirstBlackout:       StartFirstBlackout();       break;
                case Rule2State.FirstBlackoutReturn: StartFirstBlackoutReturn(); break;
                case Rule2State.SecondBlackout:      StartSecondBlackout();      break;
                case Rule2State.FixPanel:            StartFixPanel();            break;
                case Rule2State.ReturnToSeat:        StartReturnToSeat();        break;
                case Rule2State.RadioWaiting:        StartRadioWaiting();        break;
            }
        }

        void ResetRuntime()
        {
            fixedPanels    = 0;
            radioPlaying   = false;
            _lightTurnedOn = false;
        }

        void SpawnRandomPanels()
        {
            foreach (var p in activePanels)
                if (p != null) Destroy(p.gameObject);
            activePanels.Clear();

            // Fisher-Yates shuffle
            var pool = new List<Transform>(panelSpawnPoints);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int count = Mathf.Min(2, pool.Count);
            for (int i = 0; i < count; i++)
            {
                LightPanel panel = Instantiate(panelPrefab, pool[i].position, Quaternion.identity);
                panel.SetActiveLight(false);
                panel.SetRule(this, inputHandler);
                activePanels.Add(panel);
            }
        }

        void SetStreetLights(bool on)
        {
            if (streetLights == null) return;
            foreach (var l in streetLights)
            {
                if (l != null) l.enabled = on;
            }
            
        }

        void StopHelperCoroutines()
        {
            if (_randomSoundCoroutine      != null) StopCoroutine(_randomSoundCoroutine);
            if (_flashlightFlickerCoroutine != null) StopCoroutine(_flashlightFlickerCoroutine);
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 1 – First Blackout
        //   ไฟดับครั้งแรก เปิดวิทยุ → เสียงคลื่น
        // ════════════════════════════════════════════════════════════════

        void StartFirstBlackout()
        {
            saraLight.enabled = false;
            radioControl.UnlockRadio(true);
            switchLight.UnlockSwitch(true);          // ← unlock สวิตช์ตั้งแต่ไฟดับรอบแรก
            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.IsPauseTime(true);
        }

        void UpdateFirstBlackout()
        {
            // รอผู้เล่น: เปิดวิทยุ + กดสวิตช์ไฟ + กลับมานั่ง ครบ 3 อย่าง
            if (radioPlaying && _lightTurnedOn && PlayerIsSitting())
            {
                PlayerController.Instance.isBlockStanding = true;
                ChangeState(Rule2State.FirstBlackoutReturn);

            }
        }

        /// <summary>เรียกโดย RadioControl เมื่อผู้เล่นเปิดวิทยุ</summary>
        public void OnRadioTurnedOn()
        {
            if (radioPlaying) return;
            radioPlaying = true;
            AudioManager.instance.PlayRadio(); // เสียงคลื่น (RadioNoise)
        }

        /// <summary>เรียกโดย SwitchLight เมื่อผู้เล่นกดสวิตช์ไฟ</summary>
        public void OnLightTurnedOn()
        {
            // บันทึกว่ากดสวิตช์แล้ว (ใช้ตรวจ condition ใน First Blackout)
            _lightTurnedOn = true;
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 1.5 – First Blackout Return (สุ่ม event)
        //   ผู้เล่นกลับมานั่ง → สุ่ม 1 ใน 5 สิ่ง
        // ════════════════════════════════════════════════════════════════

        void StartFirstBlackoutReturn()
        {
            StartCoroutine(FirstBlackoutReturnRoutine());
        }

        IEnumerator FirstBlackoutReturnRoutine()
        {
            yield return new WaitForSeconds(Random.Range(1.5f, 3f));

            // สุ่ม 0-4
            switch (Random.Range(0, 3))
            {
                case 0: // เสียงโหยหวน
                    AudioManager.instance.PlayRule2Wail();
                    break;
                case 1: // เสียงเหมือนคนเดินในป่าข้างหลัง
                    AudioManager.instance.PlayRule2ForestFootsteps();
                    break;
                case 2: // เสียงเหมือนมีคนกระซิบข้างหู
                    AudioManager.instance.PlayRule2Whisper();
                    break;
                
            }

            // หน่วงก่อนเข้า Second Blackout
            yield return new WaitForSeconds(Random.Range(3f, 6f));
            ChangeState(Rule2State.SecondBlackout);
        }

        IEnumerator ShowPlayerShadowRoutine()
        {
            if (shadowPrefab != null && shadowSpawnPoint != null)
            {
                var shadow = Instantiate(shadowPrefab, shadowSpawnPoint.position, shadowSpawnPoint.rotation);
                yield return new WaitForSeconds(2f);
                Destroy(shadow);
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 2 – Second Blackout
        //   ทุกอย่างมืดสนิท, ไฟฉายกระพริบ, วิทยุ → บทสวด
        // ════════════════════════════════════════════════════════════════

        void StartSecondBlackout()
        {
            StartCoroutine(SecondBlackoutCoroutine());
        }

        IEnumerator SecondBlackoutCoroutine()
        {
            // ไฟกระพริบก่อนดับสนิท
            for (int i = 0; i < 9; i++)
            {
                saraLight.enabled = !saraLight.enabled;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
            }
            saraLight.enabled = false;
            SetStreetLights(false); // ไฟถนนดับ – มืดสนิท
            AudioManager.instance.PlayRule2AmbienceBG();
            // เปิดไฟฉาย (กระพริบเป็นช่วง)
            if (flashlight != null)
            {
                flashlight.enabled = true;
                _flashlightFlickerCoroutine = StartCoroutine(FlashlightFlickerRoutine());
            }

            // วิทยุ → ดับ → บทสวดปกติ
            yield return new WaitForSeconds(2f);
            AudioManager.instance.StopRadio();
            yield return new WaitForSeconds(6f);
            AudioManager.instance.PlayRadioPrayer(1); // บทสวดปกติ

            // เปิดให้ซ่อมตู้ได้ทันที
            yield return new WaitForSeconds(1f);
            ChangeState(Rule2State.FixPanel);
            PlayerController.Instance.isBlockStanding = false;

            // หลัง 30-50 วิ บทสวดเปลี่ยนเป็นถอยหลัง (background, ไม่ block Flow)
            StartCoroutine(SwitchPrayerToReverseRoutine());
        }

        IEnumerator SwitchPrayerToReverseRoutine()
        {
            yield return new WaitForSeconds(Random.Range(30f, 50f));
            if (currentState == Rule2State.FixPanel || currentState == Rule2State.ReturnToSeat)
                AudioManager.instance.PlayRadioPrayer(0); // สวดถอยหลัง
        }

        IEnumerator FlashlightFlickerRoutine()
        {
            while (true)
            {
                // ไฟติดปกติ ซักพัก
                yield return new WaitForSeconds(Random.Range(4f, 9f));

                // กระพริบ 2-4 ครั้ง
                int flickers = Random.Range(2, 5);
                for (int i = 0; i < flickers; i++)
                {
                    if (flashlight != null) flashlight.enabled = false;
                    yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
                    if (flashlight != null) flashlight.enabled = true;
                    yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
                }
            }
        }
        
        public void StartCompleteAmbience()
        {
            AudioManager.instance.PlayRule2Ambience();

        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 3 – Fix Panel
        //   เงาข้างตู้, random sound ทุก 4-6 วิ
        // ════════════════════════════════════════════════════════════════

        void StartFixPanel()
        {
            foreach (var panel in activePanels)
            {
                panel.SetActiveLight(true);
                panel.UnlockPanel(true);
            }

            SpawnPanelShadows();
            _randomSoundCoroutine = StartCoroutine(RandomFixPanelSoundRoutine());
        }

        void SpawnPanelShadows()
        {
            if (panelGhostShadowPrefab == null) return;
            Vector3 pos = activePanels[0].transform.position + activePanels[0].transform.right * 1.5f;
            var shadow = Instantiate(panelGhostShadowPrefab, pos, Quaternion.identity);
            spawnedShadows.Add(shadow);
        }

        void ClearPanelShadows()
        {
            foreach (var s in spawnedShadows)
                if (s != null) Destroy(s);
            spawnedShadows.Clear();
        }

        IEnumerator RandomFixPanelSoundRoutine()
        {
            while (currentState == Rule2State.FixPanel)
            {
                yield return new WaitForSeconds(
                    Random.Range(randomSoundMinInterval, randomSoundMaxInterval));

                if (currentState != Rule2State.FixPanel) yield break;

                // สุ่ม 1 ใน 5
                switch (Random.Range(0, 5))
                {
                    case 0: AudioManager.instance.PlayRule2GhostLaugh();   break;
                    case 1: AudioManager.instance.PlayRule2Eerie();         break;
                    case 2: AudioManager.instance.PlayRule2DogHowl();       break;
                    case 3: AudioManager.instance.PlayRule2BusApproach();   break;
                    case 4: break; // ไม่มีอะไร
                }
            }
        }

        void UpdateFixPanel() { /* รอ OnPanelFixed() callbacks */ }

        /// <summary>เรียกโดย LightPanel เมื่อผู้เล่นซ่อมตู้ไฟสำเร็จ</summary>
        public void OnPanelFixed()
        {
            fixedPanels++;
            if (fixedPanels < activePanels.Count) return;

            // ซ่อมเสร็จทั้งหมด → หยุด sounds + คืนไฟ
            if (_randomSoundCoroutine      != null) StopCoroutine(_randomSoundCoroutine);
            if (_flashlightFlickerCoroutine != null) StopCoroutine(_flashlightFlickerCoroutine);

            ClearPanelShadows();

            SetStreetLights(true);

            HorrorTextUI.instance.HideText();
            switchLight.UnlockSwitch(true);
            runningGhost.SetActive(true);

            ChangeState(Rule2State.ReturnToSeat);
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 4 – Return To Seat
        //   เดินกลับ → สุ่มเสียงคนวิ่งผ่านหลัง
        // ════════════════════════════════════════════════════════════════

        void StartReturnToSeat()
        {
            StartCoroutine(ReturnToSeatAtmosphereRoutine());
        }

        IEnumerator ReturnToSeatAtmosphereRoutine()
        {
            yield return new WaitForSeconds(Random.Range(2f, 4f));
            // 50% โอกาสได้ยินเสียงคนวิ่งผ่านหลัง
            if (Random.value < 0.5f)
                AudioManager.instance.PlayRule2RunningFootsteps();
        }

        void UpdateReturnToSeat()
        {
            if (switchLight.IsLightOn())
            {
                //stop all sounds
                AudioManager.instance.StopAllRule2Sounds();
            }
            if (playerController.IsSitting() && switchLight.IsLightOn())
            {
                runningGhost.SetActive(false);
                PlayerController.Instance.isBlockStanding = true;
                TimeManager.instance.IsPauseTime(false);
                HorrorTextUI.instance.HideText();
                ChangeState(Rule2State.RadioWaiting);
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Phase 5 – Radio Waiting
        //   ไฟกระพริบ + สุ่มเห็นร่างผีในศาลา 2 วิ
        // ════════════════════════════════════════════════════════════════

        void StartRadioWaiting()
        {
            StartCoroutine(RadioWaitingRoutine());
        }

        IEnumerator RadioWaitingRoutine()
        {
            yield return new WaitForSeconds(Random.Range(2f, 4f));

            // 50% โอกาสเห็นร่างผีในศาลา
            if (Random.value < 0.5f)
                yield return StartCoroutine(PavilionGhostSightingRoutine());

            // รอนิดนึงหลังเหตุการณ์ → จบ Rule
            yield return new WaitForSeconds(2f);
            AudioManager.instance.StopPlayRadioPrayer();
            EndRule();
        }

        IEnumerator PavilionGhostSightingRoutine()
        {
            // ไฟดับ → ผีปรากฏ → ไฟติด → ผีอยู่ 2 วิ → ผีหาย → ไฟกระพริบ
            saraLight.enabled = false;
            yield return new WaitForSeconds(0.08f);

            GameObject ghost = null;
            if (pavilionGhostPrefab != null && pavilionGhostPoint != null)
                ghost = Instantiate(pavilionGhostPrefab,
                                    pavilionGhostPoint.position,
                                    pavilionGhostPoint.rotation);

            saraLight.enabled = true;
            yield return new WaitForSeconds(2f);

            if (ghost != null) Destroy(ghost);

            // ไฟกระพริบหลังผีหาย
            for (int i = 0; i < 5; i++)
            {
                saraLight.enabled = !saraLight.enabled;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }
            saraLight.enabled = true;
        }

        #endregion

    }
}
