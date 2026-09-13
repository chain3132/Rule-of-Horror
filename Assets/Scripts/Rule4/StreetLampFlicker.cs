using System.Collections;
using UnityEngine;

namespace Rule4
{
    /// <summary>
    /// เสาไฟกระพริบตลอด Rule 4 — เริ่มด้วยชุดแรงตอนกฎเริ่ม แล้วกระพริบเป็นพักๆ ไปจนกว่า Stop()
    ///
    /// แยกจาก LightFlickerSystem ของ Rule 3 เพราะอันนั้น hard-code intensity 13 ให้ทุกดวง
    /// ส่วนอันนี้จำค่าเดิมของแต่ละดวงแยกกัน (เสาไฟแต่ละต้นสว่างไม่เท่ากัน) แล้วคืนให้ตอนจบ
    ///
    /// วงจร: Play() → [ชุดเปิดตัว] → วนซ้ำ { สงบ → กระพริบสั้นๆ } → Stop() คืนค่าเดิม
    /// </summary>
    public class StreetLampFlicker : MonoBehaviour
    {
        [Tooltip("ไฟของเสาไฟทุกต้นที่อยากให้กระพริบ")]
        [SerializeField] private Light[] lamps;

        [Header("ชุดเปิดตัว (ตอนกฎเริ่ม)")]
        [Tooltip("กระพริบแรงต่อเนื่องกี่วินาทีตอนเริ่มกฎ")]
        [SerializeField] private float introDuration = 3f;

        [Tooltip("หลังชุดเปิดตัว ดับสนิททิ้งไว้ก่อนกลับมาติด (0 = ไม่ต้อง)")]
        [SerializeField] private float introBlackout = 0.6f;

        [Header("ระหว่างกฎ (วนไปจนจบ)")]
        [Tooltip("ช่วงสงบระหว่างการกระพริบแต่ละครั้ง (สุ่ม min–max วินาที)")]
        [SerializeField] private float calmMin = 3f;
        [SerializeField] private float calmMax = 9f;

        [Tooltip("กระพริบแต่ละครั้งนานเท่าไร (สุ่ม min–max วินาที)")]
        [SerializeField] private float burstMin = 0.3f;
        [SerializeField] private float burstMax = 1.2f;

        [Header("จังหวะ / หน้าตา")]
        [Tooltip("ช่วงเวลาระหว่างการเปลี่ยนสถานะแต่ละครั้งในชุดกระพริบ (สุ่ม min–max)")]
        [SerializeField] private float minStep = 0.04f;
        [SerializeField] private float maxStep = 0.18f;

        [Tooltip("โอกาสที่แต่ละ step จะ 'ดับ' ไปเลย (0–1) — ที่เหลือจะแค่หรี่")]
        [Range(0f, 1f)]
        [SerializeField] private float blackoutChance = 0.35f;

        [Tooltip("ช่วง intensity ตอนหรี่ คิดเป็นสัดส่วนของค่าเดิม (0.2 = หรี่เหลือ 20%)")]
        [SerializeField] private float dimMin = 0.15f;
        [SerializeField] private float dimMax = 0.7f;

        private float[]   _origIntensity;
        private bool[]    _origEnabled;
        private Coroutine _routine;

        /// <summary>true = กำลังกระพริบอยู่ (ยังไม่ถูก Stop)</summary>
        public bool IsRunning => _routine != null;

        /// <summary>เริ่มกระพริบและวนไปเรื่อยๆ จนกว่าจะ Stop() — เรียกซ้ำจะเริ่มใหม่โดยไม่ทำค่าเดิมเพี้ยน</summary>
        public void Play()
        {
            if (lamps == null || lamps.Length == 0) return;

            Stop();                 // คืนค่าเดิมก่อน ไม่งั้นจะจำค่าที่กำลังกระพริบอยู่เป็น "ค่าเดิม"
            CacheOriginals();
            _routine = StartCoroutine(FlickerLoop());
        }

        /// <summary>หยุดทันทีและคืนไฟทุกดวงเป็นค่าเดิม — ปลอดภัยที่จะเรียกตอนไม่ได้กระพริบ</summary>
        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            RestoreOriginals();
        }

        private void OnDisable() => Stop();

        // ─────────────────────────── Routine ───────────────────────────

        private IEnumerator FlickerLoop()
        {
            // ชุดเปิดตัว — แรงและยาวกว่าปกติ ให้รู้ว่า "กฎเริ่มแล้ว"
            yield return Burst(introDuration);

            if (introBlackout > 0f)
            {
                SetAllEnabled(false);
                yield return new WaitForSeconds(introBlackout);
            }
            RestoreLook();

            // วนกระพริบเป็นพักๆ จนกว่า Rule4 จะสั่ง Stop() ตอนจบกฎ
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(calmMin, calmMax));
                yield return Burst(Random.Range(burstMin, burstMax));
                RestoreLook();
            }
        }

        /// <summary>กระพริบสุ่มทุกดวงเป็นเวลา length วินาที (ไม่คืนค่าเดิมให้ — คนเรียกจัดการเอง)</summary>
        private IEnumerator Burst(float length)
        {
            float t = 0f;
            while (t < length)
            {
                for (int i = 0; i < lamps.Length; i++)
                {
                    var l = lamps[i];
                    if (l == null) continue;

                    // แต่ละดวงสุ่มแยกกัน — ถ้ากระพริบพร้อมกันเป๊ะจะดูเหมือน bug ไม่เหมือนไฟเสีย
                    if (Random.value < blackoutChance)
                    {
                        l.enabled = false;
                    }
                    else
                    {
                        l.enabled   = true;
                        l.intensity = _origIntensity[i] * Random.Range(dimMin, dimMax);
                    }
                }

                float step = Random.Range(minStep, maxStep);
                t += step;
                yield return new WaitForSeconds(step);
            }
        }

        // ─────────────────────────── Cache ───────────────────────────

        private void CacheOriginals()
        {
            _origIntensity = new float[lamps.Length];
            _origEnabled   = new bool[lamps.Length];

            for (int i = 0; i < lamps.Length; i++)
            {
                if (lamps[i] == null) continue;
                _origIntensity[i] = lamps[i].intensity;
                _origEnabled[i]   = lamps[i].enabled;
            }
        }

        /// <summary>คืนหน้าตาไฟเป็นค่าเดิมระหว่างที่ยังกระพริบอยู่ (ไม่ทิ้ง cache)</summary>
        private void RestoreLook()
        {
            if (_origIntensity == null) return;

            for (int i = 0; i < lamps.Length && i < _origIntensity.Length; i++)
            {
                if (lamps[i] == null) continue;
                lamps[i].intensity = _origIntensity[i];
                lamps[i].enabled   = _origEnabled[i];
            }
        }

        /// <summary>คืนค่าเดิมและทิ้ง cache — ใช้ตอน Stop() เท่านั้น</summary>
        private void RestoreOriginals()
        {
            RestoreLook();
            _origIntensity = null;
            _origEnabled   = null;
        }

        private void SetAllEnabled(bool on)
        {
            foreach (var l in lamps)
                if (l != null) l.enabled = on;
        }
    }
}
