using System.Collections;
using System.Collections.Generic;
using Enum;
using Player;
using TMPro;
using UnityEngine;

namespace Rule6
{
    /// <summary>
    /// shot "หยิบโทรศัพท์ขึ้นมา" ของคัทซีนบทจบ — ยกโทรศัพท์เครื่องจริงขึ้นมาในมุมสายตาผู้เล่น
    ///
    /// ยกเครื่องขึ้นจากใต้กรอบภาพ ฉากหลังดำทั้งหมด เห็นแค่ตัวเครื่อง แล้วจอค่อยๆ สว่างจนสว่าง
    /// กว่าตอนเล่นปกติ — จอเปิดอยู่ที่ "แชทใหม่" (หน้าแชทเปล่า หัวแชทเป็นหมายเลขที่ไม่รู้จัก)
    /// ส่วนการพิมพ์จริงเป็น shot ถัดไป ซึ่งตัดไป UI เต็มจอ (EndingChat)
    ///
    /// หน้าแชทเปล่าทำด้วยการซ่อนของ ไม่ใช่ลบ: ปิด ChatUIController ไว้ไม่ให้วาดทับ ซ่อนแถบฟอง
    /// ข้อความ / แผงตัวเลือก / รูปโปรไฟล์ แล้วเปลี่ยนชื่อหัวแชท — ประวัติแชทจริงไม่ถูกแตะเลย
    ///
    /// ระหว่างแสดง PhoneSystem ถูกพักไว้ ผู้เล่นกด Tab / Backspace เก็บโทรศัพท์กลางคัทซีนไม่ได้
    /// Hide() คืนทุกอย่าง (กล้อง ตำแหน่งเครื่อง ความสว่างจอ ระบบโทรศัพท์) แล้วเก็บโทรศัพท์
    /// </summary>
    public class EndingPhone : MonoBehaviour
    {
        [Header("โทรศัพท์")]
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

        [Tooltip("ตัวเครื่อง (Phone Object ของ PhoneSystem) — ใช้ยกขึ้น และใช้หาว่าต้องวาด layer ไหนตอนฉากหลังดำ")]
        [SerializeField] private Transform phoneRoot;

        [Tooltip("ScreenQuad — แผ่นจอที่โชว์ภาพของโทรศัพท์")]
        [SerializeField] private Renderer screenRenderer;

        [Header("หน้าจอ: แชทใหม่")]
        [Tooltip("✓ = เปิดหน้าแชทเปล่า  ✗ = หน้า Home ของเครื่อง")]
        [SerializeField] private bool openNewChat = true;

        [Tooltip("ปิดไว้ระหว่างแสดง — ปกติคือ ChatUIController (ไม่ให้แชทจริงวาดทับ)")]
        [SerializeField] private Behaviour[] pauseWhileShowing;

        [Tooltip("ซ่อนไว้ระหว่างแสดง — แถบฟองข้อความ (Content), แผงตัวเลือก (ReplyPanel), รูปโปรไฟล์")]
        [SerializeField] private GameObject[] hideWhileShowing;

        [SerializeField] private TMP_Text headerName;

        [SerializeField] private string headerText = "หมายเลขที่ไม่รู้จัก";

        [Header("ยกเครื่องขึ้น")]
        [Tooltip("ยกขึ้นจากต่ำกว่าตำแหน่งปกติเท่านี้ (เมตร)")]
        [SerializeField] private float raiseFrom = 0.35f;

        [SerializeField] private float raiseSeconds = 0.8f;

        [Header("ตำแหน่งในภาพ")]
        [Tooltip("✓ = ยกมาไว้กลางภาพ (ตอนเล่นปกติเครื่องถือไว้ทางขวา ซึ่งใน shot นี้ดูเบี้ยว)\n" +
                 "✗ = ใช้ตำแหน่งที่ถือตอนเล่นปกติ")]
        [SerializeField] private bool centerInView = true;

        [Tooltip("เลื่อนจากกลางภาพ (เมตร) — X = ขวา, Y = ขึ้น")]
        [SerializeField] private Vector2 viewOffset = new Vector2(0f, -0.01f);

        [Tooltip("ระยะจากกล้องถึงจอ (เมตร) — 0 = ระยะเดิมที่ถือตอนเล่นปกติ, ค่าน้อย = ใกล้/ใหญ่ขึ้น")]
        [SerializeField] private float viewDepth = 0f;

