using InputSystem;
using UnityEngine;

namespace Rule4
{
    /// <summary>
    /// ศาลพระภูมิ — จุดที่ผู้เล่นเอาตุ๊กตามาวาง
    ///
    /// - ผู้เล่นกด E เมื่อเข้าใกล้ + ถือตุ๊กตาอยู่ → วาง 1 ตัว
    /// - หลังผู้เล่นเก็บตุ๊กตาแต่ละตัว Rule4 จะสั่ง RelocateTo() ให้ศาลย้ายไปจุดใหม่
    /// - ตุ๊กตาที่วางแล้วจะโผล่ที่ dollSlots "ช่องของตัวมันเอง" (Doll.ShrineSlotIndex)
    ///   ไม่ใช่เรียงตามลำดับที่วาง เพราะตุ๊กตาแต่ละแบบมีที่ทางบนศาลไม่เหมือนกัน
    /// </summary>
    public class SpiritHouse : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("ระยะที่วางตุ๊กตาได้ (เมตร)")]
        [SerializeField] private float placeRadius = 3f;

        [Tooltip("ข้อความ hint ตอนเข้าใกล้พร้อมตุ๊กตาในมือ")]
        [SerializeField] private string placeHint = "E   วางตุ๊กตาที่ศาล";

        [Tooltip("ข้อความ hint ตอนเข้าใกล้แต่มือเปล่า")]
        [SerializeField] private string emptyHandHint = "ยังไม่มีตุ๊กตาในมือ";

        [Header("Debug")]
        [Tooltip("ชั่วคราว — log สถานะตอนเข้า/ออกระยะศาล เพื่อไล่บั๊ก 'ไม่มีตุ๊กตาในมือ'")]
        [SerializeField] private bool debugLog;

        [Header("Visual")]
        [Tooltip("ช่องวางตุ๊กตาบนศาล (Slot 1, Slot 2, ...) — ปิดไว้ทั้งหมดตั้งแต่แรก" +
                 "ลำดับในอาร์เรย์นี้คือเลขที่ DollVariant.shrineSlotIndex อ้างถึง (ตัวแรก = 0)")]
        [SerializeField] private GameObject[] dollSlots;

        // ── Runtime ──
        private RuleSystem.Rule.Rule4 _rule;
        private InputHandler          _inputHandler;
        private bool                  _subscribed;
        private bool                  _hintCarryState;  // ข้อความที่แสดงอยู่ตอนนี้สะท้อนสถานะไหน

        // hint ที่ต้องค้างจนกว่าผู้เล่นจะเดินออกจากระยะ — PlayerDialogueUI จะ fade เองเมื่อ hold ครบ
        // ส่งค่ายาวๆ ไปเพื่อให้มันค้าง แล้วเรียก Hide() เองตอนออกนอกระยะ
        private const float PersistentHold = 3600f;

        // ─────────────────────────── Setup ───────────────────────────

        public void Setup(RuleSystem.Rule.Rule4 rule, InputHandler inputHandler)
        {
            _rule         = rule;
            _inputHandler = inputHandler;
            ResetSlots();

            if (debugLog)
                Debug.Log($"[SpiritHouse] Setup โดย Rule4 id={rule.GetInstanceID()} " +
                          $"(ศาลตัวนี้ id={GetInstanceID()})", this);
        }

        private void OnDestroy() => Unsubscribe();

        /// <summary>ซ่อนตุ๊กตาบนศาลทั้งหมด (เริ่มกฎใหม่ / retry หลังตาย)</summary>
        public void ResetSlots()
        {
            if (dollSlots == null) return;
            foreach (var slot in dollSlots)
                if (slot != null) slot.SetActive(false);
        }

        /// <summary>ย้ายศาลไปยังจุดใหม่ — เรียกทุกครั้งที่ผู้เล่นเก็บตุ๊กตาได้</summary>
        public void RelocateTo(Transform point)
        {
            if (point == null) return;
            transform.SetPositionAndRotation(point.position, point.rotation);
        }

        // ─────────────────────────── Update ───────────────────────────

        private void Update()
        {
            if (_rule == null) return;

            bool inRange  = Vector3.Distance(transform.position, PlayerPosition()) <= placeRadius;
            bool carrying = _rule.IsCarryingDoll;

            if (inRange)
            {
                if (!_subscribed)
                {
                    _subscribed = true;
                    if (_inputHandler != null) _inputHandler.OnInteractPressed += TryPlace;
                    ShowHint(carrying);
                }
                else if (carrying != _hintCarryState)
                {
                    // ผู้เล่นเก็บ/วางตุ๊กตาระหว่างที่ยังยืนอยู่ในระยะศาล — ข้อความต้องเปลี่ยนตาม
                    // ถ้าไม่เช็คตรงนี้ ข้อความจะค้างเป็นค่าตอนก้าวเข้าระยะครั้งแรกตลอด
                    ShowHint(carrying);
                }
            }
            else if (_subscribed)
            {
                Unsubscribe();
            }
        }

        /// <summary>แสดง hint ตามสถานะการถือตุ๊กตา — เรียกเฉพาะตอนสถานะเปลี่ยน ไม่ใช่ทุกเฟรม
        /// (PlayerDialogueUI.ShowLine เริ่ม typewriter ใหม่ทุกครั้งที่เรียก)</summary>
        private void ShowHint(bool carrying)
        {
            _hintCarryState = carrying;

            if (debugLog)
                Debug.Log($"[SpiritHouse] ShowHint(carrying={carrying}) " +
                          $"— อ่านจาก Rule4 id={_rule.GetInstanceID()}", this);

            if (PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowLine(carrying ? placeHint : emptyHandHint, PersistentHold);
        }

        private Vector3 PlayerPosition()
        {
            var pc = Player.PlayerController.Instance;
            return pc != null ? pc.transform.position : transform.position + Vector3.one * 9999f;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_inputHandler != null) _inputHandler.OnInteractPressed -= TryPlace;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        // ─────────────────────────── Place ───────────────────────────

        /// <summary>เปิดช่องบนศาลที่เป็นของตุ๊กตาตัวนี้โดยเฉพาะ</summary>
        private void RevealSlotFor(Doll doll)
        {
            if (dollSlots == null || dollSlots.Length == 0)
            {
                Debug.LogWarning("[SpiritHouse] ยังไม่ได้ใส่ dollSlots — วางตุ๊กตาแล้วจะไม่เห็นอะไรโผล่", this);
                return;
            }

            if (doll == null)
            {
                Debug.LogWarning("[SpiritHouse] ไม่รู้ว่ากำลังวางตุ๊กตาตัวไหน — ข้ามการเปิดช่อง", this);
                return;
            }

            int index = doll.ShrineSlotIndex;
            if (index < 0 || index >= dollSlots.Length)
            {
                Debug.LogWarning($"[SpiritHouse] ตุ๊กตา '{doll.name}' ชี้ช่องที่ {index} " +
                                 $"แต่ dollSlots มีแค่ {dollSlots.Length} ช่อง — เช็ค DollVariant.shrineSlotIndex", this);
                return;
            }

            if (dollSlots[index] != null) dollSlots[index].SetActive(true);
        }

        private void TryPlace()
        {
            if (_rule == null || !_rule.CanPlaceDoll) return;

            RevealSlotFor(_rule.CarriedDoll);

            AudioManager.instance.PlayDollPlace();
            Unsubscribe();

            _rule.OnDollPlaced();
        }
    }
}
