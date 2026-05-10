using System;
using RuleSystem.Rule;
using UnityEngine;

public class SoundRandom : MonoBehaviour
{
    [SerializeField] private RuleSystem.Rule.Rule2 rule2;
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && rule2.currentState == Rule2State.FixPanel)
        {
            Debug.Log("Player exited trigger, starting ambience");
            rule2.StartCompleteAmbience();
        }
    }
}
