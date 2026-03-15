using UnityEngine;

namespace System.HeartbeatSystem
{
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
                heartValue = 0f;
            }
            else if (Inside(pos, orangeMinX, orangeMaxX, orangeMinZ, orangeMaxZ))
            {
                heartValue = 0.5f;
            }
            else
            {
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

            Gizmos.DrawLine(a,b);
            Gizmos.DrawLine(b,c);
            Gizmos.DrawLine(c,d);
            Gizmos.DrawLine(d,a);
        }
    }
}
