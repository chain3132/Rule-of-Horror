using System.HeartbeatSystem;
using RuleSystem;
using UnityEngine;

public class Rule1 : RuleBase
{
    protected override void UpdateRule()
    {
        // heartbeat logic
        AudioManager.instance.UpdateHeartbeat();
        // distance logic
        HeartbeatSystem.instance.CheckPlayerInsideZone();
        
    }
}
