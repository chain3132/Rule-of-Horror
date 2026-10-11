using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Manager
{
    /// <summary>
    /// แอปมินิเกม "งูกินหาง" (ปุ่ม 4) — เกมงูแบบโนเกียบนจอโทรศัพท์ในเกม
    ///
    /// วาดลง Texture2D ทีเดียวทั้งจอแล้วแปะบน RawImage ตัวเดียว ไม่ได้สร้าง Image เป็นตารางช่อง
    /// เพราะงูยาวได้ถึงหลายสิบช่อง ถ้าทำเป็น GameObject ต่อช่องจะได้ draw call เป็นร้อยต่อเฟรม
    /// บน UI ที่ถ่ายลง RenderTexture อยู่แล้ว — แบบนี้คือ 1 draw call คงที่ ไม่ว่างูจะยาวแค่ไหน
    /// และได้ขอบคมแบบจอ LCD ฟรีจาก filterMode = Point
    ///
    /// อ่านคีย์บอร์ดตรงๆ ไม่ผ่าน InputHandler เพราะจอโทรศัพท์ถ่ายลง RenderTexture กดเมาส์ไม่ได้
    /// ทุกอย่างในโทรศัพท์เป็นคีย์บอร์ดล้วนอยู่แล้ว และตอนเปิดแอปนี้ PhoneSystem ล็อกการเดินไว้แล้ว
    /// (LockPlayer) WASD จึงว่างให้เอามาบังคับงูได้ ไม่ไปชนกับการเดินของผู้เล่น
    ///
    /// ตัวนี้เดินเฉพาะตอนแผงเปิดอยู่ (Update หยุดเองเมื่อ SetActive(false)) เก็บโทรศัพท์แล้วเกมค้างไว้
    /// กลับมาเปิดอีกจะเจอหน้า "พักอยู่" ไม่ใช่งูวิ่งต่อทันที — ไม่งั้นเปิดมาชนกำแพงตายฟรี
    /// </summary>
    public class SnakeGameController : MonoBehaviour
    {
        private enum Phase { Ready, Playing, Paused, Dead }

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("ต่อกับ UI")]
        [Tooltip("RawImage ที่เอาไว้แปะจอเกม — สคริปต์สร้าง texture ให้เอง ไม่ต้องใส่รูป")]
        [SerializeField] private RawImage screen;
        [Tooltip("แถบแต้มด้านบน เว้นว่างได้")]
        [SerializeField] private TMP_Text scoreText;
        [Tooltip("ข้อความกลางจอ (เริ่มเกม / พักอยู่ / ตายแล้ว) เว้นว่างได้")]
        [SerializeField] private TMP_Text bannerText;

        [Header("สนาม")]
        [Tooltip("ขนาดสนามเป็นช่อง — แนวตั้งให้มากกว่าแนวนอนเพราะจอโทรศัพท์เป็นแนวตั้ง")]
        [SerializeField] private Vector2Int grid = new Vector2Int(14, 18);
        [Tooltip("หนึ่งช่องกว้างกี่พิกเซลใน texture — ยิ่งมากยิ่งคมเวลาขยาย แต่กินแรมเพิ่ม")]
        [Range(2, 24)]
        [SerializeField] private int cellPixels = 10;
        [Tooltip("ช่องไฟระหว่างช่อง (พิกเซล) — ทำให้เห็นงูเป็นข้อๆ ไม่ใช่แท่งตัน")]
        [Range(0, 6)]
        [SerializeField] private int cellGap = 2;
        [Tooltip("ความหนาของกรอบสนาม (พิกเซล) — ชนกรอบคือตาย กรอบจึงต้องเห็นชัด")]
        [Range(0, 8)]
        [SerializeField] private int framePixels = 3;

        [Header("ความเร็ว")]
        [Tooltip("วินาทีต่อหนึ่งก้าวตอนเริ่มเกม")]
        [SerializeField] private float startInterval = 0.22f;
        [Tooltip("กินอาหารหนึ่งชิ้นแล้วเร็วขึ้นกี่วินาที")]
        [SerializeField] private float speedUpPerFood = 0.006f;
        [Tooltip("เร็วที่สุดได้เท่าไร — ต่ำกว่านี้คนเล่นตามไม่ทัน")]
        [SerializeField] private float minInterval = 0.07f;
        [Tooltip("ความยาวงูตอนเริ่ม")]
        [Range(2, 8)]
        [SerializeField] private int startLength = 3;

        [Header("สี")]
        [SerializeField] private Color32 backgroundColor = new Color32(14, 22, 14, 255);
        [SerializeField] private Color32 frameColor      = new Color32(46, 70, 46, 255);
        [SerializeField] private Color32 bodyColor       = new Color32(118, 190, 86, 255);
        [SerializeField] private Color32 headColor       = new Color32(196, 242, 156, 255);
        [SerializeField] private Color32 foodColor       = new Color32(226, 88, 72, 255);

        [Header("ข้อความ")]
        [Tooltip("{0} = แต้ม, {1} = แต้มสูงสุด")]
        [SerializeField] private string scoreFormat = "แต้ม {0}   สูงสุด {1}";
        [SerializeField] private string readyBanner  = "งูกินหาง\n\n<size=70%>Enter เริ่มเล่น\nWASD บังคับ</size>";
        [SerializeField] private string pausedBanner = "พักอยู่\n\n<size=70%>Enter เล่นต่อ</size>";
        [Tooltip("{0} = แต้มที่ได้รอบนี้")]
        [SerializeField] private string deadBanner   = "ตายแล้ว\n\n<size=70%>ได้ {0} แต้ม\nEnter เล่นอีกครั้ง</size>";

        /// <summary>คีย์เก็บแต้มสูงสุด — ติดข้ามรอบเล่น ไม่ผูกกับเซฟของเกมหลัก</summary>
        private const string BestScoreKey = "ROH_Snake_Best";

        // ─────────────────────────── Runtime ───────────────────────────

        /// <summary>ตัวงู index 0 = หัว — ใช้ List เพราะต้องแทรกหัว/ตัดหางทุกก้าว</summary>
        private readonly List<Vector2Int> _body = new List<Vector2Int>();

        /// <summary>ช่องว่างที่อาหารเกิดได้ — เก็บไว้ใช้ซ้ำ ไม่ให้ alloc ทุกครั้งที่กิน</summary>
        private readonly List<Vector2Int> _freeCells = new List<Vector2Int>();

        /// <summary>คิวเลี้ยว — กดสองทิศในหนึ่งก้าวแล้วเลี้ยวได้ครบทั้งสอง ไม่ใช่กินทิ้งอันแรก</summary>
        private readonly Queue<Vector2Int> _turns = new Queue<Vector2Int>();

        private Phase      _phase = Phase.Ready;
        private Vector2Int _dir   = Vector2Int.up;
        private Vector2Int _food;
        private float      _tickTimer;
        private int        _score;
        private int        _best;

        private Texture2D _tex;
        private Color32[] _pixels;
        private int       _texW, _texH;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Awake()
        {
            _best = PlayerPrefs.GetInt(BestScoreKey, 0);
            BuildTexture();
            ResetToReady();
        }

        /// <summary>
        /// เปิดแอป — ถ้าเกมค้างอยู่กลางทางให้พักไว้ก่อน ไม่ให้วิ่งต่อทันที
        /// เก็บโทรศัพท์ไปทำอย่างอื่นแล้วกลับมา งูไม่ควรเดินไปชนกำแพงตอนที่เรายังไม่พร้อม
        /// </summary>
        private void OnEnable()
        {
            if (_phase == Phase.Playing) _phase = Phase.Paused;

            _tickTimer = 0f;
            RefreshLabels();
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        private void Update()
        {
            ReadInput();

            if (_phase == Phase.Playing)
            {
                _tickTimer -= Time.unscaledDeltaTime;   // ไม่สนใจ timeScale — หน้า UI ควรเล่นได้เสมอ
                if (_tickTimer <= 0f)
                {
                    _tickTimer += CurrentInterval();
                    Step();
                }
            }

            Draw();
        }

        // ─────────────────────────── Input ───────────────────────────

        private void ReadInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Confirm();

            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)    Turn(Vector2Int.up);
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)  Turn(Vector2Int.down);
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)  Turn(Vector2Int.left);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Turn(Vector2Int.right);
        }

        /// <summary>Enter — เริ่ม / เล่นต่อ / เล่นใหม่ ตามสถานะที่ค้างอยู่</summary>
        private void Confirm()
        {
            switch (_phase)
            {
                case Phase.Ready:
                case Phase.Dead:
                    StartRound();
                    break;

                case Phase.Paused:
                    _phase     = Phase.Playing;
                    _tickTimer = CurrentInterval();
                    RefreshLabels();
                    break;
            }
        }

        /// <summary>
        /// ขอเลี้ยว — เช็กย้อนทิศกับทิศ "ที่จะใช้ก้าวถัดไป" ไม่ใช่ทิศที่กำลังวิ่งอยู่
        ///
        /// ถ้าเทียบกับทิศปัจจุบันเฉยๆ จะมีบั๊กคลาสสิก: วิ่งขึ้นอยู่ กดซ้ายแล้วกดลงรวดเดียวในหนึ่งก้าว
        /// ทั้งสองปุ่มผ่านด่านเพราะต่างก็ไม่ย้อน "ขึ้น" แต่พอก้าวจริงจะได้ทิศลงทันทีทับคอตัวเอง
        /// </summary>
        private void Turn(Vector2Int wanted)
        {
            if (_phase == Phase.Paused) Confirm();    // ขยับคือตั้งใจเล่นต่อ ไม่ต้องกด Enter ก่อน
            if (_phase != Phase.Playing) return;
            if (_turns.Count >= 2) return;            // เก็บล่วงหน้าแค่สองเลี้ยว เกินนั้นคือรัวมั่ว

            Vector2Int last = LastQueued();
            if (wanted == last || wanted == -last) return;

            _turns.Enqueue(wanted);
        }

        /// <summary>ทิศสุดท้ายที่ตกลงไว้แล้ว — ท้ายคิวถ้ามี ไม่มีก็ทิศที่กำลังวิ่ง</summary>
        private Vector2Int LastQueued()
        {
            Vector2Int last = _dir;
            foreach (var turn in _turns) last = turn;
            return last;
        }

        // ─────────────────────────── Game ───────────────────────────

        private float CurrentInterval()
            => Mathf.Max(minInterval, startInterval - _score * speedUpPerFood);

        private void ResetToReady()
        {
            _phase = Phase.Ready;
            _score = 0;
            BuildSnake();
            SpawnFood();
            RefreshLabels();
        }

        private void StartRound()
        {
            _score     = 0;
            _phase     = Phase.Playing;
            _tickTimer = startInterval;
            BuildSnake();
            SpawnFood();
            RefreshLabels();
        }

        /// <summary>วางงูตั้งต้นกลางสนามส่วนล่าง หันขึ้น — ให้มีที่วิ่งข้างหน้าเต็มจอ</summary>
        private void BuildSnake()
        {
            _body.Clear();
            _turns.Clear();
            _dir = Vector2Int.up;

            int length = Mathf.Clamp(startLength, 2, grid.y - 1);
            int headX  = grid.x / 2;
            int headY  = grid.y - length;   // แถวบนสุดของตัวงู (y มากคือล่าง)

            for (int i = 0; i < length; i++)
                _body.Add(new Vector2Int(headX, headY + i));   // index 0 = หัว อยู่บนสุด หางอยู่ล่าง
        }

        private void Step()
        {
            if (_turns.Count > 0) _dir = _turns.Dequeue();

            // y ลดคือขึ้น เพราะ grid นับแถวจากบนลงล่าง ให้ตรงกับที่คนอ่านจอ
            Vector2Int head = _body[0] + new Vector2Int(_dir.x, -_dir.y);

            if (head.x < 0 || head.y < 0 || head.x >= grid.x || head.y >= grid.y) { Die(); return; }

            // ชนตัวเอง — ไม่นับช่องหางสุดท้าย เพราะมันจะขยับออกไปในก้าวเดียวกันนี้
            int selfLimit = _body.Count - 1;
            for (int i = 0; i < selfLimit; i++)
                if (_body[i] == head) { Die(); return; }

            bool ate = head == _food;

            _body.Insert(0, head);
            if (!ate) _body.RemoveAt(_body.Count - 1);
            else
            {
                _score++;
                RefreshLabels();

                // เต็มสนาม = จบเกม ไม่มีช่องให้อาหารเกิดแล้ว
                if (_body.Count >= grid.x * grid.y) { Die(); return; }
                SpawnFood();
            }
        }

        private void Die()
        {
            _phase = Phase.Dead;
            _turns.Clear();

            if (_score > _best)
            {
                _best = _score;
                PlayerPrefs.SetInt(BestScoreKey, _best);
                PlayerPrefs.Save();
            }

            RefreshLabels();
        }

        /// <summary>
        /// สุ่มช่องว่างให้อาหาร — ไล่เก็บช่องว่างทั้งหมดก่อนแล้วสุ่มจากในนั้น
        /// ไม่ใช้วิธีสุ่มแล้วลองใหม่ เพราะตอนงูยาวเกือบเต็มสนามมันจะวนเกือบไม่จบ
        /// </summary>
        private void SpawnFood()
        {
            _freeCells.Clear();

            for (int y = 0; y < grid.y; y++)
            for (int x = 0; x < grid.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!_body.Contains(cell)) _freeCells.Add(cell);
            }

            if (_freeCells.Count == 0) return;
            _food = _freeCells[Random.Range(0, _freeCells.Count)];
        }

        // ─────────────────────────── Draw ───────────────────────────

        private void BuildTexture()
        {
            grid = new Vector2Int(Mathf.Max(4, grid.x), Mathf.Max(4, grid.y));

            _texW = grid.x * cellPixels + framePixels * 2;
            _texH = grid.y * cellPixels + framePixels * 2;

            _tex = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false)
            {
                name       = "SnakeScreen",
                filterMode = FilterMode.Point,     // ขอบคมแบบจอ LCD ไม่ต้องเบลอให้เนียน
                wrapMode   = TextureWrapMode.Clamp,
            };

            _pixels = new Color32[_texW * _texH];

            if (screen != null) screen.texture = _tex;
            else Debug.LogError("[Snake] ยังไม่ได้ใส่ RawImage ในช่อง screen — เกมเดินแต่ไม่มีภาพ", this);
        }

        /// <summary>
        /// วาดใหม่ทั้งจอทุกเฟรม — ไม่ได้ทำ dirty flag ไว้เพราะไม่คุ้มกับสถานะที่ต้องคอยดูแล
        /// แผงนี้ active แค่ตอนเปิดแอปอยู่ (PhoneUIController ปิดให้ตอนสลับแอป) Update จึงไม่เดิน
        /// เลยตอนไม่ได้เล่น — เฟรมที่วาดคือเฟรมที่ผู้เล่นกำลังดูอยู่จริงทั้งนั้น
        /// </summary>
        private void Draw()
        {
            if (_tex == null || _pixels == null) return;

            // เติมสีกรอบทั้งผืนก่อน แล้วเติมพื้นหลังทับตรงกลาง
            // ได้กรอบมาฟรีโดยไม่ต้องไล่เช็กทีละพิกเซลว่าอยู่ขอบหรือเปล่า
            Fill(frameColor);
            FillRect(framePixels, framePixels,
                     _texW - framePixels * 2, _texH - framePixels * 2, backgroundColor);

            // อาหารกะพริบ — จุดนิ่งๆ บนจอเขียวๆ หาไม่เจอ
            if (Mathf.Repeat(Time.unscaledTime, 0.7f) < 0.5f) DrawCell(_food, foodColor);

            for (int i = _body.Count - 1; i >= 0; i--)
                DrawCell(_body[i], i == 0 ? headColor : bodyColor);

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);
        }

        private void Fill(Color32 color)
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = color;
        }

        private void FillRect(int x0, int y0, int width, int height, Color32 color)
        {
            for (int y = 0; y < height; y++)
            {
                int row = (y0 + y) * _texW + x0;
                for (int x = 0; x < width; x++) _pixels[row + x] = color;
            }
        }

        /// <summary>วาดหนึ่งช่อง — grid แถว 0 อยู่บน แต่ texture แถว 0 อยู่ล่าง ต้องกลับแกน y</summary>
        private void DrawCell(Vector2Int cell, Color32 color)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= grid.x || cell.y >= grid.y) return;

            int size    = Mathf.Max(1, cellPixels - cellGap);
            int inset   = (cellPixels - size) / 2;
            int originX = framePixels + cell.x * cellPixels + inset;
            int originY = framePixels + (grid.y - 1 - cell.y) * cellPixels + inset;

            for (int y = 0; y < size; y++)
            {
                int row = (originY + y) * _texW + originX;
                for (int x = 0; x < size; x++) _pixels[row + x] = color;
            }
        }

        private void RefreshLabels()
        {
            if (scoreText != null) scoreText.text = string.Format(scoreFormat, _score, _best);

            if (bannerText == null) return;

            string banner = _phase switch
            {
                Phase.Ready  => readyBanner,
                Phase.Paused => pausedBanner,
                Phase.Dead   => string.Format(deadBanner, _score),
                _            => string.Empty,
            };

            bannerText.text = banner;
            bannerText.gameObject.SetActive(!string.IsNullOrEmpty(banner));
        }

        // ─────────────────────────── Editor ───────────────────────────

        /// <summary>แก้ขนาดสนาม/ช่องใน Inspector ตอน Play อยู่ แล้วให้สร้าง texture ใหม่ให้เลย</summary>
        private void OnValidate()
        {
            if (!Application.isPlaying || _tex == null) return;

            if (_texW == grid.x * cellPixels + framePixels * 2 &&
                _texH == grid.y * cellPixels + framePixels * 2) return;

            Destroy(_tex);
            BuildTexture();
            ResetToReady();
        }
    }
}