        [Header("ฉากหลังดำ")]
        [Tooltip("✓ = กล้องวาดแค่ตัวโทรศัพท์ ที่เหลือดำหมด")]
        [SerializeField] private bool blackBackground = true;

        [Tooltip("layer ที่ย้ายโทรศัพท์ไปอยู่ชั่วคราวตอนฉากหลังดำ — ต้องเป็น layer ที่ไม่มีของอื่นอยู่\n" +
                 "-1 = หา layer ว่าง (ไม่มีชื่อ) ให้เอง")]
        [SerializeField] private int isolateLayer = -1;

        [Header("แสงจากจอ")]
        [Tooltip("ความสว่างของจอเทียบกับปกติ — 1 = เท่าตอนเล่น, เกิน 1 = สว่างจ้า (มี Bloom จะเรืองแสง)")]
        [SerializeField] private float screenBrightness = 2.2f;

        [Tooltip("จอค่อยๆ สว่างขึ้นหลังยกเครื่องเสร็จ (วินาที) — 0 = สว่างทันที")]
        [SerializeField] private float wakeSeconds = 0.6f;

        [Tooltip("แสงที่ส่องออกมาจากจอ — 0 = ไม่มี")]
        [SerializeField] private float lightIntensity = 1.2f;

        [SerializeField] private float lightRange = 1.5f;

        [SerializeField] private Color lightColor = new Color(0.75f, 0.85f, 1f);

        [Header("แสงเรืองรอบเครื่อง")]
        [Tooltip("ความเข้มของแสงฟุ้งๆ บนพื้นดำหลังโทรศัพท์ — 0 = ไม่มี")]
        [Range(0f, 1f)]
        [SerializeField] private float haloAlpha = 0.55f;

        [Tooltip("ขนาดของแสงฟุ้ง เทียบกับความสูงของภาพ (1 = สูงเท่าจอ)")]
        [SerializeField] private float haloSize = 1.1f;

        [SerializeField] private Color haloColor = new Color(0.35f, 0.5f, 0.85f);

        // ─────────────────────────── Runtime ───────────────────────────

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private bool    _showing;
        private bool    _darkened;
        private bool    _pausedSystem;
        private Camera  _camera;
        private int     _oldMask;
        private CameraClearFlags _oldClear;
        private Color   _oldBackground;
        private Vector3 _restLocalPos;
        private Vector3 _targetLocalPos;
        private int     _layer;
        private Light   _light;
        private Canvas  _halo;
        private UnityEngine.UI.Image _haloImage;
        private static Sprite _haloSprite;
        private Color   _screenBase = Color.white;
        private MaterialPropertyBlock _block;
        private string  _oldHeader;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private readonly List<Behaviour>  _paused = new List<Behaviour>();
        private readonly Dictionary<GameObject, int> _oldLayers = new Dictionary<GameObject, int>();

        public bool IsShowing => _showing;

        /// <summary>ยกโทรศัพท์ขึ้นมา — yield จนยกเสร็จและจอสว่างเต็มที่</summary>
        public IEnumerator Show()
        {
            if (_showing) yield break;

            if (phoneSystem == null)
            {
                Debug.LogError($"[EndingPhone] '{name}' ยังไม่ได้ใส่ Phone System — ยกโทรศัพท์ไม่ได้", this);
                yield break;
            }

            _showing = true;
            if (phoneRoot != null) _restLocalPos = phoneRoot.localPosition;

            phoneSystem.RaisePhone();

            // พักหลังยกแล้ว — ต้องให้ PhoneSystem เปิดเครื่องก่อน แต่หลังจากนั้นห้ามรับปุ่ม
            // ไม่งั้นผู้เล่นกด Tab แล้วโทรศัพท์หายกลางคัทซีน
            _pausedSystem = phoneSystem.enabled;
            phoneSystem.enabled = false;

            if (openNewChat) ShowNewChat();

            var pc = PlayerController.Instance;
            _camera = pc != null && pc.CameraPivot != null ? pc.CameraPivot.GetComponentInChildren<Camera>(true) : null;
            if (_camera == null) _camera = Camera.main;

            if (blackBackground) DarkenEverythingButPhone();

            _targetLocalPos = centerInView ? CenteredLocalPosition() : _restLocalPos;

            SetScreen(0f);   // จอดับไว้ก่อน แล้วค่อยติดตอนยกเสร็จ

            yield return Raise();
            yield return Wake();
        }

