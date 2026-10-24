using System.Collections;
using FMODUnity;
using ScriptableObject;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Rule6
{
    /// <summary>
    /// หน้าจอแชทของคัทซีนบทจบ — เล่นบทแชทที่เขียนไว้ตายตัว ไม่ให้ผู้เล่นเลือกอะไร
    ///
    /// หน้าจอเป็นสำเนาของ ChatPanel ในโทรศัพท์ ขยายเต็มจอ (ตัวสร้างคัทซีนก๊อปมาให้) — หน้าตา
    /// เหมือนแชทในเกมทุกอย่าง แต่ไม่ต้องยกโทรศัพท์ และไม่แตะแชทจริงเลย
    ///
    /// ไม่ใช้ ChatUIController ของโทรศัพท์จริง เพราะตัวนั้นผูกกับ contact, timeline และการบันทึก
    /// กฎลงแอป — บทจบกับดักคือ "เรา" ส่งกฎให้คนแปลกหน้าคนต่อไป ถ้าไปเล่นผ่านแชทจริง ข้อความ
    /// พวกนี้จะไปปนอยู่ในประวัติแชทของคนปริศนา
    ///
    /// บทแชทเป็น ConversationData ตัวเดียวกับแชทในเกม จึงเขียนในหน้าต่าง Conversation Graph ได้
    /// และถูกดึงเข้าตารางแปลภาษาเหมือนแชทอื่นทุกอย่าง
    ///   ข้อความ Is Player = เราเป็นคนพิมพ์ (ฟองขวา)
    ///   ข้อความอื่น        = อีกฝั่งตอบ (ฟองซ้าย มี "กำลังพิมพ์" ก่อน)
    ///   node ที่มี Replies  = ใช้คำตอบแรกเสมอ (คัทซีนไม่มีให้เลือก)
    /// </summary>
    public class EndingChat : MonoBehaviour
    {
        [Header("บทแชท")]
        [SerializeField] private ConversationData conversation;

        [Header("UI")]
        [Tooltip("ที่วางฟองข้อความ — ควรมี VerticalLayoutGroup + ContentSizeFitter")]
        [SerializeField] private Transform contentRoot;

        [Tooltip("ฟองฝั่งซ้าย (อีกฝั่ง) — ใช้ prefab เดียวกับแชทในโทรศัพท์ได้")]
        [SerializeField] private GameObject leftBubblePrefab;

        [Tooltip("ฟองฝั่งขวา (เรา)")]
        [SerializeField] private GameObject rightBubblePrefab;

        [Tooltip("ฟอง \"กำลังพิมพ์\" — เว้นว่าง = ใช้ฟองซ้ายใส่จุดแทน")]
        [SerializeField] private GameObject typingBubblePrefab;

        [Tooltip("เลื่อนลงล่างสุดทุกครั้งที่มีข้อความใหม่ — เว้นว่างได้")]
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("ป้าย \"ส่งแล้ว\" ใต้ข้อความสุดท้ายของเรา — เว้นว่างได้")]
        [SerializeField] private GameObject sentMark;

        [Tooltip("ชื่อบนหัวแชท — เว้นว่างได้")]
        [SerializeField] private TMP_Text headerLabel;

        [Tooltip("ข้อความบนหัวแชท ตั้งตอนเริ่มเล่น (ผ่านตารางแปลภาษา)")]
        [SerializeField] private string headerText = "หมายเลขที่ไม่รู้จัก";

        [Header("จังหวะ (วินาที)")]
        [Tooltip("เราพิมพ์นานเท่านี้ก่อนข้อความจะส่งออก")]
        [SerializeField] private float playerTypeSeconds = 1.4f;

        [Tooltip("อีกฝั่งขึ้น \"กำลังพิมพ์\" นานเท่านี้ก่อนข้อความจะมา")]
        [SerializeField] private float otherTypeSeconds = 1.6f;

        [Tooltip("เว้นระหว่างข้อความ")]
        [SerializeField] private float gapSeconds = 0.5f;

        [Tooltip("ค้างหน้าจอไว้หลังข้อความสุดท้าย")]
        [SerializeField] private float holdAtEnd = 2f;

        [Tooltip("✓ = ข้อความแรกขึ้นมาทันทีตั้งแต่เปิดจอ (เป็นแชทเก่าที่ค้างอยู่แล้ว เช่นข้อความจากแม่)\n" +
                 "✗ = ข้อความแรกก็ต้องพิมพ์/รอเหมือนข้อความอื่น")]
        [SerializeField] private bool firstMessageAlreadyThere;

        [Header("เสียง (FMOD — เว้นว่างได้)")]
        [SerializeField] private string typingEvent;
        [SerializeField] private string sendEvent;
        [SerializeField] private string receiveEvent;

        [Header("ขอบเงารอบจอ")]
        [Tooltip("✓ = มีเงามืดๆ รอบขอบจอ ตรงกลางสว่าง — เหมือนมองจอโทรศัพท์ในห้องมืด")]
        [SerializeField] private bool vignette = true;

        [Tooltip("ความเข้มของเงาที่ขอบจอ")]
        [Range(0f, 1f)]
        [SerializeField] private float vignetteStrength = 0.7f;

        [Tooltip("จุดที่เงาเริ่ม วัดจากกลางจอ — 0 = เริ่มตั้งแต่กลางจอ, 1 = เริ่มที่ขอบจอพอดี")]
        [Range(0f, 1f)]
        [SerializeField] private float vignetteInner = 0.35f;

        [Tooltip("ระยะที่เงาไล่จากใสไปทึบสุด — ค่าน้อย = ขอบเงาคม, ค่ามาก = ไล่นุ่มๆ")]
        [Range(0.05f, 1.5f)]
        [SerializeField] private float vignetteSoftness = 0.7f;

        [SerializeField] private Color vignetteColor = Color.black;

        private const int MaxNodes = 200;   // กันบทแชทที่ next node วนกลับมาที่เดิม

        private Image _vignette;
        private Texture2D _vignetteTex;
        private Vector2 _vignetteBuilt = new Vector2(-1f, -1f);

        // ตอนจอนี้ถูกเปิด (ตัดเข้า shot แชท) เงาต้องอยู่แล้วตั้งแต่เฟรมแรก ไม่ใช่โผล่ตอนแชทเริ่มเล่น
        private void OnEnable() => UpdateVignette();

        // ปรับค่าใน Inspector ระหว่างกด Play แล้วเห็นผลทันที ไม่ต้องรอรอบหน้า
        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled) UpdateVignette();
        }

        /// <summary>
        /// เงารอบขอบจอ — รูปที่สร้างเองตอนเล่น (กลางใส ขอบทึบ) ยืดเต็มจอ วางไว้บนสุดของ Canvas
        ///
        /// ยืดตามจอจึงเป็นวงรีตามสัดส่วนจอ ซึ่งคือทรงของ vignette ที่ถูกอยู่แล้ว ไม่ต้องคำนวณเอง
        /// ไม่ใช้ post-processing เพราะ Canvas แบบ Overlay ไม่ผ่าน Volume ของกล้องเลย
        /// </summary>
        private void UpdateVignette()
        {
            if (!vignette)
            {
                if (_vignette != null) _vignette.gameObject.SetActive(false);
                return;
            }

            if (_vignette == null)
            {
                var go = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;

                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                _vignette = go.GetComponent<Image>();
                _vignette.raycastTarget = false;
            }

            var shape = new Vector2(vignetteInner, vignetteSoftness);
            if (_vignetteTex == null || _vignetteBuilt != shape)
            {
                if (_vignetteTex != null) Destroy(_vignetteTex);
                _vignetteTex = BuildVignette(vignetteInner, vignetteSoftness);
                _vignetteBuilt = shape;
                _vignette.sprite = Sprite.Create(_vignetteTex, new Rect(0, 0, _vignetteTex.width, _vignetteTex.height),
                                                 new Vector2(0.5f, 0.5f));
            }

            Color c = vignetteColor;
            c.a = vignetteStrength;
            _vignette.color = c;

            _vignette.gameObject.SetActive(true);
            _vignette.transform.SetAsLastSibling();
        }

        private static Texture2D BuildVignette(float inner, float softness)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name       = "Ending Chat Vignette",
            };

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;

                // ระยะแบบวงรีตามสัดส่วนจอ: 0 = กลางจอ, 1 = ขอบจอ (กลางด้าน), ~1.41 = มุมจอ
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, inner + softness, r));
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>ล้างจอแล้วเล่นบททั้งหมดจนจบ</summary>
        public IEnumerator Play()
        {
            Clear();
            UpdateVignette();

            if (headerLabel != null && !string.IsNullOrWhiteSpace(headerText))
                headerLabel.text = L10n.T(headerText);

            if (conversation == null || conversation.nodes == null || conversation.nodes.Count == 0)
            {
                Debug.LogWarning($"[EndingChat] '{name}' ยังไม่ได้ใส่บทแชท (Conversation) — ข้ามไป", this);
                yield break;
            }

            int  index = conversation.startNode;
            bool first = true;
            for (int guard = 0; guard < MaxNodes; guard++)
            {
                if (index < 0 || index >= conversation.nodes.Count) break;

                var node = conversation.nodes[index];
                string text = node.message;
                int next    = node.nextNode;

                if (node.NeedsReply && node.replies != null && node.replies.Count > 0)
                {
                    // ข้อความของ node ที่ให้เลือกตอบคือคำถาม ไม่ใช่สิ่งที่เราพิมพ์ — ใช้คำตอบแรกแทน
                    text = node.replies[0].replyText;
                    next = node.replies[0].nextNode;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (first && firstMessageAlreadyThere)
                        AddBubble(node.isPlayer ? rightBubblePrefab : leftBubblePrefab, text);
                    else if (node.isPlayer) yield return SendMine(text);
                    else                    yield return ReceiveTheirs(text);

                    first = false;

                    if (gapSeconds > 0f) yield return new WaitForSeconds(gapSeconds);
                }

                index = next;
            }

            if (holdAtEnd > 0f) yield return new WaitForSeconds(holdAtEnd);
        }

        private IEnumerator SendMine(string text)
        {
            if (sentMark != null) sentMark.SetActive(false);

            Sound(typingEvent);
            if (playerTypeSeconds > 0f) yield return new WaitForSeconds(playerTypeSeconds);

            AddBubble(rightBubblePrefab, text);
            Sound(sendEvent);

            if (sentMark != null)
            {
                sentMark.SetActive(true);
                sentMark.transform.SetAsLastSibling();
            }
        }

        private IEnumerator ReceiveTheirs(string text)
        {
            GameObject typing = null;
            var prefab = typingBubblePrefab != null ? typingBubblePrefab : leftBubblePrefab;

            if (otherTypeSeconds > 0f && prefab != null && contentRoot != null)
            {
                typing = Instantiate(prefab, contentRoot);
                var label = typing.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null && typingBubblePrefab == null) label.text = "...";
                ScrollToBottom();

                yield return new WaitForSeconds(otherTypeSeconds);
                Destroy(typing);
            }

            AddBubble(leftBubblePrefab, text);
            Sound(receiveEvent);
        }

        private void AddBubble(GameObject prefab, string text)
        {
            if (prefab == null || contentRoot == null)
            {
                Debug.LogWarning($"[EndingChat] '{name}' ยังไม่ได้ใส่ Content Root หรือ prefab ฟองข้อความ", this);
                return;
            }

            var bubble = Instantiate(prefab, contentRoot);
            var label  = bubble.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = L10n.T(text);

            if (sentMark != null) sentMark.transform.SetAsLastSibling();
            ScrollToBottom();
        }

        private void Clear()
        {
            if (contentRoot == null) return;

            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                var child = contentRoot.GetChild(i).gameObject;
                if (sentMark != null && child == sentMark) continue;
                Destroy(child);
            }

            if (sentMark != null) sentMark.SetActive(false);
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        }

        private void Sound(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                RuntimeManager.PlayOneShot(path);
            }
            catch (EventNotFoundException)
            {
                Debug.LogWarning($"[EndingChat] ยังไม่มี event '{path}' ใน FMOD", this);
            }
        }
    }
}
