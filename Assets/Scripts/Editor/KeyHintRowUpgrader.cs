using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// แปลง prefab แถวคำใบ้ปุ่มแบบเก่า (Key เป็น TMP เฉยๆ) ให้รองรับ prefab ปุ่มจาก KeyIconLibrary
///
/// โครงเดิม                     โครงหลังแปลง
///   Row  [HLG]                   Row  [HLG] + KeyHintRow
///   ├─ Key    (TMP)              ├─ KeyCap [HLG+CSF] + KeyCapView
///   └─ Action (TMP)              │  └─ Key (TMP)        ← ตัวสำรองตอนปุ่มนั้นไม่มี prefab
///                                └─ Action (TMP)
///
/// KeyCap เป็นที่ให้ prefab ปุ่มไปเกิด ส่วน Key ตัวเดิมถูกย้ายลงไปเป็นลูกแล้วถูกซ่อนอัตโนมัติ
/// ตอนที่ปุ่มนั้นมี prefab — เลยไม่เสียของเดิม ปุ่มที่ยังไม่ได้ทำ prefab ก็ยังอ่านออก
///
/// ทำกับ prefab ที่เลือกไว้ใน Project ทีละหลายอันได้ เรียกซ้ำไม่มีผล (ข้ามตัวที่ทำแล้ว)
/// </summary>
public static class KeyHintRowUpgrader
{
    private const string MenuPath = "Rule of Horror/Phone/ติดตั้งภาพปุ่มให้ prefab แถวคำใบ้";

