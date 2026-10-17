using System;
using System.Collections;
using InputSystem;
using Player;
using UnityEngine;

namespace Rule6
{
    /// <summary>
    /// ท่าเดียวกันทั้งเกม: "รถคันหนึ่งวิ่งไปตามเส้นทาง พร้อมผู้เล่นที่นั่งอยู่ในนั้น"
    ///
    /// มีสองที่ที่ต้องใช้ท่านี้ และทั้งสองที่ต้องออกมาเหมือนกันเป๊ะ:
    ///   ฉากเปิดเกม           รถพาเรามาถึงศาลาตอน 18:40 แล้วเราลงจากรถ
    ///   บทจบวนลูปของกฎ 6    รถคันที่ 4 พาเรากลับมาถึงศาลาตอน 18:40 แล้วเราลงจากรถ
    ///
    /// ความเหมือนนั้นคือตัวมุกเลย — คืนที่สองไม่ใช่ "คืนใหม่ที่หน้าตาคล้ายคืนก่อน" มันคือคืน
    /// เดิมซ้ำ ถ้าสองฉากนี้ขับคนละความเร็ว จอดคนละจุด หรือลงรถคนละวิธี มุกจะพัง เลยเขียน
    /// ไว้ที่เดียวแล้วให้ทั้งสองฝั่งเรียกใช้ ไม่ใช่ก๊อปไปวางสองรอบ
    ///
    /// ตัวนี้ไม่ใช่ MonoBehaviour — ใครเรียกก็ new ไว้เป็นฟิลด์ตัวเอง แล้ว yield return
    /// เมธอดที่เป็น IEnumerator จาก coroutine ของตัวเอง (coroutine ซ้อน: หยุดตัวแม่แล้วตัวนี้
    /// หยุดตามไปด้วย ไม่มี coroutine ลอยค้างอยู่หลังฉากถูกยกเลิก)
    /// </summary>
    public class BusRide
    {
        /// <summary>hint ต้องค้างจนเราสั่ง Hide เอง — PlayerDialogueUI fade เองเมื่อ hold ครบ</summary>
        private const float PersistentHint = 3600f;

        // ════════════════════════════════════════════════════════════════
        #region พาผู้เล่นไปกับรถ
        // ════════════════════════════════════════════════════════════════

        private Transform  _bus;
        private Vector3    _localPos;
        private Quaternion _localRot;

        /// <summary>ตอนนี้กำลังพาผู้เล่นไปกับรถอยู่ไหม</summary>
        public bool IsCarrying => _bus != null;

        /// <summary>
        /// เริ่มพาผู้เล่นไปกับรถ — จำท่าของผู้เล่นเทียบกับตัวรถไว้ แล้วคำนวณกลับทุกเฟรม
        ///
        /// ไม่ใช้ transform.SetParent เพราะผู้เล่นเป็น DontDestroyOnLoad: พอเอาไปเป็นลูกของ
        /// object ในซีน Unity จะย้ายมันเข้าซีนนั้น แล้วตอน unparent มันไม่ย้ายกลับให้ ผู้เล่นจะ
        /// ถูกทำลายพร้อมซีนในครั้งถัดไปที่โหลดซีน
        ///
        /// เขียน transform ตรงๆ ปลอดภัยเพราะช่วงนี้ผู้เล่นนั่งอยู่: Update ไม่ใส่ gravity
        /// CharacterController.Move ไม่ถูกเรียก และ RotateWhileSitting หันแค่ cameraPivot
        /// ไม่ได้แตะ transform.rotation ของตัวละคร จึงไม่มีใครมาแย่งเขียนท่าเดียวกัน
        /// </summary>
        public void BeginCarry(Transform bus)
        {
            var pc = PlayerController.Instance;
            if (pc == null || bus == null) return;

            _bus      = bus;
            _localPos = bus.InverseTransformPoint(pc.transform.position);
            _localRot = Quaternion.Inverse(bus.rotation) * pc.transform.rotation;
        }

        /// <summary>ลากผู้เล่นไปอยู่ท่าเดิมเทียบกับรถ — เรียกทุกเฟรมที่รถขยับ</summary>
        public void Step()
        {
            if (_bus == null) return;

            var pc = PlayerController.Instance;
            if (pc == null) return;

            pc.transform.SetPositionAndRotation(
                _bus.TransformPoint(_localPos),
                _bus.rotation * _localRot);
        }

        public void End() => _bus = null;

