using System.HeartbeatSystem;
using RuleSystem;
using UnityEngine;

public class Rule2 : RuleBase
{
    public Transform forbiddenTarget; // เงา
    public float lookTriggerAngle = 20f;

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

        // 👁️ เริ่มดูดเมื่อ "เริ่มหันไปทางมัน"
        if (angle < lookTriggerAngle)   
        {
            LookDistortionSystem.Instance.StartPull(forbiddenTarget);
        }
        else
        {
            LookDistortionSystem.Instance.StopPull();
        }
    }

    public override void EndRule()
    {
        base.EndRule();

        LookDistortionSystem.Instance.StopPull();
    }
}
