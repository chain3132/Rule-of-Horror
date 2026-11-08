using System.Collections.Generic;
using Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// ให้ NPC ที่นั่งอยู่ในรถ (ทุกตัวที่อยู่ใต้ GameObject นี้) เล่นท่านั่ง Idle วนไปเรื่อยๆ
/// ใส่ไว้ที่ตัวรถ (Intro Bus, Bus 1 Trap, Bus 4 Loop ฯลฯ) แล้วลาก NPCSittingLoop ใส่ช่อง Sitting Clip
/// ข้ามคนขับ (ชื่อมีคำว่า Driver) และผู้เล่นให้เอง — ไม่ต้องมี Animator Controller
/// NPC ตัวไหนไม่มีคอมโพเนนต์ Animator จะใส่ให้เองตอนเริ่มเกม
///
/// clip นี้ทำจากโครงกระดูกของ NPC1_Man (รากชื่อ metarig.001) — NPC ตัวอื่นกระดูกชุดเดียวกันแต่ชื่อรากต่างกัน
/// (metarig / NPCmass1 ...) จึงเปลี่ยนชื่อรากให้ตรงก่อนเล่น
/// และคืนตำแหน่งกระดูกเดิมของแต่ละตัวทุกเฟรม (ใช้แค่การหมุนจาก clip) ไม่งั้นทุกตัวจะโดนดึงสัดส่วนให้เท่า NPC1_Man
/// </summary>
public class SeatedPassengers : MonoBehaviour
{
    [Tooltip("ท่านั่ง Idle — NPCSittingLoop")]
    [SerializeField] private AnimationClip sittingClip;

    [Header("ข้าม")]
    [Tooltip("ข้าม NPC ที่ชื่อมีคำเหล่านี้ (คนขับ)")]
    [SerializeField] private string[] skipNameContains = { "Driver" };
    [Tooltip("ข้าม NPC ตัวนี้เพิ่มเติม")]
    [SerializeField] private List<GameObject> skip = new List<GameObject>();

    [Header("ท่าทาง")]
    [Tooltip("สุ่มจุดเริ่มของท่าแต่ละตัว ไม่ให้ขยับพร้อมกันทั้งคัน")]
    [SerializeField] private bool randomizeStart = true;
    [Tooltip("สุ่มความเร็วท่าแต่ละตัวในช่วงนี้")]
    [SerializeField] private Vector2 speedRange = new Vector2(0.9f, 1.1f);
    [Tooltip("คงสัดส่วนร่างของแต่ละตัว — ใช้แค่การหมุนกระดูกจาก clip (สะโพกยังขยับตาม clip)")]
    [SerializeField] private bool keepOwnProportions = true;

    [Header("โครงกระดูก")]
    [Tooltip("ชื่อกระดูกรากที่ clip อ้างถึง")]
    [SerializeField] private string clipRigRootName = "metarig.001";
    [Tooltip("ชื่อกระดูกสะโพก (ลูกตัวแรกของราก) ใช้หาว่ากระดูกรากคืออันไหน")]
    [SerializeField] private string hipsBoneName = "spine";

    private class Passenger
    {
        public Animator animator;
        public bool ready;
        public PlayableGraph graph;
        public AnimationClipPlayable playable;

        public Transform rigRoot;
        public Transform hips;
        public Vector3 rootPos, rootScale;
        public Quaternion rootRot;
        public Transform[] bones;
        public Vector3[] restPos, restScale;
    }

    private readonly List<Passenger> _passengers = new List<Passenger>();