        /// <summary>ย้ายรถทั้งคันไปจุดใหม่ พร้อมผู้เล่นที่นั่งอยู่ในนั้น — ใช้ตอนจอดำ</summary>
        public void PlaceAt(Transform bus, Transform point)
        {
            if (bus == null || point == null) return;

            bus.SetPositionAndRotation(point.position, point.rotation);
            Step();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region ขับ
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// ขับรถจากจุดหนึ่งไปอีกจุดหนึ่งใน seconds วินาที
        ///
        /// decelerate = true ตอนเข้าจอด (ช้าลงท้ายทาง) / false ตอนออก (ค่อยๆ เร่ง)
        /// ถ้าใช้ SmoothStep ทั้งสองทิศ รถจะออกตัวช้าแล้วจอดกึก ซึ่งอ่านเป็นรถของเล่น
        ///
        /// stopEarly = เงื่อนไขยกเลิกกลางทาง (เช่นผู้เล่นกด skip ฉากเปิด) — พอจริงจะกระโดด
        /// ไปจุดปลายทางทันที ไม่ใช่ทิ้งรถค้างกลางถนน
        /// </summary>
        public IEnumerator Drive(Transform bus, Transform from, Transform to,
                                 float seconds, bool decelerate,
                                 bool carryPlayer = false, Func<bool> stopEarly = null)
        {
            if (bus == null || from == null || to == null) yield break;

            if (seconds > 0f)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    if (stopEarly != null && stopEarly()) break;

                    elapsed += Time.deltaTime;

                    float k     = Mathf.Clamp01(elapsed / seconds);
                    float eased = decelerate ? 1f - (1f - k) * (1f - k)   // ease-out
                                             : k * k;                      // ease-in

                    bus.SetPositionAndRotation(
                        Vector3.Lerp(from.position, to.position, eased),
                        Quaternion.Slerp(from.rotation, to.rotation, eased));

                    if (carryPlayer) Step();

                    yield return null;
                }
            }

            bus.SetPositionAndRotation(to.position, to.rotation);
            if (carryPlayer) Step();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region นั่ง / ลงจากรถ
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// พาผู้เล่นไปนั่งที่ที่นั่งของรถคันนั้น
        ///
        /// ใช้ StartSittingSequence ตัวเดียวกับม้านั่งที่ศาลา เพื่อให้ท่านั่ง ความสูงกล้อง และ
        /// animator trigger เป็นชุดเดียวกันทั้งเกม — ถ้า teleport เองกล้องจะค้างที่ระดับคนยืน
        ///
        /// ล็อก isBlockStanding ก่อนนั่ง ไม่ใช่หลัง: ระหว่าง SitRoutine (0.7 วิ) ผู้เล่นยังกด E
        /// ได้อยู่ ถ้าไม่ล็อกไว้ก่อนจะลุกออกจากรถกลางฉากได้
        ///
        /// คืนค่าผ่าน onFailed เพราะ coroutine คืนค่าตรงๆ ไม่ได้ — ผู้เรียกบางคน (ฉากเปิด)
        /// ต้องรู้ว่าพาไปนั่งไม่สำเร็จ เพื่อจะไม่เริ่มพาผู้เล่นไปกับรถทั้งที่ยังยืนอยู่
        /// </summary>
        public IEnumerator Sit(Rule6Bus bus, float settleSeconds, UnityEngine.Object context = null,
                               Action onFailed = null)
        {
            var player = PlayerController.Instance;
            if (player == null || bus == null) { onFailed?.Invoke(); yield break; }

            player.isBlockStanding = true;

            // SitPose ไม่ใช่ Seat — Seat อยู่บนเบาะ (ไว้คุมระยะกด E) ส่วน SitPose อยู่ที่พื้นรถ
            // เพราะ SitRoutine วางราก (เท้า) ของตัวละครที่จุดนั้นเป๊ะ ใช้จุดบนเบาะตัวจะลอย
            if (bus.SitPose == null)
            {
                Debug.LogError($"[BusRide] รถ '{bus.name}' ไม่มีทั้ง Sit Pose และ Seat — " +
                               "พาผู้เล่นไปนั่งไม่ได้", context);
                onFailed?.Invoke();
                yield break;
            }

            if (player.IsSitting()) yield break;

            player.StartSittingSequence(bus.SitPose);

            // มี timeout เพราะ StartSittingSequence เงียบออกไปเลยถ้า _isTransitioning อยู่
            // (เช่นผู้เล่นเพิ่งลุกจากม้านั่งศาลาแล้ววิ่งเข้ารถทันทีในช่วงอนิเมชั่นลุก)
            float waited = 0f;
            while (!player.IsSitting() && waited < 2f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!player.IsSitting())
            {
                Debug.LogWarning($"[BusRide] พาผู้เล่นไปนั่งในรถ '{bus.name}' ไม่สำเร็จ", context);
                onFailed?.Invoke();
                yield break;
            }

            if (settleSeconds > 0f) yield return new WaitForSeconds(settleSeconds);
        }

