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
            CheckRule(rule, currentTime);
        }
    }
    
    void CheckRule(RuleBase rule, int currentTime)
    {
        int startTime = rule.startHour * 60 + rule.startMinute;
        int endTime = rule.endHour * 60 + rule.endMinute;

        if (!rule.ruleActive && currentTime >= startTime && currentTime < endTime)
        {
            rule.StartRule();
        }

        if (rule.ruleActive)
        {
            rule.UpdateRule();
        }

        if (rule.ruleActive && currentTime >= endTime)
        {
            rule.EndRule();
        }
    }
}
