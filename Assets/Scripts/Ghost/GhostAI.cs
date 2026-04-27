using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.AI;

public class GhostAI : MonoBehaviour
{
    public enum GhostState
    {
        MovingToPoint1,
        IdleAtPoint1,
        TurningToPlayer,
        MovingToPoint2,
        ReachedFinal
    }

    private NavMeshAgent agent;
    private Transform player;
    private Animator animator;
    private RuleSystem.Rule.Rule2 rule2;

    [Header("Points")]
    public Transform point1; // หน้าศาลา
    public Transform point2; // ในศาลา

    [Header("Settings")]
    public float stopDistance = 1.5f;
    public float turnSpeed = 2f;

    private GhostState currentState;

    private bool isTurning;
    private RuleSystem.Rule.Rule2 currentRule;
    
    private EventInstance ghostWalk;
    private EventInstance brokenBone;



    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

    }

    void Start()
    {
        ghostWalk = RuntimeManager.CreateInstance("event:/GhostWalk");
        brokenBone = RuntimeManager.CreateInstance("event:/BoneBroken");

        ghostWalk.start();

    }
    

    public void Init(Transform playerTarget,RuleSystem.Rule.Rule2 rule2)
    {
        player = playerTarget;
        currentRule = rule2;
        currentState = GhostState.MovingToPoint1;
        agent.SetDestination(point1.position);
    }

    void Update()
    {
        ghostWalk.set3DAttributes(
            FMODUnity.RuntimeUtils.To3DAttributes(transform));
        switch (currentState)
        {
            case GhostState.MovingToPoint1:
                UpdateMoveToPoint1();
                break;

            case GhostState.IdleAtPoint1:
                break;

            case GhostState.TurningToPlayer:
                UpdateTurning();
                break;

            case GhostState.MovingToPoint2:
                UpdateMoveToPoint2();
                break;
        }
    }
    public void PlayBrokenBoneSound()
    {
        brokenBone.start();
    }
    #region Phase 1

    void UpdateMoveToPoint1()
    {
        float dist = Vector3.Distance(transform.position, point1.position);
        animator.SetBool("isWalk", true);


        if (dist <= stopDistance)
        {
            agent.isStopped = true;
            animator.SetBool("isWalk", false);
            ghostWalk.setVolume(0);
            ChangeState(GhostState.IdleAtPoint1);

            StartCoroutine(IdleThenTurn());
        }
    }

    IEnumerator IdleThenTurn()
    {
        // 👉 ยืนเฉย ๆ ก่อน
        yield return new WaitForSeconds(2f);

        ChangeState(GhostState.TurningToPlayer);
    }

    #endregion

    #region Turning

    void UpdateTurning()
    {
        if (player == null) return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0;

        Quaternion targetRot = Quaternion.LookRotation(dir);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            Time.deltaTime * turnSpeed
        );

        float angle = Quaternion.Angle(transform.rotation, targetRot);

        if (angle < 5f)
        {
            ChangeState(GhostState.MovingToPoint2);

            agent.isStopped = false;
            agent.SetDestination(point2.position);

            // 🔥 เริ่มไฟกระพริบ
            StartFlicker();
        }
    }

    #endregion

    #region Phase 2

    void UpdateMoveToPoint2()
    {
        float dist = Vector3.Distance(transform.position, point2.position);
        agent.speed = 1.5f;
        if (dist <= stopDistance)
        {
            agent.isStopped = true;
            agent.enabled = false;
            agent.updatePosition = false;
            agent.updateRotation = false;
            ChangeState(GhostState.ReachedFinal);
            StopFlicker();
            OnReachedFinal();
            
        }
    }
    IEnumerator OnReachedFinalRoutine()
    {
        ghostWalk.setVolume(0);
        animator.SetBool("isWalk", false);
        yield return new WaitForSeconds(2f);
        animator.applyRootMotion = true;
        agent.enabled = false;
        animator.SetTrigger("Vecna");
        yield return new WaitForSeconds(13f);
        currentRule.SpawnJumpScareGhost();
        Destroy(gameObject,0.5f);
        
    }

    #endregion

    #region Effects

    public Light saraLight;

    private Coroutine flickerRoutine;

    void StartFlicker()
    {
        if (flickerRoutine != null) StopCoroutine(flickerRoutine);
        flickerRoutine = StartCoroutine(FlickerLight());
    }

    void StopFlicker()
    {
        if (flickerRoutine != null)
        {
            StopCoroutine(flickerRoutine);
            saraLight.enabled = true;
        }
    }

    IEnumerator FlickerLight()
    {
        while (true)
        {
            saraLight.enabled = !saraLight.enabled;
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
        }
    }

    #endregion

    #region Final

    void OnReachedFinal()
    {
        // 👉 moment หลอน
        // - หัวหัก
        saraLight.enabled = true;
       //AudioManager.instance.StopNoise();
       StartCoroutine(OnReachedFinalRoutine());
       //currentRule.RadioStopped();
       //
        // - เสียง
        // - จ้องผู้เล่น
    }

    #endregion

    void ChangeState(GhostState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case GhostState.MovingToPoint1:
            case GhostState.MovingToPoint2:
                ghostWalk.setVolume(1);
                animator.SetBool("isWalk", true);
                break;

            case GhostState.IdleAtPoint1:
                animator.SetBool("isWalk", false);
                break;
        }
    }
}
