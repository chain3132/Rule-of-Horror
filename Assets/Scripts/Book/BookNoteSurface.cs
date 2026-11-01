using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Book
{
    /// <summary>
    /// จุดที่ Note หนึ่งอันไปปรากฏบนหน้ากระดาษ — quad บางๆ ที่เกาะกระดูกหน้ากระดาษ (Page1 ... Page7)
    ///
    /// ทำไมเป็น quad แยก ไม่ใช่เปลี่ยน material ของหน้ากระดาษ:
    ///   หน้ากระดาษทั้ง 7 หน้าของ TryNewBook เป็น mesh เดียวกัน ใช้ material "Pages" อันเดียวกัน
    ///   Page1-Page7 ใน Hierarchy เป็นกระดูก (bone) ที่พลิกหน้า ไม่ใช่ mesh — ไม่มี renderer ให้ใส่
    ///   material ต่อหน้าได้ quad ลูกของกระดูกจะพลิกไปพร้อมหน้ากระดาษเอง โดยไม่ต้องแตะโมเดล
    ///   หรือ animation เลย
    ///
    /// quad ใช้ shader "Rule of Horror/Book Ink" ซึ่ง "คูณ" ลงบนหน้ากระดาษ — พื้นขาวของวิดีโอ
    /// จึงหายไปเอง ลายกระดาษเดิมยังอยู่ครบ (ดูคำอธิบายใน BookInk.shader)
    ///
    /// ขนาด: ใส่ขนาดพื้นที่เขียนได้บนหน้ากระดาษ (Page Size) แล้ว quad จะย่อให้พอดีในกรอบนั้น
    /// ตามสัดส่วนของวิดีโอเอง — ภาพไม่ยืด ไม่บี้ ไม่ว่าหน้ากระดาษจะสัดส่วนเท่าไหร่
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class BookNoteSurface : MonoBehaviour
    {
        public enum PageSide { Front, Back }

        /// <summary>วิดีโอเป็นหมึกเข้มบนพื้นขาว หรือตัวหนังสือขาวบนพื้นดำ</summary>
        public enum InkStyle { Auto, DarkOnLight, LightOnDark }

        // ─────────────────────────── Note ───────────────────────────

        [Header("Note")]
        [Tooltip("เลข Note — ใช้หาไฟล์ BookVideos/NoteNN/NoteNN_TH.mp4 ตอนกด Link Note Videos")]
        [SerializeField] private int noteNumber = 1;

        [SerializeField] private VideoClip clipThai;
        [SerializeField] private VideoClip clipEnglish;

        [Tooltip("Auto = ดูจากเฟรมแรกของวิดีโอเองว่าพื้นขาวหรือพื้นดำ\n" +
                 "Dark On Light = หมึกเข้มบนพื้นขาว\n" +
                 "Light On Dark = ตัวหนังสือขาวบนพื้นดำ (export จาก After Effects แบบไม่มีพื้นหลัง)\n\n" +
                 "พื้นดำจะถูกตัดทิ้ง ตัวหนังสือลงสีตาม Ink Color ใน material BookInk")]
        [SerializeField] private InkStyle inkStyle = InkStyle.Auto;

        // ─────────────────────────── ตำแหน่ง ───────────────────────────

        [Header("อยู่หน้าไหน")]
        [Tooltip("ด้านของหน้ากระดาษที่ Note นี้อยู่\n\n" +
                 "Front = ด้านที่เห็นก่อนพลิกหน้านี้ (หน้าขวา)\n" +
                 "Back  = ด้านที่เห็นหลังพลิกหน้านี้ไปแล้ว (หน้าซ้าย)")]
        [SerializeField] private PageSide side = PageSide.Front;

        [Tooltip("เปิดหนังสือถึงหน้าคู่ที่เท่าไหร่ Note นี้ถึงจะเห็น\n" +
                 "-1 = คิดให้เองจากกระดูก PageN ที่เป็นพ่อ + ด้าน (ปกติไม่ต้องแก้)\n\n" +
                 "หน้าคู่ 0 = เพิ่งเปิดหนังสือ, 1 = พลิก Page1 แล้ว, 2 = พลิก Page2 แล้ว ...")]
        [SerializeField] private int spreadOverride = -1;

        // ─────────────────────────── ขนาด ───────────────────────────

        [Header("ขนาดบนหน้ากระดาษ (เมตร)")]
        [Tooltip("ขนาดพื้นที่ที่ให้ตัวหนังสือลงได้บนหน้ากระดาษ — กรอบเหลืองใน Scene view\n" +
                 "quad จริง (กรอบฟ้า) จะย่อให้อยู่ในกรอบนี้ตามสัดส่วนของวิดีโอ")]
        [SerializeField] private Vector2 pageSize = new Vector2(0.14f, 0.2f);

        [Tooltip("กลับภาพซ้าย-ขวา — ใช้เมื่อหัน quad แล้วตัวหนังสือออกมากลับด้าน")]
        [SerializeField] private bool flipHorizontal;

        [Tooltip("กลับภาพบน-ล่าง")]
        [SerializeField] private bool flipVertical;

        // สัดส่วนของวิดีโอจาก After Effects (1748 × 2480) — ใช้ตอนยังไม่ได้ใส่ clip
        private const float DefaultAspect = 1748f / 2480f;

        private static readonly int InkTexId    = Shader.PropertyToID("_InkTex");
        private static readonly int InkAmountId = Shader.PropertyToID("_InkAmount");
        private static readonly int InkUVId     = Shader.PropertyToID("_InkUV");
        private static readonly int InkInvertId = Shader.PropertyToID("_InkInvert");
        private static readonly int InkTexelId  = Shader.PropertyToID("_InkTexel");

        private MeshRenderer          _renderer;
        private MaterialPropertyBlock _block;

        // ─────────────────────────── ข้อมูลให้ BookVideoController ───────────────────────────

        public int NoteNumber => noteNumber;

        public InkStyle Style => inkStyle;

        public PageSide Side => side;

        /// <summary>วิดีโอของภาษานี้ — ไม่มีก็ใช้อีกภาษาแทน ดีกว่าหน้ากระดาษว่าง</summary>
        public VideoClip ClipFor(bool thai)
        {
            VideoClip wanted   = thai ? clipThai : clipEnglish;
            VideoClip fallback = thai ? clipEnglish : clipThai;
            return wanted != null ? wanted : fallback;
        }

        /// <summary>เลขของกระดูก PageN ที่ใกล้ที่สุดเหนือ quad นี้ (0 = ไม่ได้อยู่ใต้หน้าไหน)</summary>
        public int PageNumber
        {
            get
            {
                for (Transform t = transform.parent; t != null; t = t.parent)
                {
                    int n = ParsePageNumber(t.name);
                    if (n > 0) return n;
                }
                return 0;
            }
        }

        /// <summary>
        /// หน้าคู่ที่ Note นี้เห็น (-1 = ไม่รู้)
        ///
        /// พลิกไปแล้ว k หน้า: ซ้าย = หลังของ Page k, ขวา = หน้าของ Page k+1
        /// ด้านหน้าของ PageN จึงเห็นที่หน้าคู่ N-1 และด้านหลังเห็นที่หน้าคู่ N
        /// </summary>
        public int Spread
        {
            get
            {
                if (spreadOverride >= 0) return spreadOverride;

                int page = PageNumber;
                if (page <= 0) return -1;
                return side == PageSide.Front ? page - 1 : page;
            }
        }

        /// <summary>"Page3" → 3, "Page3_end" / "Page4y" / อื่นๆ → 0</summary>
        public static int ParsePageNumber(string name)
        {
            const string prefix = "Page";
            if (name == null || !name.StartsWith(prefix) || name.Length == prefix.Length) return 0;

            int n = 0;
            for (int i = prefix.Length; i < name.Length; i++)
            {
                char c = name[i];
                if (c < '0' || c > '9') return 0;
                n = n * 10 + (c - '0');
            }
            return n;
        }

        // ─────────────────────────── แสดงผล ───────────────────────────

        private void Awake()
        {
            CacheRenderer();
            ApplyLayout();

            // หมึกเป็นของที่ controller เปิดให้เท่านั้น — ตอนเริ่มเกมทุกหน้าต้องว่าง
            if (Application.isPlaying) Hide();
        }

        /// <summary>
        /// แสดงหมึกจาก texture นี้ — amount 0-1 ใช้ fade ตอนวิดีโอเริ่ม
        /// lightOnDark = วิดีโอเป็นตัวหนังสือขาวบนพื้นดำ
        /// </summary>
        public void Show(Texture ink, float amount, bool lightOnDark)
        {
            CacheRenderer();

            _renderer.GetPropertyBlock(_block);
            _block.SetTexture(InkTexId, ink != null ? ink : Texture2D.whiteTexture);
            _block.SetFloat(InkAmountId, Mathf.Clamp01(amount));
            _block.SetFloat(InkInvertId, lightOnDark ? 1f : 0f);
            if (ink != null)
                _block.SetVector(InkTexelId, new Vector4(1f / Mathf.Max(1, ink.width), 1f / Mathf.Max(1, ink.height), 0f, 0f));
            _block.SetVector(InkUVId, InkUV());
            _renderer.SetPropertyBlock(_block);

            _renderer.enabled = true;
        }

        /// <summary>
        /// ซ่อน quad ทั้งอัน
        ///
        /// ไม่ใช่แค่ตั้งหมึกเป็น 0 — หน้ากระดาษที่พลิกไปทับกันห่างกันไม่ถึงมิลลิเมตร quad ของหน้า
        /// ที่ไม่ได้เปิดอยู่อาจทะลุออกมาให้เห็นบนหน้าที่ทับอยู่ได้ และไม่ต้องวาดของที่มองไม่เห็นด้วย
        /// </summary>
        public void Hide()
        {
            CacheRenderer();
            _renderer.enabled = false;
        }

        private Vector4 InkUV()
        {
            float sx = flipHorizontal ? -1f : 1f;
            float sy = flipVertical   ? -1f : 1f;
            return new Vector4(sx, sy, flipHorizontal ? 1f : 0f, flipVertical ? 1f : 0f);
        }

        private void CacheRenderer()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows    = false;
            }
            if (_block == null) _block = new MaterialPropertyBlock();
        }

        // ─────────────────────────── ขนาด / สัดส่วน ───────────────────────────

        /// <summary>สัดส่วนกว้าง/สูงของวิดีโอ — อ่านจาก clip จริง ไม่มี clip ใช้ 1748 × 2480</summary>
        public float VideoAspect
        {
            get
            {
                VideoClip clip = clipThai != null ? clipThai : clipEnglish;
                if (clip != null && clip.width > 0 && clip.height > 0)
                    return (float)clip.width / clip.height;
                return DefaultAspect;
            }
        }

        /// <summary>
        /// ขนาด quad จริง (เมตร) — ใหญ่ที่สุดที่ยังอยู่ในกรอบ Page Size และสัดส่วนเท่าวิดีโอ
        ///
        /// กรอบกว้างกว่าวิดีโอ → สูงเต็มกรอบ เหลือที่ว่างซ้ายขวา
        /// กรอบสูงกว่าวิดีโอ   → กว้างเต็มกรอบ เหลือที่ว่างบนล่าง
        /// ที่ว่างนั้นเป็นแค่กระดาษเปล่า ไม่มีขอบดำแบบวิดีโอในจอ เพราะไม่มี quad อยู่ตรงนั้นเลย
        /// </summary>
        public Vector2 FittedSize
        {
            get
            {
                float w = Mathf.Max(0.001f, pageSize.x);
                float h = Mathf.Max(0.001f, pageSize.y);
                float aspect = VideoAspect;

                return w / h > aspect ? new Vector2(h * aspect, h)
                                      : new Vector2(w, w / aspect);
            }
        }

        /// <summary>
        /// ตั้ง scale ของ quad ให้ได้ขนาดจริงเป็นเมตร
        ///
        /// หารด้วย scale ของพ่อ เพราะกระดูกจาก Blender มักมี scale ติดมา (0.01 หรือ 100) — ถ้าเขียน
        /// localScale ตรงๆ quad จะเล็ก/ใหญ่ผิดไปเป็นร้อยเท่า
        /// </summary>
        public void ApplyLayout()
        {
            Vector2 size = FittedSize;

            float parentScale = 1f;
            if (transform.parent != null)
            {
                parentScale = Mathf.Abs(transform.parent.lossyScale.x);
                if (parentScale < 1e-6f) parentScale = 1f;
            }

            var scale = new Vector3(size.x / parentScale, size.y / parentScale, 1f / parentScale);
            if (transform.localScale != scale) transform.localScale = scale;
        }

