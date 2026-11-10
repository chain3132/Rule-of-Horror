using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ปุ่ม "กลับเมนูหลัก" — ใส่คู่กับ Button ตัวไหนก็ได้ (เมนู Esc, จอ Game Over, จอบทจบ)
/// ไม่ต้องผูก OnClick เอง ตัวนี้ผูกให้ตอนเปิด
/// ใส่ให้อัตโนมัติด้วยเมนู Rule of Horror ▸ Localization ▸ Add Main Menu Buttons
/// </summary>
[RequireComponent(typeof(Button))]
public class MainMenuButton : MonoBehaviour
{
    private Button _button;

    private void Awake() => _button = GetComponent<Button>();

    private void OnEnable()  => _button.onClick.AddListener(GameSession.ReturnToMainMenu);
    private void OnDisable() => _button.onClick.RemoveListener(GameSession.ReturnToMainMenu);
}
