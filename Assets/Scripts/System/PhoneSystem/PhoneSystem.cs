using System.Collections.Generic;
using Enum;
using InputSystem;
using Manager;
using Player;
using ScriptableObject;
using UnityEngine;

namespace PhoneSystem
{
    public class PhoneSystem : MonoBehaviour
    {
        public PhoneState CurrentState { get; private set; }

        private bool _phoneLocked;

        /// <summary>หน้าที่ผ่านมาก่อนหน้านี้ เรียงจากเก่าไปใหม่ — ไม่รวมหน้าปัจจุบัน</summary>
        private readonly List<PhoneState> _history = new List<PhoneState>();

        private static PhoneSystem _active;
        private static int _backFrame = -1;

        /// <summary>
        /// Esc เฟรมนี้เป็นของโทรศัพท์ ไม่ใช่ของเมนูหยุดเกม
        ///
        /// ต้องนับเฟรมที่เพิ่งกดถอยด้วย ลำดับ Update ไม่การันตี: ถ้า Esc เพิ่งปิดโทรศัพท์ก่อนเมนูจะได้อ่าน
        /// เมนูจะเห็นว่าโทรศัพท์ปิดแล้วและเปิดตัวเองขึ้นมาทันทีในเฟรมเดียวกัน
        /// </summary>
        public static bool HandlesEscape =>
            (_active != null && _active.CurrentState != PhoneState.Hidden) || _backFrame == Time.frameCount;

        [SerializeField] private PlayerController playerController;
        [SerializeField] private GameObject phoneObject;
        [SerializeField] private PhoneAppController appController;
        [SerializeField] private PhoneUIController uiController;
        [SerializeField] private InputHandler input;
        [SerializeField] private ConversationRunner runner;

        [Tooltip("ใส่ ChatUIController — ตอนมีตัวเลือกคำตอบค้างอยู่ ปุ่มตัวเลขจะกลายเป็นปุ่มเลือกคำตอบ")]
        [SerializeField] private ChatUIController chatUI;

        
        private void OnEnable()
        {
            _active = this;
            input.OnPhoneToggle += TogglePhone;
            input.OnAppKeyPressed += OpenAppByIndex;
            input.OnBackPressed += Back;
            input.OnSetTime += appController.SetTimeApp;
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
            input.OnPhoneToggle -= TogglePhone;
            input.OnAppKeyPressed -= OpenAppByIndex;
            input.OnBackPressed -= Back;
            input.OnSetTime -= appController.SetTimeApp;
        }
        public void ChangeState(PhoneState newState)
        {
            RememberForBack(newState);
            CurrentState = newState;
            uiController.UpdateState(newState);
        }

        /// <summary>
        /// จดหน้าที่เพิ่งออกมา ให้ Esc พากลับไปถูกที่
        ///
        /// ถ้ากลับไปหน้าที่อยู่ในประวัติแล้ว ตัดทางที่เดินวนทิ้ง ไม่จดซ้ำ — ประวัติจึงไม่ยาวเกินจำนวนหน้า
        /// สลับแอปไปมากี่รอบก็ตาม กด Esc ไม่กี่ครั้งก็ถึงการปิดโทรศัพท์
        /// </summary>
        private void RememberForBack(PhoneState next)
        {
            if (next == CurrentState) return;

            if (next == PhoneState.Hidden)
            {
                _history.Clear();
                return;
            }

            int seenAt = _history.IndexOf(next);
            if (seenAt >= 0)
            {
                _history.RemoveRange(seenAt, _history.Count - seenAt);
                return;
            }

            if (CurrentState != PhoneState.Hidden) _history.Add(CurrentState);
        }
        /// <summary>ล็อกไม่ให้ผู้เล่นเปิดโทรศัพท์ได้ (ใช้ตอนโทรศัพท์หาย เช่น Rule 1)</summary>
        public void LockPhone()
        {
            _phoneLocked = true;
            LowerPhone(); // บังคับซ่อน + set state = Hidden
        }

        /// <summary>ปลดล็อกให้ผู้เล่นเปิดโทรศัพท์ได้ตามปกติ</summary>
        public void UnlockPhone()
        {
            _phoneLocked = false;
        }

