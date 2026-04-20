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

public class Rule3 : RuleBase
{
    public static Rule3 Instance;
    public Transform forbiddenTarget; // เงา
    [SerializeField] private float enterAngle = 20f;
    [SerializeField] private float exitAngle = 25f;
    [SerializeField] PaperSpawner paperSpawner;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] private GameObject ghost;
    
    private bool isLooking = false;
    
    private int currentIndex = 0;
    
    [SerializeField]private List<int> correctOrder = new List<int>();
    private Transform playerCamera;
    
    private int wrongCount = 0;
    [SerializeField] private int maxWrong = 3;

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
        
        List<Paper> papers = paperSpawner.SpawnPapers();
        correctOrder = papers
            .Select(p => p.number)
            .OrderBy(n => n)
            .ToList();
        playerCamera = Camera.main.transform;
        Object ghostObj = Instantiate(ghost, spawnPoints[0].position, Quaternion.Euler(0,90,0));
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
    public void OnPaperSelected(int number)
    {
        if (currentIndex >= correctOrder.Count) return;
        currentIndex++;

        if (currentIndex >= correctOrder.Count)
        {
            Debug.Log("SUCCESS");
        }
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
        GameModeController.instance.BlinkToMode(GameMode.Relax);
        PlayerController.Instance.isBlockStanding = false;
        AudioManager.instance.StopHeartbeat();
        LookDistortionSystem.Instance.StopPull();
    }
}