    [MenuItem(MenuPath)]
    private static void Upgrade()
    {
        var paths = Selection.objects
            .OfType<GameObject>()
            .Select(AssetDatabase.GetAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab"))
            .Distinct()
            .ToList();

        if (paths.Count == 0)
        {
            Debug.LogWarning("[KeyHintRow] ยังไม่ได้เลือก prefab — เลือกแถวคำใบ้ใน Project " +
                             "(เช่น Keyprompt_Text, YourHand) แล้วสั่งเมนูนี้อีกที");
            return;
        }

        int done = 0, skipped = 0;
        foreach (var path in paths)
        {
            switch (UpgradeOne(path))
            {
                case Result.Upgraded: done++;   break;
                case Result.AlreadyDone: skipped++; break;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KeyHintRow] ติดตั้งให้ {done} prefab (ข้ามที่ทำไว้แล้ว {skipped})");
    }

    private enum Result { Upgraded, AlreadyDone, Failed }

    private static Result UpgradeOne(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // ทำไว้แล้วก็ยังต้องวิ่งผ่าน FixLayout — เผื่อค่า layout เพี้ยนหรือเพิ่ม KeyCap มาทีหลัง
            if (root.GetComponent<KeyHintRow>() != null)
            {
                FixLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[KeyHintRow] จัด layout ใหม่ให้: {path}");
                return Result.AlreadyDone;
            }

            // ลำดับใน hierarchy คือลำดับที่ตาเห็น — ตัวแรกคือปุ่ม ตัวที่สองคือคำอธิบาย
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 0)
            {
                Debug.LogError($"[KeyHintRow] '{path}' ไม่มี TMP_Text สักตัว — ข้าม");
                return Result.Failed;
            }

            TMP_Text keyText    = texts[0];
            TMP_Text actionText = texts.Length > 1 ? texts[1] : null;

            var keyCap = WrapInKeyCap(keyText);

            var view = keyCap.AddComponent<KeyCapView>();
            var so   = new SerializedObject(view);
            so.FindProperty("keyText").objectReferenceValue = keyText;
            // iconSlot เว้นว่าง = ให้ prefab ปุ่มไปเกิดใต้ KeyCap เอง ซึ่งคือสิ่งที่ต้องการพอดี
            so.ApplyModifiedPropertiesWithoutUndo();

            var row   = root.AddComponent<KeyHintRow>();
            var rowSo = new SerializedObject(row);
            var caps  = rowSo.FindProperty("keyCaps");
            caps.arraySize = 1;
            caps.GetArrayElementAtIndex(0).objectReferenceValue = view;
            rowSo.FindProperty("actionText").objectReferenceValue = actionText;
            rowSo.ApplyModifiedPropertiesWithoutUndo();

            FixLayout(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[KeyHintRow] ติดตั้งแล้ว: {path}" +
                      (actionText == null ? "  (ไม่เจอช่องคำอธิบาย — ใส่เองใน Inspector ได้)" : ""));
            return Result.Upgraded;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// จัดกฎ layout ของทั้งแถวให้ถูก — ส่วนนี้คือสิ่งที่ทำให้ปุ่มกับคำอธิบายไม่ทับกัน
    ///
    /// กติกาที่ต้องตรงกันทั้งสาย: ใครคุมขนาดลูก ลูกคนนั้นห้ามมี ContentSizeFitter ของตัวเอง
    ///   แถว  : HLG คุมขนาดลูก (KeyCap, Action) + CSF ของตัวเองเพื่อหดตามเนื้อหา
    ///   KeyCap: มี HLG ไว้รายงานขนาดที่ต้องการให้แถว แต่ห้ามมี CSF เพราะแถวเป็นคนตั้งขนาดให้แล้ว
    ///
    /// ของเดิมตั้ง Control Child Size ✗ / Force Expand ✓ ซึ่งกลับกันพอดี — แถวจึงอ่านความกว้าง
    /// ลูกได้ 0 แล้วเอาทุกตัวไปวางซ้อนกันที่จุดเดียว
    /// </summary>
    private static void FixLayout(GameObject root)
    {
        var group = root.GetComponent<HorizontalLayoutGroup>();
        if (group != null)
        {
            group.childControlWidth      = true;
            group.childControlHeight     = true;
            group.childForceExpandWidth  = false;
            group.childForceExpandHeight = false;
            group.childAlignment         = TextAnchor.MiddleLeft;
            if (group.spacing < 1f) group.spacing = 12f;
        }

        // ไม่มี CSF แถวจะติดอยู่ที่ขนาดเดิม (200x50) คำอธิบายยาวๆ จะตัดบรรทัดแล้วล้นทับปุ่ม
        var fitter = root.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var cap in root.GetComponentsInChildren<KeyCapView>(true))
        {
            var capFitter = cap.GetComponent<ContentSizeFitter>();
            if (capFitter != null) Object.DestroyImmediate(capFitter, true);

            var capGroup = cap.GetComponent<HorizontalLayoutGroup>();
            if (capGroup == null) continue;

            capGroup.childControlWidth      = true;
            capGroup.childControlHeight     = true;
            capGroup.childForceExpandWidth  = false;
            capGroup.childForceExpandHeight = false;
        }
    }

    /// <summary>
    /// ยัด TMP ของปุ่มลงไปอยู่ใต้ GameObject ใหม่ชื่อ KeyCap โดยไม่ขยับตำแหน่งในแถว
    ///
    /// KeyCap ได้ layout group + ContentSizeFitter เพื่อให้มันหดขยายตามของที่อยู่ข้างใน
    /// ไม่ว่าจะเป็น prefab ปุ่ม 70x70 หรือตัวหนังสือสำรอง — แถวจะได้ไม่มีช่องว่างค้าง
    /// </summary>
    private static GameObject WrapInKeyCap(TMP_Text keyText)
    {
        var parent  = keyText.transform.parent;
        int sibling = keyText.transform.GetSiblingIndex();

        var keyCap = new GameObject("KeyCap", typeof(RectTransform));
        keyCap.layer = keyText.gameObject.layer;
        keyCap.transform.SetParent(parent, false);
        keyCap.transform.SetSiblingIndex(sibling);

        var group = keyCap.AddComponent<HorizontalLayoutGroup>();
        group.childControlWidth       = true;
        group.childControlHeight      = true;
        group.childForceExpandWidth   = false;
        group.childForceExpandHeight  = false;
        group.padding                 = new RectOffset(0, 0, 0, 0);
        group.spacing                 = 0f;
        group.childAlignment          = TextAnchor.MiddleCenter;

        // ไม่ใส่ ContentSizeFitter ตรงนี้ — แถวเป็นคนตั้งขนาดให้ KeyCap ถ้าใส่จะแย่งกันคุม
        keyText.transform.SetParent(keyCap.transform, false);
        return keyCap;
    }
}
