using Enum;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// ผลของรางวัล "วิทยุ"
///
///   ยังไม่ได้เลือก     : เงียบ
///   เลือกแล้ว + Relax  : เพลงผ่อนคลาย (event:/Radio/Release Radio)
///   เลือกแล้ว + Tension: เสียงผิดเพี้ยน (event:/Radio/Tension Radio)
///
/// ทั้งสอง event เป็น loop + 3D ใน FMOD — เสียงออกจากตัววิทยุ เดินห่างแล้วเบาลงเอง
/// (Max Distance ตั้งใน FMOD ตอนนี้ 20 ม.)
///
/// สลับโหมด = ค่อยๆ หยุดตัวเก่า (ALLOWFADEOUT) แล้วเริ่มตัวใหม่ ไม่ตัดฉับ
/// และถ้าโหมดเดิมเล่นอยู่แล้วจะไม่เริ่มใหม่ — Apply ถูกเรียกซ้ำได้ เพลงต้องไม่กระตุกกลับไปต้นเพลง
///
/// วิธีวาง: Rule of Horror ▸ Reward ▸ Add Radio Reward Sound ใส่ตัวนี้ให้ที่วิทยุในศาลาเอง
/// </summary>
public class RewardRadio : RewardEffect
{
    [Header("เสียง")]
    [SerializeField] private string relaxEvent   = "event:/Radio/Release Radio";
    [SerializeField] private string tensionEvent = "event:/Radio/Tension Radio";

    [Tooltip("จุดที่เสียงออก — เว้นว่าง = ตัวนี้เอง")]
    [SerializeField] private Transform speaker;

    private EventInstance _relax;
    private EventInstance _tension;
    private bool          _created;
    private bool          _relaxOk;
    private bool          _tensionOk;

    private void Reset() => kind = RewardKind.Radio;

    protected override void Apply(bool owned, GameMode mode)
    {
        if (!Application.isPlaying) return;
        EnsureInstances();

        SetPlaying(_relax,   _relaxOk,   owned && mode == GameMode.Relax);
        SetPlaying(_tension, _tensionOk, owned && mode == GameMode.Tension);
    }

    private void Update()
    {
        // วิทยุอาจถูกยก/ย้ายได้ (เช่นตอนเริ่มคืนใหม่) — ตามตำแหน่งทุกเฟรมถูกกว่ามาก
        if (!_created) return;

        var attributes = RuntimeUtils.To3DAttributes(speaker != null ? speaker : transform);
        if (_relaxOk)   _relax.set3DAttributes(attributes);
        if (_tensionOk) _tension.set3DAttributes(attributes);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (!_created) return;

        // ปิดวิทยุ (ทั้งตัวหรือ component) แล้วเสียงต้องเงียบด้วย ไม่งั้นดังค้างจากที่ว่าง
        SetPlaying(_relax,   _relaxOk,   false);
        SetPlaying(_tension, _tensionOk, false);
    }

    private void OnDestroy()
    {
        if (!_created) return;

        if (_relaxOk)   { _relax.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);   _relax.release(); }
        if (_tensionOk) { _tension.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); _tension.release(); }
        _created = false;
    }

    private void EnsureInstances()
    {
        if (_created) return;
        _created = true;

        _relaxOk   = TryCreate(relaxEvent,   out _relax);
        _tensionOk = TryCreate(tensionEvent, out _tension);
        Update();
    }

    private static void SetPlaying(EventInstance instance, bool ok, bool on)
    {
        if (!ok) return;

        instance.getPlaybackState(out PLAYBACK_STATE state);
        bool playing = state == PLAYBACK_STATE.PLAYING
                    || state == PLAYBACK_STATE.STARTING
                    || state == PLAYBACK_STATE.SUSTAINING;

        if (on && !playing)  instance.start();
        if (!on && playing)  instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    private bool TryCreate(string path, out EventInstance instance)
    {
        instance = default;
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            instance = RuntimeManager.CreateInstance(path);
            return true;
        }
        catch (EventNotFoundException)
        {
            Debug.LogWarning($"[Reward] ยังไม่มี event '{path}' ใน FMOD — วิทยุเงียบไปก่อน", this);
            return false;
        }
    }
}