        /// <summary>
        /// รอให้ผู้เล่นลุกจากเบาะแล้วเดินลงจากรถ
        ///
        /// ต้องรับปุ่ม E เองที่นี่ เพราะเบาะรถไม่มี SittingPoint — ตัวนั้นเป็นของม้านั่งศาลา
        /// ถ้าไม่มีใครรับ ผู้เล่นจะนั่งค้างอยู่ในรถตลอดไปโดยไม่มีอะไรบอกว่าต้องทำอะไร
        ///
        /// และต้องรอ "เดินออกจากรถ" ไม่ใช่แค่ "ลุก" ด้วย ไม่งั้นรถจะออกตัวตอนผู้เล่นยังยืนอยู่
        /// ในนั้น แล้วมันจะวิ่งหายไปทิ้งผู้เล่นยืนลอยอยู่กลางถนน
        ///
        /// ไม่ต้อง SetMovement(true) เอง — StandUpRoutine ปลดทั้งเดินและหันให้ตอนยืนเสร็จ
        /// </summary>
        public IEnumerator LeaveBus(Rule6Bus bus, InputHandler input, string hint,
                                    float offBusDistance, float extraDelay)
        {
            var pc = PlayerController.Instance;
            if (pc == null || bus == null) yield break;

            End();

            // รถพาผู้เล่นมาไกลจากจุดที่นั่งลงแล้ว ต้องย้ายจุดที่จะกลับไปยืนมาที่นี่ก่อนปล่อยให้ลุก
            // ไม่งั้น StandUpRoutine จะดีดผู้เล่นกลับไปยืนที่จุดที่ขึ้นรถ ซึ่งอยู่ปลายถนน
            //
            // และต้องยืนที่ "ทางเดินข้างเบาะ" ไม่ใช่ตรงจุดที่นั่ง: ตอนนั่งตัวละครอยู่เลเยอร์
            // PlayerSitting ซึ่งไม่ชนเบาะ พอลุกมันกลับไปเลเยอร์ Default แล้วชนทุกอย่าง —
            // ยืนตรงเบาะพอดีคือยืนอยู่ข้างในเบาะ แล้วขยับไปไหนไม่ได้เลย
            if (bus.StandPose != null) pc.RebaseStandUpPosition(bus.StandPose.position);
            else
            {
                pc.RebaseStandUpPosition();
                Debug.LogWarning($"[BusRide] รถ '{bus.name}' ยังไม่ได้ใส่ Stand Pose — ผู้เล่นจะลุก" +
                                 "ขึ้นตรงเบาะพอดีแล้วติดอยู่ในเบาะ ลาก Stand Pose ไปที่ทางเดินข้างเบาะ", bus);
            }
            pc.isBlockStanding = false;

            // ไม่ตั้งธง "กดแล้ว" เพราะ StartStandingSequence เงียบออกไปเลยถ้า _isTransitioning
            // อยู่ — ปล่อยให้กด E ซ้ำได้เรื่อยๆ จนลุกสำเร็จ ดีกว่าค้างรอธงที่ไม่มีวันเป็นจริง
            void Stand()
            {
                if (pc.IsSitting()) pc.StartStandingSequence();
            }

            if (input != null) input.OnInteractPressed += Stand;
            if (!string.IsNullOrWhiteSpace(hint) && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowLine(hint, PersistentHint);

            yield return new WaitUntil(() => !pc.IsSitting());

            if (input != null) input.OnInteractPressed -= Stand;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            Transform seat = bus.Seat != null ? bus.Seat : bus.transform;

            yield return new WaitUntil(() =>
            {
                Vector3 delta = pc.transform.position - seat.position;
                delta.y = 0f;
                return delta.sqrMagnitude >= offBusDistance * offBusDistance;
            });

            if (extraDelay > 0f) yield return new WaitForSeconds(extraDelay);
        }

        #endregion
    }
}
