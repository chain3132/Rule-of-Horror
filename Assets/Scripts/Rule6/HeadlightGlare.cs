using UnityEngine;
using UnityEngine.UI;

namespace Rule6
{
    /// <summary>
    /// แสงจ้าของไฟหน้ารถที่พุ่งเข้าหาเรา (คัทซีนคันที่ 3) — วาดเป็น UI ทับจอ ตามตำแหน่งดวงไฟในโลก
    ///
    /// Spot light อย่างเดียวส่องได้แค่ของในรถ มองจากในรถจะไม่เห็นตัวดวงไฟเลย ตัวนี้เลยวาดดวงไฟ
    /// กับแสงฟุ้งรอบดวงให้เห็นจริงๆ ขนาดบนจอคิดจากระยะ (ไกลเป็นจุดเล็กๆ ใกล้แล้วโตพรวด)
    /// ส่วนความจ้าอ่านจาก intensity ของ Light ตัวเดียวกัน — ขั้น Light Ramp กับ Move To ของคัทซีน
    /// คุมทั้ง spot light และแสงบนจอไปพร้อมกัน ไม่ต้องมีขั้นแยก
    ///
    /// ไฟยังปิดอยู่ (Light.enabled = false) = ไม่วาดอะไร
    /// </summary>
    [DefaultExecutionOrder(1000)]   // หลังทุกอย่างที่ขยับกล้อง (หันมอง / กล้องสั่น) จะได้ไม่ช้าไปหนึ่งเฟรม
    [RequireComponent(typeof(Light))]
    public class HeadlightGlare : MonoBehaviour
    {
        [Tooltip("จุดดวงไฟแต่ละดวง (ลูกของ object นี้) — ว่างไว้ = ดวงเดียวที่ตำแหน่ง object นี้")]
        [SerializeField] private Transform[] lamps;

        [Tooltip("intensity ของ Light ที่ถือว่าจ้าสุด — ตั้งให้เท่ากับปลายทางของขั้น Light Ramp")]
        [SerializeField] private float fullIntensity = 60f;

        [Tooltip("ขนาดดวงไฟจริง (เมตร) — ใช้คิดว่าบนจอควรใหญ่แค่ไหนที่ระยะนั้นๆ")]
        [SerializeField] private float lampSize = 0.35f;

        [Tooltip("ตอนจ้าสุด แสงฟุ้งรอบดวงใหญ่กว่าตัวดวงไฟกี่เท่า")]
        [SerializeField] private float flareScale = 10f;

        [Tooltip("ขนาดเล็กสุดบนจอ (พิกเซล) — ตอนยังไกลมากจะได้ยังเห็นเป็นจุด")]
        [SerializeField] private float minPixels = 10f;

        [Tooltip("ความทึบของแสงขาวที่ฟุ้งเต็มจอตอนจ้าสุด (0 = ไม่มี)")]
        [Range(0f, 1f)]
        [SerializeField] private float washAlpha = 0.6f;

        [SerializeField] private Color color = new Color(1f, 0.96f, 0.88f);

        [Tooltip("ลำดับ canvas — ต้องต่ำกว่าแผ่น Fade ของคัทซีน (500) จอขาว/ดำของคัทซีนจะได้ทับแสงนี้")]
        [SerializeField] private int sortingOrder = 450;

        private Light           _light;
        private Canvas          _canvas;
        private RectTransform[] _flares;
        private Image[]         _flareImages;
        private Image           _wash;

        private void Awake() => _light = GetComponent<Light>();

        private void OnDisable()
        {
            if (_canvas != null) _canvas.enabled = false;
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void LateUpdate()
        {
            if (_light == null || !_light.enabled || !_light.gameObject.activeInHierarchy)
            {
                if (_canvas != null) _canvas.enabled = false;
                return;
            }

            Camera cam = ActiveCamera();
            if (cam == null) return;

            EnsureCanvas();
            _canvas.enabled = true;

            float bright = fullIntensity > 0f ? Mathf.Clamp01(_light.intensity / fullIntensity) : 1f;
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float maxPixels = cam.pixelHeight * 3f;

            for (int i = 0; i < _flares.Length; i++)
            {
                Transform lamp = LampAt(i);
                Vector3 sp = cam.WorldToScreenPoint(lamp.position);

                // อยู่ข้างหลังกล้อง = ไม่วาด
                if (sp.z <= 0.01f) { _flareImages[i].enabled = false; continue; }

                float pxPerMeter = cam.pixelHeight / (2f * sp.z * tanHalf);
                float core = Mathf.Max(minPixels, lampSize * pxPerMeter);
                float size = Mathf.Min(maxPixels, core * (1f + flareScale * bright));

                _flareImages[i].enabled = true;
                _flareImages[i].color   = new Color(color.r, color.g, color.b, Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(bright)));
                _flares[i].anchoredPosition = new Vector2(sp.x, sp.y);
                _flares[i].sizeDelta        = new Vector2(size, size);
            }

            _wash.color = new Color(color.r, color.g, color.b, washAlpha * bright);
        }

        private Transform LampAt(int i)
        {
            if (lamps != null && i < lamps.Length && lamps[i] != null) return lamps[i];
            return transform;
        }

        private static Camera ActiveCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (best == null || c.depth > best.depth) best = c;
            return best;
        }

        // ─────────────────────────── สร้าง UI ───────────────────────────

        private void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("Headlight Glare", typeof(RectTransform), typeof(Canvas));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            // แสงฟุ้งเต็มจออยู่ล่างสุด ดวงไฟทับข้างบน
            var wash = new GameObject("Wash", typeof(RectTransform), typeof(Image));
            wash.transform.SetParent(go.transform, false);
            var wrt = (RectTransform)wash.transform;
            wrt.anchorMin = Vector2.zero; wrt.anchorMax = Vector2.one;
            wrt.offsetMin = Vector2.zero; wrt.offsetMax = Vector2.zero;
            _wash = wash.GetComponent<Image>();
            _wash.raycastTarget = false;
            _wash.color = Color.clear;

            Sprite sprite = BuildGlareSprite();
            int count = lamps != null && lamps.Length > 0 ? lamps.Length : 1;
            _flares      = new RectTransform[count];
            _flareImages = new Image[count];

            for (int i = 0; i < count; i++)
            {
                var f = new GameObject($"Lamp {i + 1}", typeof(RectTransform), typeof(Image));
                f.transform.SetParent(go.transform, false);

                var rt = (RectTransform)f.transform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;   // พิกัดจอ เริ่มจากมุมซ้ายล่าง
                rt.pivot     = new Vector2(0.5f, 0.5f);

                var img = f.GetComponent<Image>();
                img.sprite        = sprite;
                img.raycastTarget = false;

                _flares[i]      = rt;
                _flareImages[i] = img;
            }
        }

        /// <summary>
        /// วงกลมไล่ความทึบ — แกนกลางขาวแน่น แล้วฟุ้งจางออกไปถึงขอบ
        /// สร้างตอนเล่น ไม่ต้องมีไฟล์ภาพ
        /// </summary>
        private static Sprite BuildGlareSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;
                float r  = Mathf.Sqrt(dx * dx + dy * dy);

                float halo = Mathf.Pow(Mathf.Clamp01(1f - r), 2.4f);
                float core = Mathf.Clamp01((0.14f - r) / 0.06f);
                float a    = Mathf.Clamp01(halo + core);

                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
