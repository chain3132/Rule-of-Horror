using UnityEngine;

namespace RuleSystem
{
    public abstract class RuleBase : MonoBehaviour
    {
        public int startHour;
        public int startMinute;

        public int endHour;
        public int endMinute;

        public bool ruleActive;

        public virtual void StartRule()
        {
            ruleActive = true;
        }

        public virtual void EndRule()
        {
            ruleActive = false;
        }

        public virtual void UpdateRule() { }
    }
}
