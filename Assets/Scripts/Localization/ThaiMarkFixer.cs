using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// แก้สระ/วรรณยุกต์ไทยจม — จัดตำแหน่งเครื่องหมายบน-ล่างใหม่หลัง TMP วางตัวอักษรเสร็จ
///
/// ต้นเหตุ: ฟอนต์ไทยวาดวรรณยุกต์ไว้ "ระดับต่ำ" (ระดับเดียวกับสระบน) แล้วพึ่งตาราง GPOS
/// (mark / mkmk) ให้ยกขึ้นเองตอนมีสระบน แต่ฟอนต์ Torsilp Chettana ใน TMP ไม่มีตารางนั้นเลย
/// (Mark To Base / Mark To Mark ว่างทั้งคู่) ผลคือ:
///   ที่ / นี้ / ชื่อ     วรรณยุกต์ซ้อนทับสระบน
///   น้ำ / ค่ำ          วรรณยุกต์ซ้อนกับวงกลมของสระอำ
///   ป่ ฝั่ ฟ้ ฬ        สระบน/วรรณยุกต์ทับหางของพยัญชนะหางยาว
///   ฎุ ฏู ญุ ฐุ        สระล่างทับเชิงของพยัญชนะ
///
/// ตัวนี้ฟัง TEXT_CHANGED ของ TMP ทุกตัวในเกม — ไม่ต้องใส่ component ที่ป้ายไหนเลย ฟองแชท
/// ที่สร้างตอนเล่นก็โดนด้วย
///
/// ขยับเฉพาะตอน "ซ้อนกันจริง" (วัดจากขนาดตัวอักษรจริงของฟอนต์) — ฟอนต์ที่จัดถูกอยู่แล้ว
/// หรือข้อความที่ไม่มีภาษาไทย จะไม่ถูกแตะเลย เปลี่ยนฟอนต์ทีหลังก็ไม่พัง
/// </summary>
public static class ThaiMarkFixer
{
    // ช่องว่างระหว่างเครื่องหมายที่ซ้อนกัน เทียบกับขนาดฟอนต์ตอนสร้าง atlas
    private const float GapRatio = 0.03f;

    // พยัญชนะหางยาว: ขยับเครื่องหมายบนไปทางซ้ายจนพ้นหาง (หางกว้างราวๆ เท่านี้ของตัว)
    private const float TailRatio = 0.24f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        // Remove ก่อน — ปิด Reload Domain แล้ว static ไม่ถูกล้าง จะได้ไม่ฟังซ้ำสองรอบ
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

#if UNITY_EDITOR
    // ให้หน้าต่าง Scene/Game ตอนแก้ฉากเห็นถูกด้วย — แก้แค่ mesh บนจอ ไม่ได้บันทึกอะไรลงซีน
    [UnityEditor.InitializeOnLoadMethod]
    private static void HookEditor() => Hook();
#endif