    private void Awake()
    {
        if (sittingClip == null)
        {
            Debug.LogWarning("[SeatedPassengers] ยังไม่ได้ใส่ Sitting Clip", this);
            enabled = false;
            return;
        }

        // หา NPC จากโครงกระดูก: กระดูก "spine" → พ่อคือกระดูกราก (metarig...) → พ่อถัดไปคือตัว NPC
        // โมเดล NPC ส่วนใหญ่ไม่มี Animator มาเลย จึงใส่ให้เองตรงนี้ (ไม่ต้องมี Controller)
        // เก็บรายชื่อตั้งแต่แรก รวมตัวที่ปิดอยู่ — ไปเริ่มเล่นจริงตอนตัวนั้นเปิด (ดู LateUpdate)
        foreach (var bone in GetComponentsInChildren<Transform>(true))
        {
            if (bone.name != hipsBoneName) continue;

            var rigRoot = bone.parent;
            var npc = rigRoot != null ? rigRoot.parent : null;
            if (npc == null || npc == transform) continue;
            if (ShouldSkip(npc)) continue;

            var animator = npc.GetComponent<Animator>();
            if (animator == null) animator = npc.gameObject.AddComponent<Animator>();

            _passengers.Add(new Passenger { animator = animator, rigRoot = rigRoot, hips = bone });
        }
    }

    private void LateUpdate()
    {
        float length = sittingClip.length;

        foreach (var p in _passengers)
        {
            if (p.animator == null) continue;

            if (!p.ready)
            {
                if (p.animator.isActiveAndEnabled) Setup(p);
                continue;
            }

            // clip ไม่ได้ติ๊ก Loop Time — วนเอง
            if (!sittingClip.isLooping && length > 0f)
            {
                double t = p.playable.GetTime();
                if (t >= length) p.playable.SetTime(t % length);
            }

            if (keepOwnProportions) RestoreProportions(p);
        }
    }

    private void OnDestroy()
    {
        foreach (var p in _passengers)
            if (p.graph.IsValid()) p.graph.Destroy();
    }

    private void Setup(Passenger p)
    {
        p.ready = true;

        // จำท่าเดิมไว้ก่อน clip จะเขียนทับ
        p.rootPos   = p.rigRoot.localPosition;
        p.rootRot   = p.rigRoot.localRotation;
        p.rootScale = p.rigRoot.localScale;
        p.bones     = p.rigRoot.GetComponentsInChildren<Transform>(true);
        p.restPos   = new Vector3[p.bones.Length];
        p.restScale = new Vector3[p.bones.Length];
        for (int i = 0; i < p.bones.Length; i++)
        {
            p.restPos[i]   = p.bones[i].localPosition;
            p.restScale[i] = p.bones[i].localScale;
        }

        // ชื่อรากต้องตรงกับใน clip ถึงจะจับกระดูกได้
        if (p.rigRoot.name != clipRigRootName)
        {
            p.rigRoot.name = clipRigRootName;
            p.animator.Rebind();
        }
        p.animator.applyRootMotion = false;

        p.graph = PlayableGraph.Create($"{p.animator.name} Sitting");
        p.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        var output = AnimationPlayableOutput.Create(p.graph, "Sitting", p.animator);
        p.playable = AnimationClipPlayable.Create(p.graph, sittingClip);
        p.playable.SetApplyFootIK(false);
        output.SetSourcePlayable(p.playable);

        if (randomizeStart) p.playable.SetTime(Random.value * sittingClip.length);
        p.playable.SetSpeed(Random.Range(speedRange.x, speedRange.y));

        p.graph.Play();
    }

    // clip เขียนตำแหน่ง/สเกลกระดูกของ NPC1_Man มาด้วย — คืนค่าของตัวเองหลัง Animator ทำงาน (LateUpdate มาหลัง Animator)
    private static void RestoreProportions(Passenger p)
    {
        p.rigRoot.localPosition = p.rootPos;
        p.rigRoot.localRotation = p.rootRot;
        p.rigRoot.localScale    = p.rootScale;

        for (int i = 0; i < p.bones.Length; i++)
        {
            var bone = p.bones[i];
            if (bone == p.rigRoot) continue;
            if (bone != p.hips) bone.localPosition = p.restPos[i]; // สะโพกขยับตามจังหวะหายใจของ clip
            bone.localScale = p.restScale[i];
        }
    }

    private bool ShouldSkip(Transform npc)
    {
        if (npc.GetComponentInParent<PlayerController>(true) != null) return true;

        for (var t = npc; t != null && t != transform; t = t.parent)
        {
            if (skip.Contains(t.gameObject)) return true;
            foreach (var word in skipNameContains)
                if (!string.IsNullOrEmpty(word) && t.name.Contains(word)) return true;
        }
        return false;
    }
}
