using System;
using System.Collections.Generic;
using System.HeartbeatSystem;
using System.Linq;
using RuleSystem;
using TMPro;
using UnityEngine;

public class Rule3 : RuleBase
{
    public static Rule3 Instance;
    public Transform forbiddenTarget; // เงา
    [SerializeField] private float enterAngle = 20f;
    [SerializeField] private float exitAngle = 25f;
    [SerializeField] PaperSpawner paperSpawner;
    
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
        List<Paper> papers = paperSpawner.SpawnPapers();
        correctOrder = papers
            .Select(p => p.number)
            .OrderBy(n => n)
            .ToList();
        playerCamera = Camera.main.transform;
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
        HeartbeatSystem.instance.CheckPlayerInsideZone();
        CheckLookAtForbidden();
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
        //AudioManager.instance.IncreaseHeartbeatLevel();
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

        LookDistortionSystem.Instance.StopPull();
    }
}
