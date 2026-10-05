using System.Collections.Generic;
using UnityEngine;

namespace ScriptableObject
{
    /// <summary>
    /// ตารางแปลง "ชื่อปุ่ม" → "ภาพปุ่ม" ที่ใช้ร่วมกันทั้งเกม
    ///
    /// ทุกที่ที่โชว์ปุ่ม (การ์ดมือของคุณในแอปกฎ / prompt ลอยข้างวัตถุ / แถบมุมซ้ายล่าง)
    /// เขียนเป็น string เหมือนเดิม เช่น "E" "Space" "คลิกซ้าย ค้าง" แล้วค่อยมาเปิดตารางนี้
    /// หาภาพเอาตอนวาด — ไม่ได้ให้ทุกที่ไปถือ Sprite เอง เพราะ
    ///   • เปลี่ยนสไตล์ปุ่มทั้งเกมต้องแก้ที่เดียว
    ///   • ข้อมูลกฎ (RuleEntryData) จะได้ไม่ต้องผูกกับ asset ภาพ ย้าย/แปลได้อิสระ
    ///   • ปุ่มไหนยังไม่มีภาพ ก็ตกกลับไปเป็นตัวหนังสือเองโดยไม่พัง
    ///
    /// ตัวที่ไม่มีวันมีภาพ (เช่น "คลิกซ้าย ค้าง") ไม่ต้องใส่ ปล่อยให้เป็นตัวหนังสือได้เลย
    /// </summary>
    [CreateAssetMenu(menuName = "Phone/Key Icon Library", fileName = "KeyIcons")]
    public class KeyIconLibrary : UnityEngine.ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("ชื่อปุ่มหลัก — ตรงกับที่พิมพ์ไว้ในช่อง key ของ RuleKeyHint / KeyPromptTrigger")]
            public string key;

            [Tooltip("ปุ่มที่ประกอบจากหลายชิ้น (กรอบ + ตัวอักษร + เงา) หรือมี animation — " +
                     "ใส่ตรงนี้แล้วมันจะถูก Instantiate ลงช่องไอคอน\n" +
                     "ใส่พร้อม Icon ได้ แต่ Prefab จะถูกใช้ก่อน")]
            public GameObject prefab;

            [Tooltip("ภาพของปุ่มนี้ — ใช้ตอนปุ่มเป็นภาพแบนๆ ภาพเดียวจบ")]
            public Sprite icon;

            [Tooltip("ชื่อเรียกอื่นที่ให้ใช้ของเดียวกัน เช่น Enter กับ Return, Esc กับ Escape")]
            public string[] aliases;

            public bool HasVisual => prefab != null || icon != null;
        }

        [Tooltip("ใส่ให้ครบเฉพาะปุ่มที่มีภาพหรือ prefab — ปุ่มที่ไม่อยู่ในนี้จะถูกวาดเป็นตัวหนังสือแทน")]
        [SerializeField] private Entry[] entries;

        [Tooltip("✓ = ไม่สนตัวพิมพ์ใหญ่เล็ก (space กับ Space ใช้ภาพเดียวกัน)")]
        [SerializeField] private bool ignoreCase = true;

        private Dictionary<string, Entry> _lookup;

        private static KeyIconLibrary _default;

        /// <summary>
        /// ตารางกลางที่ใช้เมื่อช่องใน Inspector ถูกเว้นว่าง — วางไฟล์ไว้ที่
        /// Assets/Resources/KeyIcons.asset แล้วไม่ต้องไปลากใส่ทีละ component
        /// </summary>
        public static KeyIconLibrary Default
        {
            get
            {
                if (_default == null) _default = Resources.Load<KeyIconLibrary>("KeyIcons");
                return _default;
            }
        }

        /// <summary>
        /// หาว่าปุ่มนี้มีหน้าตาเก็บไว้ไหม — คืน Entry ทั้งก้อนเพราะฝั่งเรียกต้องรู้ว่าได้ prefab
        /// หรือได้ Sprite ซึ่งวาดคนละวิธีกัน ไม่เจอคืน null แล้วให้วาดเป็นตัวหนังสือแทน
        /// </summary>
        public Entry Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            Build();
            return _lookup.TryGetValue(Normalize(key), out var entry) ? entry : null;
        }

        public bool TryGet(string key, out Sprite icon)
        {
            icon = Resolve(key)?.icon;
            return icon != null;
        }

        public bool TryGetPrefab(string key, out GameObject prefab)
        {
            prefab = Resolve(key)?.prefab;
            return prefab != null;
        }

        public Sprite Get(string key) => Resolve(key)?.icon;

        /// <summary>ล้าง cache — เรียกหลังแก้ตารางตอน Play อยู่</summary>
        public void Rebuild() => _lookup = null;

        private void OnValidate() => _lookup = null;

        private void Build()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, Entry>();
            if (entries == null) return;

            foreach (var entry in entries)
            {
                if (entry == null || !entry.HasVisual) continue;

                Add(entry.key, entry);
                if (entry.aliases == null) continue;
                foreach (var alias in entry.aliases) Add(alias, entry);
            }
        }

        private void Add(string key, Entry entry)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _lookup[Normalize(key)] = entry;
        }

        private string Normalize(string key)
        {
            key = key.Trim();
            return ignoreCase ? key.ToLowerInvariant() : key;
        }
    }
}
