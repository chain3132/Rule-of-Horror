using System;
using System.Collections;
using System.Collections.Generic;
using System.HeartbeatSystem;
using System.Linq;
using Manager;
using Player;
using RuleSystem;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class Rule3 : RuleBase
{
    public static Rule3 Instance;
    public Transform forbiddenTarget; // เงา
    [SerializeField] private float enterAngle = 20f;
    [SerializeField] private float exitAngle = 25f;
    [SerializeField] PaperSpawner paperSpawner;
    [SerializeField] Transform[] ghostSpawnPoints;
    [SerializeField] private GameObject ghost;
    
    private bool isLooking = false;
    private int currentIndex = 0;
    private int ghostIndex = 0;
    
    [SerializeField]private List<int> correctOrder = new List<int>();
    private Transform playerCamera;
    
    private int wrongCount = 0;
    private List<Paper> realPapers;
    private List<Paper> fakePapers;
    [SerializeField] private int maxWrong = 3;
    
    private GameObject ghostInstance;
    private Animator ghostAnimator;

    private Coroutine heartbeatEscalationCoroutine;

    private void Awake()
    {
        Instance = this;
    }
    
    public override void StartRule()
    {
        base.StartRule();
        StartCoroutine(RuleFlow());
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
        // 🪑 1. รอให้ผู้เล่น "นั่งก่อน"
        yield return new WaitUntil(() => PlayerIsSitting());
        GameModeController.instance.BlinkToMode(GameMode.Tension);
        PlayerController.Instance.isBlockStanding = true;
        yield return new WaitUntil(() => PlayerEyesOpened());
        // 3. เริ่มกฎจริง
        StartGameplay();
    }
    void StartGameplay()
    {
        
        var result = paperSpawner.SpawnPapers();

        realPapers = result.realPapers;
        fakePapers = result.fakePapers;

        correctOrder = realPapers
            .Select(p => p.number)
            .OrderBy(n => n)
            .ToList();
        playerCamera = Camera.main.transform;
        ghostIndex = 0;
        ghostInstance = Instantiate(ghost, ghostSpawnPoints[0].position, Quaternion.Euler(0,90,0));
        ghostAnimator = ghostInstance.GetComponent<Animator>();

        LightFlickerSystem.Instance.StartAmbientFlicker();
        AudioManager.instance.SetHeartbeatLevel(1);
        heartbeatEscalationCoroutine = StartCoroutine(HeartbeatEscalationRoutine());

    }
    IEnumerator HeartbeatEscalationRoutine()
    {
        yield return new WaitForSeconds(10f);
        AudioManager.instance.IncreaseHeartbeatLevel(); // → level 2

        yield return new WaitForSeconds(30f);
        AudioManager.instance.IncreaseHeartbeatLevel(); // → level 3
    }
    
    public bool CheckAnswer(int number)
    {
        if (currentIndex >= correctOrder.Count) return false;

        return number == correctOrder[currentIndex];
    }
    protected override void UpdateRule()
    {
        // heartbeat logic
        AudioManager.instance.UpdateHeartbeat();
        // distance logic
        //HeartbeatSystem.instance.CheckPlayerInsideZone();
        //CheckLookAtForbidden();
    }
    
    
    public void OnPaperSelected(Paper selectedPaper)
    {
        if (currentIndex >= correctOrder.Count) return;
        currentIndex++;
        realPapers.Remove(selectedPaper);
        AudioManager.instance.DecreaseHeartbeatLevel();

        LightFlickerSystem.Instance.PlayImpactFlicker();
        
        bool isLast = currentIndex >= correctOrder.Count;
        StartCoroutine(OnCorrectRoutine(isLast));
    }
    IEnumerator OnCorrectRoutine(bool isLast)
    {
        yield return new WaitForSeconds(0.3f); // รอช่วงไฟดับ

        // ย้ายผีไปตำแหน่งถัดไปแล้ว trigger Sit
        ghostIndex++;
        if (ghostInstance != null && ghostIndex < ghostSpawnPoints.Length)
        {
            ghostInstance.transform.position = ghostSpawnPoints[ghostIndex].position;
            ghostInstance.transform.rotation = ghostSpawnPoints[ghostIndex].rotation;
        }

        if (ghostAnimator != null)
            Debug.Log("Trigger ghost Sit animation");
            ghostAnimator.SetTrigger("Sit");

        if (isLast)
        {
            // รอหลังไฟดับครั้งสุดท้ายสักแป๊บ
            yield return new WaitForSeconds(1.5f);
            CleanupAndEnd();
        }
        else
        {
            // Shuffle กระดาษใหม่
            StartCoroutine(paperSpawner.ShuffleRoutine(realPapers, fakePapers));
        }
    }
    void CleanupAndEnd()
    {
        // ✅ Destroy ผี
        if (ghostInstance != null) Destroy(ghostInstance);

        // ✅ Destroy กระดาษทั้งหมด
        foreach (var p in realPapers) if (p != null) Destroy(p.gameObject);
        foreach (var p in fakePapers) if (p != null) Destroy(p.gameObject);
        realPapers.Clear();
        fakePapers.Clear();

        EndRule();
    }
    public void OnWrong()
    {
        wrongCount++;
        Debug.Log($"Wrong! Count: {wrongCount}");
        AudioManager.instance.IncreaseHeartbeatLevel();
    }

    void CheckLookAtForbidden()
    {
        Vector3 dirToTarget = (forbiddenTarget.position - playerCamera.position).normalized;
        float angle = Vector3.Angle(playerCamera.forward, dirToTarget);

        if (!isLooking && angle < enterAngle)
        {
            isLooking = true;
            LookDistortionSystem.Instance.StartPull(forbiddenTarget);
        }
        else if (isLooking && angle > exitAngle)
        {
            isLooking = false;
            LookDistortionSystem.Instance.StopPull();
        }
    }

    public override void EndRule()
    {
        base.EndRule();
        // หยุด escalation coroutine
        if (heartbeatEscalationCoroutine != null)
            StopCoroutine(heartbeatEscalationCoroutine);
        
        TimeManager.instance.SetTime(21,40);
        TimeManager.instance.IsPauseTime(false);
        LightFlickerSystem.Instance.StopAmbientFlicker();
        GameModeController.instance.BlinkToMode(GameMode.Relax);
        PlayerController.Instance.isBlockStanding = false;
        AudioManager.instance.StopHeartbeat();
        LookDistortionSystem.Instance.StopPull();
    }
}
