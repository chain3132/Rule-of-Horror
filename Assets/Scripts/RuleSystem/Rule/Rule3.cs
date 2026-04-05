using System;
using System.Collections.Generic;
using System.HeartbeatSystem;
using RuleSystem;
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
    
    private List<int> correctOrder = new List<int> {1,2,3,4};

    private Transform playerCamera;

    private void Awake()
    {
        Instance = this;
    }

    public override void StartRule()
    {
        base.StartRule();
        paperSpawner.SpawnPapers();
        playerCamera = Camera.main.transform;
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
        if (number == correctOrder[currentIndex])
        {
            currentIndex++;

            if (currentIndex >= 4)
            {
                Debug.Log("SUCCESS");
            }
        }
        else
        {
            OnWrong();
        }
    }

    void OnWrong()
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
