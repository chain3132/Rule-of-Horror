using System;
using System.Collections;
using System.Collections.Generic;
using System.HeartbeatSystem;
using InputSystem;
using JetBrains.Annotations;
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
        FirstBlackout,
        TurnOnLight,
        SecondBlackout,
        FixPanel,
        ReturnToSeat,
        RadioWaiting,
        GhostEvent,
        Completed
    }

    public class Rule2 : RuleBase
    {
        [Header("TimeToFirstBlackout")]
        [SerializeField] float hourFirstBlackout = 0f;
        [SerializeField] float minuteFirstBlackout = 0f;
        [Header("TimeToSecondBlackout")]
        [SerializeField] float hourSecondBlackout = 0f;
        [SerializeField] float minuteSecondBlackout = 0f;
        
        [SerializeField] private RadioControl radioControl;
        
        [SerializeField] private LightPanel panelPrefab;
        [SerializeField] private Transform[] panelSpawnPoints;
        [SerializeField] private List<Transform> activePanelPoints;

        [SerializeField] private GameObject runningGhost;
        private List<LightPanel> activePanels = new List<LightPanel>();

        [Header("Ghost")]
        public GameObject ghostPrefab;
        [SerializeField] private GameObject ghostJumpScarePrefab;
        [SerializeField] private GameObject ghostTestPrefab;
        private GameObject currentGhost;
        [SerializeField] private Transform ghostSpawnPoint;
        [SerializeField] private Transform ghostPointOutside;
        [SerializeField] private Transform ghostPointInside;
        [SerializeField] private Transform jumpScarePoint;

        
        [Header("Panel Setup")]
        
        [SerializeField] Light saraLight;
        [SerializeField] private InputHandler inputHandler;
        [SerializeField] private SwitchLight switchLight;
        [SerializeField] private Rule2State currentState;
        [SerializeField] private PlayerController playerController;

        private HashSet<LightPanel> fixedPanelSet = new HashSet<LightPanel>();
        private int fixedPanels = 0;

        private bool isSitting;
        private bool radioPlaying;
        private float radioTimer;
        private bool lightTurnedOn;

        private bool ghostTriggered;
        private bool _isRadioStopped;

        #region Lifecycle

        public override void StartRule()
        {
            base.StartRule();
            StartCoroutine(RuleFlow());
            
            //ChangeState(Rule2State.FirstBlackout);
        }

        void StartGameplay()
        {
            ResetState();
            SpawnRandomPanels();
            TimeManager.instance.IsPauseTime(false);

        }
        bool PlayerIsSitting()
        {
            return PlayerController.Instance.IsSitting();
        }
        bool PlayerEyesOpened()
        {
            return GameModeController.instance.IsEyesOpen;
        }
        IEnumerator RuleFlow()
        {
            TimeManager.instance.IsPauseTime(true);
            //  1. รอให้ผู้เล่น "นั่งก่อน"
            yield return new WaitUntil(() => PlayerIsSitting());
            PlayerController.Instance.isBlockStanding = true;
            GameModeController.instance.BlinkToMode(GameMode.Tension);
            yield return new WaitUntil(() => PlayerEyesOpened());
            // 3. เริ่มกฎจริง
            StartGameplay();
        }
        

        protected override void UpdateRule()
        {
            // sync player state
            //isSitting = PlayerState.Instance.IsSitting;
            if (TimeManager.instance.CheckTime((int)hourFirstBlackout, (int)minuteFirstBlackout) && currentState == Rule2State.None)
            {
                ChangeState(Rule2State.FirstBlackout);
            }
            switch (currentState)
            {
                case Rule2State.FirstBlackout:
                    UpdateFirstBlackout();
                    break;
                
                case Rule2State.TurnOnLight:
                    UpdateTurnOnLight();
                    break;
                
                case Rule2State.FixPanel:
                    UpdateFixPanel();
                    break;

                case Rule2State.ReturnToSeat:
                    UpdateReturnToSeat();
                    break;

                case Rule2State.RadioWaiting:
                    UpdateRadioWaiting();
                    break;

                case Rule2State.GhostEvent:
                    UpdateGhostEvent();
                    break;
            }
        }

        public override void EndRule()
        {
            base.EndRule();
            ResetAll();
        }

        #endregion

        #region State Machine
        void SpawnRandomPanels()
        {
            // เคลียร์ของเก่า
            foreach (var p in activePanels)
            {
                if (p != null) Destroy(p.gameObject);
            }
            activePanels.Clear();

            // copy list
            List<Transform> shuffled = new List<Transform>(panelSpawnPoints);

            // shuffle
            for (int i = 0; i < shuffled.Count; i++)
            {
                Transform temp = shuffled[i];
                int rand = Random.Range(i, shuffled.Count);
                shuffled[i] = shuffled[rand];
                activePanelPoints.Add(shuffled[i]);
                shuffled[rand] = temp;
            }

            // เลือก 2 อันแรก
            for (int i = 0; i < 2; i++)
            {
                LightPanel panel = Instantiate(panelPrefab, shuffled[i].position, Quaternion.identity);
                panel.SetActiveLight(false); 
                panel.SetRule(this,inputHandler); // ผูก callback
                activePanels.Add(panel);
            }
        }

        void ChangeState(Rule2State newState)
        {
            currentState = newState;

            switch (newState)
            {
                case Rule2State.FirstBlackout:
                    StartFirstBlackout();
                    break;

                case Rule2State.SecondBlackout:
                    StartSecondBlackout();
                    break;
                case Rule2State.FixPanel:
                    StartPanelFixed();
                    break;
                case Rule2State.ReturnToSeat:
                    StartReturnToSeat();
                    break;
                case Rule2State.RadioWaiting:
                    StartRadioPhase();
                    break;

                case Rule2State.GhostEvent:
                    StartGhostEvent();
                    break;
            }
        }

        void ResetState()
        {
            fixedPanels = 0;
            fixedPanelSet.Clear();
            radioPlaying = false;
            ghostTriggered = false;
        }

        #endregion

        #region Phase 1

        void StartFirstBlackout()
        {
            // TODO: ปิดไฟ + mood
            saraLight.enabled = false;
            switchLight.UnlockSwitch(true);
            radioControl.UnlockRadio(true);
            PlayerController.Instance.isBlockStanding = true;
            TimeManager.instance.IsPauseTime(true);
            radioControl.UnlockRadio(true);
            StartCoroutine(AllowStandAfterSetup());
        }
        IEnumerator AllowStandAfterSetup()
        {
            yield return new WaitForSeconds(0.5f);

            PlayerController.Instance.isBlockStanding = false;
        }

        void UpdateFirstBlackout()
        {
            if (lightTurnedOn)
            {
                ChangeState(Rule2State.TurnOnLight);
            }
        }

        public void OnLightTurnedOn()
        {
            lightTurnedOn = true;
        }
        public void OnRadioTurnedOn()
        {
            radioPlaying = true;
        }

        #endregion

        #region Phase 2

        void UpdateTurnOnLight()
        {
            if (playerController.IsSitting() && radioPlaying)
            {
                radioControl.UnlockRadio(false);
                ChangeState(Rule2State.SecondBlackout);
                TimeManager.instance.IsPauseTime(false);
                PlayerController.Instance.isBlockStanding = true;
            }
        }

        #endregion

        #region Phase 3

        void StartSecondBlackout()
        {
            // TODO: ไฟกระพริบ + เสียงเพี้ยน
            StartCoroutine(SecondBlackoutCoroutine());
            
        }  
        
        private IEnumerator SecondBlackoutCoroutine()
        {
            yield return new WaitForSeconds(12f); // 2 minutes in game time 
            for (int i = 0; i < 9; i++)
            {
                
                saraLight.enabled = !saraLight.enabled;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
                
            }
            UpdateSecondBlackout();
        }

        void UpdateSecondBlackout()
        {
            TimeManager.instance.IsPauseTime(true);
            PlayerController.Instance.isBlockStanding = true;
            ChangeState(Rule2State.FixPanel);
            StartCoroutine(AllowStandAfterSetup());
        }

        #endregion

        #region Phase 4

        void CreateGhost()
        {
            Instantiate(ghostTestPrefab, activePanels[0].gameObject.transform.position + Vector3.right * 1.2f, Quaternion.identity);
        }
        void StartPanelFixed()
        {
            AudioManager.instance.StopRadio();
            StartCoroutine(RadioWaitingCoroutine());

            foreach (var panel in activePanels)
            {
                panel.SetActiveLight(true);
                panel.UnlockPanel(true);
            }
            CreateGhost();

        }

        void UpdateFixPanel()
        {
            // รอ event จาก panel (OnPanelFixed)
        }
        IEnumerator RadioWaitingCoroutine()
        {
            yield return new WaitForSeconds(6f);
            AudioManager.instance.StartWomanScream();
            AudioManager.instance.PlayRadioPrayer(1);
            yield return new WaitForSeconds(12f);
            AudioManager.instance.PlayRadioPrayer(0);


        }
        public void OnPanelFixed()
        {

            //fixedPanelSet.Add(panel);
            fixedPanels++;
            if (fixedPanels == 1)
            {
                AudioManager.instance.StopWomanScream();
            }
            if (fixedPanels >= 2)
            {
                HorrorTextUI.instance.HideText();
                switchLight.UnlockSwitch(true);
                runningGhost.SetActive(true);
                ChangeState(Rule2State.ReturnToSeat);
            }
        }

        #endregion

        #region Phase 5
        void StartReturnToSeat()
        {
        }

        void UpdateReturnToSeat()
        {
            if (playerController.IsSitting() && switchLight.IsLightOn())
            {
                runningGhost.SetActive(false);
                PlayerController.Instance.isBlockStanding = true;
                TimeManager.instance.IsPauseTime(false);
                ChangeState(Rule2State.RadioWaiting);
                HorrorTextUI.instance.HideText();
            }
        }

        #endregion

        #region Phase 6

        void StartRadioPhase()
        {
            radioPlaying = true;
            StartCoroutine(WaitBeforeGhost());
            //radioTimer = Random.Range(40,90);
            //AudioManager.instance.GoToNoise();
        }
        IEnumerator WaitBeforeGhost()
        {
            yield return new WaitForSeconds(6f);
            AudioManager.instance.StopPlayRadioPrayer();
            //AudioManager.instance.PlayRadioPrayer(2);
            ghostTriggered = true;
            ChangeState(Rule2State.GhostEvent);
        }
        void UpdateRadioWaiting()
        {
            //  ถ้าลุก → หยุดเวลา + เพิ่มความเครียด
            // if (!isSitting)
            // {
            //     //HeartbeatSystem.instance.AddStress(20f * Time.deltaTime);
            //     return;
            // }
            
        }

        #endregion

        #region Phase 7

        void StartGhostEvent()
        {
            // spawn ผี
            if (ghostPrefab != null)
            {
                currentGhost = Instantiate(ghostPrefab, ghostSpawnPoint.position, Quaternion.identity);

                GhostAI ai = currentGhost.GetComponent<GhostAI>();
                ai.point1 = ghostPointOutside;
                ai.point2 = ghostPointInside;
                ai.saraLight = saraLight;
                ai.Init(playerController.transform,this);
            }

            // TODO: เสียงหัวเราะ / ไฟกระพริบ
        }

        void UpdateGhostEvent()
        {
            if (!isSitting)
            {
                //HeartbeatSystem.instance.AddStress(30f * Time.deltaTime);
            }
            
            if (_isRadioStopped)
            {
                PlayerController.Instance.isBlockStanding = false;
                ChangeState(Rule2State.Completed);
            }
        }

        public void RadioStopped()
        {
            _isRadioStopped = true;
        }

        public void SpawnJumpScareGhost()
        {
            Instantiate(ghostJumpScarePrefab,jumpScarePoint.position, jumpScarePoint.rotation,jumpScarePoint);
        }

        #endregion

        #region End

        void ResetAll()
        {
           
        }

        #endregion

        #region Mock (คุณต้องไปผูกจริง)


        #endregion
    }
}