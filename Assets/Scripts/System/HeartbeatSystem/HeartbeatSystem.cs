using UnityEngine;

namespace System.HeartbeatSystem
{
    /// <summary>
    /// ระดับโซนที่ผู้เล่นอยู่ปัจจุบัน
    /// Green   = โซนปลอดภัย
    /// Orange  = โซนเตือน
    /// Red     = โซนอันตราย (ยังมีเวลา)
    /// Outside = นอกทุกโซน → countdown ตาย
    /// </summary>
    public enum ZoneLevel { Green = 0, Orange = 1, Red = 2, Outside = 3 }

    public class HeartbeatSystem : MonoBehaviour
    {
        public Transform player;

        // Green zone (ในสุด)
        public float greenMinX;
        public float greenMaxX;
        public float greenMinZ;
        public float greenMaxZ;

        // Orange zone
        public float orangeMinX;
        public float orangeMaxX;
        public float orangeMinZ;
        public float orangeMaxZ;

        // Red zone
        public float redMinX;
        public float redMaxX;
        public float redMinZ;
        public float redMaxZ;

        float heartValue;
        public static HeartbeatSystem instance;

        /// <summary>โซนที่ผู้เล่นอยู่ปัจจุบัน — อัปเดตทุกครั้งที่เรียก CheckPlayerInsideZone()</summary>
        public ZoneLevel CurrentZoneLevel { get; private set; } = ZoneLevel.Green;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void CheckPlayerInsideZone()
        {
            Vector3 pos = player.position;

            if (Inside(pos, greenMinX, greenMaxX, greenMinZ, greenMaxZ))
            {
                CurrentZoneLevel = ZoneLevel.Green;
                heartValue = 0f;
            }
            else if (Inside(pos, orangeMinX, orangeMaxX, orangeMinZ, orangeMaxZ))
            {
                CurrentZoneLevel = ZoneLevel.Orange;
                heartValue = 0.5f;
            }
            else if (Inside(pos, redMinX, redMaxX, redMinZ, redMaxZ))
            {
                CurrentZoneLevel = ZoneLevel.Red;
                heartValue = 1f;
            }
            else
            {
                CurrentZoneLevel = ZoneLevel.Outside;
                heartValue = 1f;
            }

            AudioManager.instance.SetHeartbeat(heartValue);
        }

        bool Inside(Vector3 p, float minX, float maxX, float minZ, float maxZ)
        {
            return p.x > minX && p.x < maxX && p.z > minZ && p.z < maxZ;
        }

        void OnDrawGizmos()
        {
            // Green zone
            Gizmos.color = Color.green;
            DrawRect(greenMinX, greenMaxX, greenMinZ, greenMaxZ);

            // Orange zone
            Gizmos.color = new Color(1f, 0.5f, 0f);
            DrawRect(orangeMinX, orangeMaxX, orangeMinZ, orangeMaxZ);

            // Red zone
            Gizmos.color = Color.red;
            DrawRect(redMinX, redMaxX, redMinZ, redMaxZ);
        }

        void DrawRect(float minX, float maxX, float minZ, float maxZ)
        {
            Vector3 a = new Vector3(minX, 0, minZ);
            Vector3 b = new Vector3(maxX, 0, minZ);
            Vector3 c = new Vector3(maxX, 0, maxZ);
            Vector3 d = new Vector3(minX, 0, maxZ);

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