        /// <summary>คืนทุกอย่างกลับเหมือนก่อนคัทซีน แล้วเก็บโทรศัพท์</summary>
        public void Hide()
        {
            if (!_showing) return;
            _showing = false;

            StopAllCoroutines();

            if (phoneRoot != null) phoneRoot.localPosition = _restLocalPos;
            ClearScreen();
            if (_light != null) _light.enabled = false;
            if (_halo  != null) _halo.gameObject.SetActive(false);

            if (_camera != null && _darkened)
            {
                _darkened               = false;
                _camera.cullingMask     = _oldMask;
                _camera.clearFlags      = _oldClear;
                _camera.backgroundColor = _oldBackground;
            }

            foreach (var pair in _oldLayers) if (pair.Key != null) pair.Key.layer = pair.Value;
            _oldLayers.Clear();

            if (headerName != null && _oldHeader != null) headerName.text = _oldHeader;
            _oldHeader = null;
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            foreach (var b  in _paused) if (b  != null) b.enabled = true;
            _hidden.Clear();
            _paused.Clear();

            if (phoneSystem != null)
            {
                if (_pausedSystem) phoneSystem.enabled = true;
                phoneSystem.LowerPhone();
            }
        }

        private void OnDisable() => Hide();

        // ─────────────────────────── ยกเครื่อง ───────────────────────────

        private IEnumerator Raise()
        {
            if (phoneRoot == null) yield break;

            if (raiseSeconds > 0f && raiseFrom > 0f)
            {
                // ยกในแนวแกนตั้งของกล้อง ไม่ใช่แกนตั้งของโลก — "ล่าง" คือใต้กรอบภาพ
                Vector3 camUp = _camera != null ? _camera.transform.up : Vector3.up;
                Vector3 down  = phoneRoot.parent != null ? phoneRoot.parent.InverseTransformDirection(-camUp) : -camUp;
                Vector3 from  = _targetLocalPos + down.normalized * raiseFrom;

                float t = 0f;
                while (t < raiseSeconds)
                {
                    t += Time.deltaTime;
                    phoneRoot.localPosition = Vector3.Lerp(from, _targetLocalPos, Mathf.SmoothStep(0f, 1f, t / raiseSeconds));
                    yield return null;
                }
            }

            phoneRoot.localPosition = _targetLocalPos;
        }

        /// <summary>
        /// ตำแหน่งของตัวเครื่องที่ทำให้ "กลางจอโทรศัพท์" อยู่กลางภาพ
        ///
        /// วัดจากกลางจอ ไม่ใช่จาก pivot ของเครื่อง — pivot ของโมเดลอยู่ตรงไหนก็ได้ (มักอยู่ที่ขอบ
        /// หรือก้นเครื่อง) ถ้าเอา pivot ไปไว้กลางภาพ จอจะยังเบี้ยวอยู่ดี
        /// </summary>
        private Vector3 CenteredLocalPosition()
        {
            if (phoneRoot == null || _camera == null) return _restLocalPos;

            Transform cam    = _camera.transform;
            Vector3   center = screenRenderer != null ? screenRenderer.bounds.center : phoneRoot.position;

            float depth = viewDepth > 0f ? viewDepth : Vector3.Dot(center - cam.position, cam.forward);
            if (depth < 0.05f) depth = 0.4f;

            Vector3 desired = cam.position + cam.forward * depth + cam.right * viewOffset.x + cam.up * viewOffset.y;
            Vector3 world   = phoneRoot.position + (desired - center);

            return phoneRoot.parent != null ? phoneRoot.parent.InverseTransformPoint(world) : world;
        }

        // ─────────────────────────── จอสว่าง ───────────────────────────

        private IEnumerator Wake()
        {
            EnsureLight();
            EnsureHalo();

            float t = 0f;
            while (t < wakeSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / wakeSeconds);
                SetScreen(k);
                SetHalo(k);
                if (_light != null) _light.intensity = lightIntensity * k;
                yield return null;
            }

            SetScreen(1f);
            SetHalo(1f);
            if (_light != null) _light.intensity = lightIntensity;
        }

        // ─────────────────────────── แสงเรืองรอบเครื่อง ───────────────────────────

