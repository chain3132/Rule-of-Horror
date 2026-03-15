using System.HeartbeatSystem;
using RuleSystem;
using UnityEngine;

public class Rule1 : RuleBase
{
    void Update()
    {
        if (!ruleActive) return;

        // heartbeat logic
        AudioManager.instance.UpdateHeartbeat();
        // distance logic
        HeartbeatSystem.instance.CheckPlayerInsideZone();
    }
}
