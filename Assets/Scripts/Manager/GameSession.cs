using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// กลับหน้าเมนูหลักกลางเกม — ใช้จากปุ่มในจอ Game Over / บทจบ และในเมนู Esc (ผ่าน MainMenuButton)
///
/// LoadScene อย่างเดียวไม่พอ เพราะสองเรื่อง:
///
///   1. ตัวหลักของเกมเป็น DontDestroyOnLoad (ผู้เล่น, TimeManager, GameModeController, AudioManager,
///      HeartbeatSystem, HorrorTextUI) — โหลดซีนใหม่แล้วมันยังอยู่ ผู้เล่นจะไปโผล่ในหน้าเมนู
///      แล้วพอกด Start อีกรอบ ตัวในซีนใหม่จะทำลายตัวเองทิ้งเพราะเห็นว่ามีตัวเก่าอยู่แล้ว ตัวเก่า
///      ที่ชี้ไปหาของในซีนรอบก่อน (ซึ่งถูกทำลายไปแล้ว) จึงได้เล่นต่อ → เกมรอบสองพัง
///      ต้องทำลายพวกนี้ทิ้งก่อน ให้รอบหน้าสร้างใหม่จากซีนสะอาดๆ
///
///   2. ค่า static ไม่ถูกล้างเองตอนเปลี่ยนซีน (เหมือนตอนปิด domain reload ใน Editor) — ล้างด้วยเมธอด
///      ResetStatic ตัวเดียวกับที่รันตอนกด Play (นับรอบวนลูปคืน, สมุดที่ปลดล็อก, ธงคัทซีน ฯลฯ)
///      ถ้าเพิ่ม static ที่เป็น "สถานะของเกมรอบนี้" ใหม่ ให้ใส่การล้างใน ResetGameStatics ด้วย
///      (ห้ามใส่ L10n / MasterVolume — นั่นเป็นค่าของตัวเกม ไม่ใช่ของรอบที่เล่น)
/// </summary>
public static class GameSession
{
    /// <summary>ชื่อซีนเมนูหลัก (ต้องอยู่ใน Build Settings)</summary>
    public const string MainMenuScene = "MainMenu";

    /// <summary>กำลังโหลดกลับเมนูอยู่ — กันกดปุ่มซ้ำ</summary>
    public static bool IsReturning { get; private set; }

    /// <summary>ยิงก่อนเริ่มล้างทุกอย่าง — ระบบไหนต้องเก็บกวาดเพิ่มเองมาเกาะตรงนี้ได้</summary>
    public static event Action BeforeReturnToMainMenu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic()
    {
        IsReturning            = false;
        BeforeReturnToMainMenu = null;
        _persistent.Clear();
        SceneManager.sceneLoaded -= OnMainMenuLoaded;
    }

    /// <summary>กลับหน้าเมนูหลัก ล้างสถานะของเกมรอบนี้ทั้งหมด</summary>
    public static void ReturnToMainMenu()
    {
        if (IsReturning) return;
        IsReturning = true;

        try { BeforeReturnToMainMenu?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }

        // เมนู Esc หยุดเวลา/เสียงไว้ — คืนก่อน ไม่งั้นหน้าเมนูหลักจะค้างนิ่งและเงียบ
        Time.timeScale = 1f;
        StopAllSound();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // ล้าง static ก่อนซีนถูกปิด — InGameLanguageMenu.OnDisable จะเรียก Close() ถ้ายังเห็นว่า
        // เมนูเปิดอยู่ แล้วไปคืน timeScale/ล็อกเมาส์ตามค่าที่จำไว้ ทับค่าที่เพิ่งตั้งข้างบน
        ResetGameStatics();

        // ปิดก่อน ทำลายทีหลัง: ตอนซีนเกมถูกปิด ของในซีนยังเรียก AudioManager.instance ฯลฯ ใน OnDisable
        // ถ้าทำลายตอนนี้เลย พวกนั้นจะเจอ null แล้ว error เต็ม Console — ปิดไว้ก่อน (กล้อง/เสียงของผู้เล่น
        // ไม่ทับหน้าเมนู) แล้วค่อยทำลายตอนเมนูโหลดเสร็จ (หน้าเมนูไม่มีตัวพวกนี้ของตัวเอง)
        _persistent.Clear();
        CollectPersistentObjects();
        foreach (var go in _persistent) go.SetActive(false);

        SceneManager.sceneLoaded += OnMainMenuLoaded;
        SceneManager.LoadScene(MainMenuScene);
    }

    private static readonly System.Collections.Generic.List<GameObject> _persistent =
        new System.Collections.Generic.List<GameObject>();

    private static void OnMainMenuLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnMainMenuLoaded;

        foreach (var go in _persistent)
            if (go != null) UnityEngine.Object.Destroy(go);
        _persistent.Clear();

        IsReturning      = false;
        Time.timeScale   = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private static void StopAllSound()
    {
        try
        {
            FMODUnity.RuntimeManager.PauseAllEvents(false);
            FMODUnity.RuntimeManager.GetBus("bus:/").stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameSession] หยุดเสียง FMOD ไม่ได้: " + e.Message);
        }
    }

    private static void ResetGameStatics()
    {
        InGameLanguageMenu.ResetStatic();
        CutsceneManager.ResetStatics();
        RuleSystem.RuleBase.ResetStatics();
        RuleSystem.RuleDevTools.ResetStatics();
        RuleSystem.Rule.Rule6.ResetNightLoopCount();
        Rule2.LeverPuzzleController.ResetStatics();
        Book.BookPageTurner.ResetStatics();
        Book.BookVideoController.ResetStatics();
    }

    /// <summary>
    /// ตัวหลักที่เป็น DontDestroyOnLoad — เฉพาะตัวของเกมเอง
    /// ไม่แตะ DontDestroyOnLoad ของ FMOD / Localization (ทำลายแล้วเสียงหรือภาษาจะพังทั้งเกม)
    /// </summary>
    private static void CollectPersistentObjects()
    {
        Collect<Player.PlayerController>();
        Collect<Manager.TimeManager>();
        Collect<GameModeController>();
        Collect<AudioManager>();
        Collect<global::System.HeartbeatSystem.HeartbeatSystem>();
        Collect<HorrorTextUI>();
    }

    private static void Collect<T>() where T : Component
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;

            var root = c.transform.root.gameObject;
            if (root.scene.name != "DontDestroyOnLoad") continue;   // ตัวที่อยู่ในซีนปกติ หายไปพร้อมซีนเอง
            if (!_persistent.Contains(root)) _persistent.Add(root);
        }
    }
}