    private static void OnTextChanged(Object obj)
    {
        var text = obj as TMP_Text;
        if (text == null || !text.isActiveAndEnabled) return;

        var info = text.textInfo;
        if (info == null || info.characterCount == 0 || !HasThaiMark(info)) return;

        if (Fix(info)) text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    private static bool HasThaiMark(TMP_TextInfo info)
    {
        var chars = info.characterInfo;
        for (int i = 0; i < info.characterCount; i++)
            if (IsUpper(chars[i].character) || IsTone(chars[i].character) || IsLower(chars[i].character))
                return true;
        return false;
    }

    // ─────────────────────────── จัดตำแหน่ง ───────────────────────────

    private static bool Fix(TMP_TextInfo info)
    {
        var chars   = info.characterInfo;
        bool moved  = false;
        int  baseAt = -1;   // พยัญชนะ (ตัวที่ไม่ใช่เครื่องหมาย) ตัวล่าสุด
        int  upper  = -1;   // สระบนของพยัญชนะตัวนั้น

        for (int i = 0; i < info.characterCount; i++)
        {
            char c = chars[i].character;

            if (!IsUpper(c) && !IsTone(c) && !IsLower(c))
            {
                baseAt = i;
                upper  = -1;
                continue;
            }
            if (baseAt < 0 || !chars[i].isVisible || !chars[baseAt].isVisible) continue;

            float gap = Gap(chars[baseAt]);

            if (IsLower(c))
            {
                // สระล่างทับเชิงพยัญชนะ (ฎ ฏ ญ ฐ) — กดลงให้พ้นเชิง
                float overlap = Top(chars[i]) - (Bottom(chars[baseAt]) - gap);
                if (overlap > 0f) moved |= Shift(info, i, 0f, -overlap);
                continue;
            }

            // สระบนหรือวรรณยุกต์ — ข้างใต้มันคืออะไร: สระบน (ที่) / วงกลมของสระอำที่ตามมา (น้ำ)
            int below = -1;
            if (IsTone(c))
            {
                if (upper >= 0) below = upper;
                else if (i + 1 < info.characterCount && chars[i + 1].character == SaraAm && chars[i + 1].isVisible)
                    below = i + 1;
            }

            if (below >= 0)
            {
                float overlap = (Top(chars[below]) + gap) - Bottom(chars[i]);
                if (overlap > 0f) moved |= Shift(info, i, 0f, overlap);
            }

            // พยัญชนะหางยาว — เลื่อนไปซ้ายจนขอบขวาพ้นหาง ทั้งสระบนและวรรณยุกต์ชิดขอบเดียวกัน
            // จึงยังตรงกันอยู่หลังเลื่อน
            if (IsTall(chars[baseAt].character))
            {
                float tailLeft = Right(chars[baseAt]) - Width(chars[baseAt]) * TailRatio;
                float dx = tailLeft - Right(chars[i]);
                if (dx < 0f) moved |= Shift(info, i, dx, 0f);
            }

            if (IsUpper(c)) upper = i;
        }

        return moved;
    }

    private static bool Shift(TMP_TextInfo info, int index, float dx, float dy)
    {
        ref var ch = ref info.characterInfo[index];
        var vertices = info.meshInfo[ch.materialReferenceIndex].vertices;
        if (vertices == null || ch.vertexIndex + 3 >= vertices.Length) return false;

        var d = new Vector3(dx, dy, 0f);
        for (int v = 0; v < 4; v++) vertices[ch.vertexIndex + v] += d;

        // เก็บตำแหน่งใหม่ไว้ใน characterInfo ด้วย — ตัวถัดไปในกลุ่มเดียวกันวัดจากตัวนี้
        ch.bottomLeft  += d;
        ch.topLeft     += d;
        ch.topRight    += d;
        ch.bottomRight += d;
        ch.origin      += dx;
        ch.baseLine    += dy;
        return true;
    }

    // ขอบจริงของตัวอักษร (ไม่รวม padding ของ SDF) — วัดจาก metrics ของ glyph
    private static GlyphMetrics Metrics(in TMP_CharacterInfo ch) => ch.textElement.glyph.metrics;

    private static float Top(in TMP_CharacterInfo ch)    => ch.baseLine + Metrics(ch).horizontalBearingY * ch.scale;
    private static float Bottom(in TMP_CharacterInfo ch) => Top(ch) - Metrics(ch).height * ch.scale;
    private static float Width(in TMP_CharacterInfo ch)  => Metrics(ch).width * ch.scale;
    private static float Right(in TMP_CharacterInfo ch)  => ch.origin + (Metrics(ch).horizontalBearingX + Metrics(ch).width) * ch.scale;

    private static float Gap(in TMP_CharacterInfo ch)
    {
        float points = ch.fontAsset != null ? ch.fontAsset.faceInfo.pointSize : 90f;
        return points * GapRatio * ch.scale;
    }

    // ─────────────────────────── ชนิดตัวอักษร ───────────────────────────

    private const char SaraAm = 'ำ';   // ำ

    // ั ิ ี ึ ื ็ ํ
    private static bool IsUpper(char c) => c == 'ั' || (c >= 'ิ' && c <= 'ื') || c == '็' || c == 'ํ';

    // ่ ้ ๊ ๋ ์ ๎
    private static bool IsTone(char c) => (c >= '่' && c <= '์') || c == '๎';

    // ุ ู ฺ
    private static bool IsLower(char c) => c >= 'ุ' && c <= 'ฺ';

    // ป ฝ ฟ ฬ
    private static bool IsTall(char c) => c == 'ป' || c == 'ฝ' || c == 'ฟ' || c == 'ฬ';
}