        /// <summary>
        /// แสงฟุ้งสีฟ้าบนพื้นดำหลังโทรศัพท์ — เหมือนแสงจอที่กระจายออกไปในห้องมืด
        ///
        /// เป็น Canvas แบบ Screen Space - Camera ที่วางลึกกว่าเครื่อง จึงถูกตัวเครื่องบังเหมือนของ
        /// ในฉากจริง (แสงอยู่ "หลัง" โทรศัพท์) ใช้ shader UI ปกติ ไม่ต้องมี material พิเศษที่อาจ
        /// หายไปตอน build — และอยู่ layer เดียวกับเครื่อง กล้องที่วาดแค่ layer นั้นจึงยังเห็นมัน
        /// </summary>
        private void EnsureHalo()
        {
            if (haloAlpha <= 0f || _camera == null) return;

            if (_halo == null)
            {
                var go = new GameObject("Screen Halo", typeof(RectTransform), typeof(Canvas));
                go.transform.SetParent(transform, false);
                _halo = go.GetComponent<Canvas>();

                var img = new GameObject("Glow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                img.transform.SetParent(go.transform, false);
                _haloImage = img.GetComponent<UnityEngine.UI.Image>();
                _haloImage.sprite        = HaloSprite();
                _haloImage.raycastTarget = false;
            }

            _halo.renderMode  = RenderMode.ScreenSpaceCamera;
            _halo.worldCamera = _camera;

            int layer = _darkened ? _layer : (phoneRoot != null ? phoneRoot.gameObject.layer : 0);
            _halo.gameObject.layer      = layer;
            _haloImage.gameObject.layer = layer;

            _halo.gameObject.SetActive(true);
            SetHalo(0f);
        }

        private void SetHalo(float k)
        {
            if (_haloImage == null || _camera == null) return;

            PlaceHalo();

            Color c = haloColor;
            c.a = haloAlpha * k;
            _haloImage.color = c;
        }

        /// <summary>
        /// วางแสงไว้ตรงกลางจอโทรศัพท์ ไม่ใช่กลางภาพ (เอาติ๊ก Center In View ออกก็ยังตามเครื่อง)
        /// วางซ้ำทุกเฟรมของช่วงจอติด เพราะขนาดของ Canvas ที่เพิ่งสร้างยังไม่ถูกต้องในเฟรมแรก
        /// </summary>
        private void PlaceHalo()
        {
            Transform cam = _camera.transform;
            Vector3 center = screenRenderer != null ? screenRenderer.bounds.center
                           : phoneRoot != null ? phoneRoot.position : cam.position + cam.forward;
            float depth = Vector3.Dot(center - cam.position, cam.forward);

            _halo.planeDistance = Mathf.Min(depth + 0.3f, _camera.farClipPlane * 0.9f);

            var rt   = (RectTransform)_haloImage.transform;
            var size = ((RectTransform)_halo.transform).rect.size;
            Vector3 vp = _camera.WorldToViewportPoint(center);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
            rt.sizeDelta        = Vector2.one * size.y * haloSize;
        }

        /// <summary>วงกลมฟุ้งขาว ขอบจางเป็นศูนย์ — สร้างครั้งเดียวใช้ทั้งเกม</summary>
        private static Sprite HaloSprite()
        {
            if (_haloSprite != null) return _haloSprite;

            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name       = "Ending Phone Halo",
            };

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;
                float r  = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float a  = 1f - Mathf.SmoothStep(0f, 1f, r);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _haloSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return _haloSprite;
        }

        /// <summary>
        /// ความสว่างของจอ 0..1 ของทางจาก "ดับ" ไปถึง Screen Brightness
        ///
        /// ใช้ MaterialPropertyBlock ไม่แก้ material ตรงๆ — material ของจอเป็นแอสเซต แก้ตอนเล่น
        /// ใน Editor ค่าจะค้างอยู่ในไฟล์หลังกด Stop แล้วจอจะสว่างจ้าตลอดไป
        /// </summary>
        private void SetScreen(float k)
        {
            if (screenRenderer == null) return;

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
                var mat = screenRenderer.sharedMaterial;
                if (mat != null && mat.HasProperty(BaseColor)) _screenBase = mat.GetColor(BaseColor);
            }

            Color c = _screenBase * Mathf.Lerp(0.05f, screenBrightness, k);
            c.a = _screenBase.a;

            screenRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, c);
            screenRenderer.SetPropertyBlock(_block);
        }

        private void ClearScreen()
        {
            if (screenRenderer == null) return;
            screenRenderer.SetPropertyBlock(null);
            _block = null;
        }

        /// <summary>ไฟดวงเล็กระหว่างจอกับกล้อง — ให้ตัวเครื่อง (และมือ ถ้ามีวันหลัง) ติดแสงสีจอ</summary>
        private void EnsureLight()
        {
            if (lightIntensity <= 0f || screenRenderer == null) return;

            if (_light == null)
            {
                var go = new GameObject("Screen Light");
                go.transform.SetParent(screenRenderer.transform, false);
                _light = go.AddComponent<Light>();
                _light.type    = LightType.Point;
                _light.shadows = LightShadows.None;
            }

            Vector3 toCam = _camera != null
                ? (_camera.transform.position - screenRenderer.bounds.center).normalized
                : -screenRenderer.transform.forward;
            _light.transform.position = screenRenderer.bounds.center + toCam * 0.08f;

            _light.color     = lightColor;
            _light.range     = lightRange;
            _light.intensity = 0f;
            _light.enabled   = true;
        }

        // ─────────────────────────── ฉากหลังดำ ───────────────────────────

        /// <summary>
        /// ให้กล้องวาดแค่ตัวโทรศัพท์ แล้วพื้นหลังเป็นสีดำ
        ///
        /// ย้ายโทรศัพท์ไปอยู่ layer ว่างชั่วคราว แล้วให้กล้องวาดแค่ layer นั้น — ใช้ layer เดิมของ
        /// เครื่องไม่ได้ เพราะเครื่องอยู่ Default เหมือนฉากทั้งฉาก ถ้าวาด Default ก็เห็นทุกอย่าง
        /// </summary>
        private void DarkenEverythingButPhone()
        {
            if (_camera == null) return;

            int layer = isolateLayer >= 0 ? isolateLayer : FindEmptyLayer();
            if (layer < 0)
            {
                Debug.LogWarning($"[EndingPhone] '{name}' ไม่มี layer ว่างให้ใช้ — ไม่ทำฉากหลังดำ " +
                                 "(ใส่ Isolate Layer เอง หรือเว้น layer ไว้ใน Tags and Layers สักช่อง)", this);
                return;
            }

            _oldLayers.Clear();
            if (phoneRoot != null)
                foreach (var r in phoneRoot.GetComponentsInChildren<Renderer>(true))
                    MoveToLayer(r.gameObject, layer);
            if (screenRenderer != null) MoveToLayer(screenRenderer.gameObject, layer);

            if (_oldLayers.Count == 0)
            {
                Debug.LogWarning($"[EndingPhone] '{name}' หา renderer ของโทรศัพท์ไม่เจอ — ไม่ทำฉากหลังดำ", this);
                return;
            }

            _oldMask       = _camera.cullingMask;
            _oldClear      = _camera.clearFlags;
            _oldBackground = _camera.backgroundColor;

            _layer = layer;
            _camera.cullingMask     = 1 << layer;
            _camera.clearFlags      = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _darkened = true;
        }

        private void MoveToLayer(GameObject go, int layer)
        {
            if (_oldLayers.ContainsKey(go)) return;
            _oldLayers[go] = go.layer;
            go.layer = layer;
        }

        /// <summary>layer แรกที่ไม่มีชื่อใน Tags and Layers = ไม่มีใครใช้ (8 ขึ้นไป — 0-7 เป็นของ Unity)</summary>
        private static int FindEmptyLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        // ─────────────────────────── แชทใหม่ ───────────────────────────

        /// <summary>
        /// เปิดหน้าแชทแล้วทำให้มันดูเป็นแชทใหม่ที่ยังว่าง — ปิดระบบแชทก่อนเปลี่ยนหน้า
        /// ไม่งั้นมันจะวาดประวัติแชทของคนปริศนาขึ้นมาในเฟรมเดียวกัน
        /// </summary>
        private void ShowNewChat()
        {
            _paused.Clear();
            if (pauseWhileShowing != null)
                foreach (var b in pauseWhileShowing)
                    if (b != null && b.enabled) { b.enabled = false; _paused.Add(b); }

            phoneSystem.ChangeState(PhoneState.ChatView);

            _hidden.Clear();
            if (hideWhileShowing != null)
                foreach (var go in hideWhileShowing)
                    if (go != null && go.activeSelf) { go.SetActive(false); _hidden.Add(go); }

            if (headerName != null)
            {
                _oldHeader      = headerName.text;
                headerName.text = L10n.T(headerText);
            }
        }
    }
}
