using Manager;
using RuleSystem;
using UnityEngine;

public class RuleManager : MonoBehaviour
{
    public RuleBase[] rules;


    private void OnEnable()
    {
        TimeManager.OnTimeChanged += CheckRules;
    }

    private void OnDisable()
    {
        TimeManager.OnTimeChanged -= CheckRules;
    }

    void CheckRules(int hour, int minute)
    {
        int currentTime = hour * 60 + minute;

        foreach (var rule in rules)
        {
            int startTime = rule.startHour * 60 + rule.startMinute;
            int endTime = rule.endHour * 60 + rule.endMinute;

            // เริ่มกฎ
            if (!rule.ruleActive && currentTime >= startTime && currentTime < endTime)
            {
                GameModeController.instance.BlinkToMode(GameMode.Tension);
                rule.StartRule();
            }

            // จบกฎ
            if (rule.ruleActive && currentTime >= endTime)
            {
                GameModeController.instance.BlinkToMode(GameMode.Relax);
                rule.EndRule();
            }
        }
    }
    // public bool CheckTime(int hour, int minute)
    // {
    //     
    //     
    // }
}
