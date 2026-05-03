using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

public class TriggerGhostRun : MonoBehaviour
{
    public EventInstance ghostRun;

    [SerializeField] float duration = 1.5f; // เวลาที่เสียงวิ่ง
    [SerializeField] float distance = 2f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private float angle;
    private float t = 0f;
    private bool isPlaying = false;
    private void Start()
    {
        ghostRun = RuntimeManager.CreateInstance("event:/RunningGhost");
        ghostRun.set3DAttributes(
            RuntimeUtils.To3DAttributes(transform));
        t = 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        ghostRun.start();
        isPlaying = true;
    }

    // Update is called once per frame
    void Update()
    {

        if (!isPlaying) return;

        t += Time.deltaTime / duration;

        //  เริ่มจาก "ขวา"
        Vector3 start = AudioManager.instance.player.position + AudioManager.instance.player.right * distance;

        //  ไป "หลังซ้าย"
        Vector3 end = AudioManager.instance.player.position + (-AudioManager.instance.player.right - AudioManager.instance.player.forward).normalized * distance;

        //  lerp ตำแหน่ง
        Vector3 pos = Vector3.Lerp(start, end, t);

        ghostRun.set3DAttributes(
            RuntimeUtils.To3DAttributes(pos)
        );

        // จบ
        if (t >= 1f)
        {
            ghostRun.stop(STOP_MODE.ALLOWFADEOUT);
            ghostRun.release();
            isPlaying = false;
        }
    }
}