        private void TogglePhone()
        {
            if (_phoneLocked) return; // โทรศัพท์หายอยู่ → ห้ามเปิด

            // ถือสมุดอยู่ → ห้ามเปิด/ปิด (ปิด = UnlockPlayer ผู้เล่นเดินได้ทั้งที่สมุดลอยค้างตรงหน้า)
            if (Book.BookPageTurner.IsReading) return;

            // ซ่อมตู้ไฟกฎ 2 อยู่ → ห้ามเปิด (เหตุผลเดียวกัน กล้องจับอยู่ที่ตู้)
            if (Rule2.LeverPuzzleController.AnyPlayerLocked) return;

            if (CurrentState == PhoneState.Hidden)
                RaisePhone();
            else
                LowerPhone();
        }
        public void OpenChat()
        {
            ChangeState(PhoneState.ChatView);
        }
        /// <summary>
        /// Esc — กลับไปหน้าที่อยู่ก่อนหน้า ไม่มีหน้าก่อนหน้าแล้วก็ปิดโทรศัพท์
        ///
        /// โทรศัพท์ไม่ได้ยกอยู่ = ไม่ทำอะไร Esc ตอนนั้นเป็นของเมนูหยุดเกม
        /// </summary>
        public void Back()
        {
            if (CurrentState == PhoneState.Hidden) return;

            _backFrame = Time.frameCount;

            if (_history.Count == 0)
            {
                LowerPhone();
                return;
            }

            PhoneState previous = _history[_history.Count - 1];
            ChangeState(previous);

            // ล็อกตัวให้ตรงกับหน้าที่กลับไป: หน้าโฮมกับไฟฉายเดินได้เหมือนตอนเปิดเข้ามาครั้งแรก
            if (previous == PhoneState.AppSelection || previous == PhoneState.FlashLight) UnlockPlayer();
            else LockPlayer();
        }
        public void OpenMessageApp()
        {
            ChangeState(PhoneState.FriendList);
        }
        public void RaisePhone()
        {
            phoneObject.SetActive(true);
            ChangeState(PhoneState.AppSelection);
        }

        /// <summary>
        /// กดปุ่มตัวเลขเพื่อเปิดแอป — สลับแอปได้ตรงๆ ไม่ต้องถอยกลับหน้าโฮมก่อน ยกเว้นตอนอ่านแชท
        /// </summary>
        public void OpenAppByIndex(int index)
        {
            if (CurrentState == PhoneState.Hidden) return;

            // อยู่ในหน้าแชท → ตัวเลขใช้เลือกคำตอบเท่านั้น ไม่สลับแอป ออกจากแชทต้องกด Esc
            //
            // เดิมเลขที่ไม่ตรงกับตัวเลือกจะหลุดไปเปิดแอปอื่น ผู้เล่นที่กดเลขผิดตอนอ่านแชทจึงถูกพา
            // ออกจากบทสนทนาโดยไม่ตั้งใจ
            //
            // ต้องเช็กว่าอยู่หน้าแชทจริงๆ ด้วย — ตัวเลือกยังค้างอยู่หลังปิดโทรศัพท์/ถอยออกจากแชท
            // เดิมเปิดโทรศัพท์ใหม่แล้วกด 1 เพื่อเข้าแอปแชท เลข 1 กลับไปเลือกคำตอบแรกแทน
            if (CurrentState == PhoneState.ChatView)
            {
                if (chatUI != null) chatUI.TrySelectReply(index);
                return;
            }

            PhoneState state = appController.GetStateByIndex(index);

            if (state != PhoneState.FlashLight)
            {
                LockPlayer();
            }
            else
            {
                UnlockPlayer();
            }
            appController.OpenAppByIndex(index);
        }

        /// <summary>
        /// ยกโทรศัพท์แล้วล็อกตัว/กล้อง แต่ "ไม่" ปล่อยเมาส์ออกมา
        ///
        /// จอโทรศัพท์ถ่ายลง RenderTexture เมาส์จึง raycast โดนปุ่มในจอไม่ได้อยู่แล้ว ทุกอย่าง
        /// บนโทรศัพท์เป็นปุ่มคีย์บอร์ดทั้งหมด (เลขสลับแอป / Esc ถอยกลับ) ลูกศรเมาส์ที่
        /// โผล่มาจึงไม่มีอะไรให้กด มีแต่ลอยทับจอแล้วทำให้หลุดจากเกม
        ///
        /// ปล่อย lockState = Locked ไว้ด้วย ไม่ใช่แค่ visible = false — ถ้าแค่ซ่อนแต่ไม่ล็อก
        /// เมาส์จริงยังไหลออกนอกหน้าต่างได้ กลับมาคลิกอีกทีอาจไปโดนจอที่สองหรือ taskbar
        /// </summary>
        private void LockPlayer()
        {
            playerController.SetMovement(false);
            playerController.SetLook(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void LowerPhone()
        {
            phoneObject.SetActive(false);
            UnlockPlayer();
            ChangeState(PhoneState.Hidden);
        }

        private void UnlockPlayer()
        {
            if (!playerController.IsSitting())
                playerController.SetMovement(true); 
            playerController.SetLook(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
