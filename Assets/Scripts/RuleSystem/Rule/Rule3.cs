using System.HeartbeatSystem;
using RuleSystem;
using UnityEngine;

public class Rule3 : RuleBase
{
    public Transform forbiddenTarget; // เงา
    [SerializeField] private float enterAngle = 20f;
    [SerializeField] private float exitAngle = 25f;
    private bool isLooking = false;

    private Transform playerCamera;

    public override void StartRule()
    {
        base.StartRule();
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