#if UNITY_EDITOR
        private static Texture2D _testPattern;

        /// <summary>
        /// เช็กตำแหน่ง quad โดยไม่ต้องกด Play — ขึ้นกรอบดำกับตัว F ใหญ่ที่มุมบนซ้าย
        ///
        /// ถ้ามองจากมุมคนอ่านแล้วไม่เห็นอะไร = quad หันผิดด้าน (หมุน Y 180°) หรือจมอยู่ใต้กระดาษ
        /// ถ้าตัว F กลับด้าน/กลับหัว = ติ๊ก Flip Horizontal / Flip Vertical
        /// </summary>
        [ContextMenu("Preview Test Pattern")]
        private void PreviewTestPattern()
        {
            Show(TestPattern(), 1f, lightOnDark: false);
            UnityEditor.SceneView.RepaintAll();
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreview()
        {
            CacheRenderer();
            _renderer.SetPropertyBlock(null);
            UnityEditor.SceneView.RepaintAll();
        }

        private static Texture2D TestPattern()
        {
            if (_testPattern != null) return _testPattern;

            const int w = 140, h = 200;
            var pixels = new Color32[w * h];
            var paper  = new Color32(255, 255, 255, 255);
            var ink    = new Color32(20, 10, 10, 255);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool border = x < 4 || y < 4 || x >= w - 4 || y >= h - 4;

                // ตัว F ที่มุมบนซ้าย (y ของ texture นับจากล่างขึ้นบน)
                int fx = x - 16, fy = (h - 1 - y) - 16;
                bool letterF = fx >= 0 && fy >= 0 && fy < 60 &&
                               (fx < 10 || (fy < 10 && fx < 40) || (fy >= 25 && fy < 35 && fx < 30));

                pixels[y * w + x] = border || letterF ? ink : paper;
            }

            _testPattern = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name      = "Book Test Pattern",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode  = TextureWrapMode.Clamp,
            };
            _testPattern.SetPixels32(pixels);
            _testPattern.Apply();
            return _testPattern;
        }

        private void OnValidate()
        {
            pageSize.x = Mathf.Max(0.001f, pageSize.x);
            pageSize.y = Mathf.Max(0.001f, pageSize.y);

            // OnValidate ห้ามแก้ transform ตรงๆ (Unity เตือน SendMessage) — เลื่อนไปทำหลังจากนี้
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) ApplyLayout();
            };
        }

        /// <summary>
        /// กรอบเหลือง = Page Size, กรอบฟ้า = ที่หมึกลงจริง, ลูกศร = ด้านที่มองเห็นหมึก
        ///
        /// quad ของ Unity มองเห็นจากด้าน -Z ของตัวเอง ถ้าลูกศรชี้เข้าไปในกระดาษ หมุน quad 180°
        /// ไม่งั้นหมึกจะอยู่ใต้กระดาษ มองไม่เห็นเลย
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector2 fitted = FittedSize;
            Matrix4x4 m = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            Gizmos.matrix = m;
            Gizmos.color  = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(pageSize.x, pageSize.y, 0f));

            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.95f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(fitted.x, fitted.y, 0f));

            float arrow = Mathf.Min(fitted.x, fitted.y) * 0.5f;
            Gizmos.DrawLine(Vector3.zero, Vector3.back * arrow);
            Gizmos.DrawWireSphere(Vector3.back * arrow, arrow * 0.08f);

            // มุมบนซ้ายของภาพ — ไว้เช็กว่าตัวหนังสือไม่กลับหัว/กลับด้าน
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.95f);
            Vector3 topLeft = new Vector3(flipHorizontal ? fitted.x : -fitted.x, flipVertical ? -fitted.y : fitted.y, 0f) * 0.5f;
            Gizmos.DrawWireSphere(topLeft, arrow * 0.1f);

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
