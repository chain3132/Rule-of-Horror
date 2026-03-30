using System;
using System.Collections;
using System.Collections.Generic;
using System.HeartbeatSystem;
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

        private List<LightPanel> activePanels = new List<LightPanel>();

        [Header("Ghost")]
        public GameObject ghostPrefab;
        private GameObject currentGhost;
        [SerializeField] private Transform ghostSpawnPoint;
        [SerializeField] private Transform ghostPointOutside;
        [SerializeField] private Transform ghostPointInside;
        
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

            ResetState();
            SpawnRandomPanels();
            //ChangeState(Rule2State.FirstBlackout);
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
                int rand = UnityEngine.Random.Range(i, shuffled.Count);
                shuffled[i] = shuffled[rand];
                shuffled[rand] = temp;
            }

            // เลือก 2 อันแรก
            for (int i = 0; i < 2; i++)
            {
                LightPanel panel = Instantiate(panelPrefab, shuffled[i].position, shuffled[i].rotation);
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
            TimeManager.instance.IsPauseTime(true);
            radioControl.UnlockRadio(true);
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
                Debug.Log("Go to second blackout");
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
            ChangeState(Rule2State.FixPanel);
        }

        #endregion

        #region Phase 4

        void StartPanelFixed()
        {
            foreach (var panel in activePanels)
            {
                panel.SetActiveLight(true);
                panel.UnlockPanel(true);
            }
        }

        void UpdateFixPanel()
        {
            // รอ event จาก panel (OnPanelFixed)
        }

        public void OnPanelFixed()
        {

            //fixedPanelSet.Add(panel);
            fixedPanels++;
            if (fixedPanels >= 2)
            {
                switchLight.UnlockSwitch(true);
                ChangeState(Rule2State.ReturnToSeat);
            }
        }

        #endregion

        #region Phase 5

        void UpdateReturnToSeat()
        {
            if (playerController.IsSitting() && switchLight.IsLightOn())
            {
                ChangeState(Rule2State.RadioWaiting);
            }
        }

        #endregion

        #region Phase 6

        void StartRadioPhase()
        {
            radioPlaying = true;
            radioTimer = 12f;
            AudioManager.instance.GoToNoise();
            // TODO: เปิดวิทยุ
        }

        void UpdateRadioWaiting()
        {
            //  ถ้าลุก → หยุดเวลา + เพิ่มความเครียด
            // if (!isSitting)
            // {
            //     //HeartbeatSystem.instance.AddStress(20f * Time.deltaTime);
            //     return;
            // }

            radioTimer -= Time.deltaTime;

            // 👻 trigger ghost ครั้งเดียว
            if (!ghostTriggered && radioTimer <= 10f)
            {
                ghostTriggered = true;
                ChangeState(Rule2State.GhostEvent);
            }
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
                ChangeState(Rule2State.Completed);
            }
        }

        public void RadioStopped()
        {
            _isRadioStopped = true;
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