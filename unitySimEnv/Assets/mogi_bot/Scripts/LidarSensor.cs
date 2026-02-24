using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;

/// <summary>
/// Simula un sensore LiDAR 2D usando Raycast di Unity.
/// Pubblica i dati sul topic /scan (LaserScan) per slam_toolbox.
/// Va attaccato al GameObject scan_link del robot.
/// </summary>
public class LidarSensor : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string topicName = "/scan";
    public string frameId = "scan_link";
    public float publishRate = 10f; // Hz

    [Header("Parametri LiDAR")]
    [Tooltip("Angolo minimo in gradi (di solito -180)")]
    public float angleMin = -180f;
    [Tooltip("Angolo massimo in gradi (di solito 180)")]
    public float angleMax = 180f;
    [Tooltip("Numero di raggi totali")]
    public int numRays = 720;
    [Tooltip("Distanza minima di rilevamento in metri")]
    public float rangeMin = 0.1f;
    [Tooltip("Distanza massima di rilevamento in metri")]
    public float rangeMax = 20f;
    [Tooltip("Layer da ignorare nei raycast (es. il robot stesso)")]
    public LayerMask layerMask;

    private ROSConnection ros;
    private float publishInterval;
    private float lastPublishTime;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<LaserScanMsg>(topicName);

        publishInterval = 1f / publishRate;
        lastPublishTime = 0f;

        Debug.Log($"[LidarSensor] Pubblicando su topic: {topicName}");
    }

    void Update()
    {
        if (Time.time - lastPublishTime >= publishInterval)
        {
            PublishLaserScan();
            lastPublishTime = Time.time;
        }
    }

    void PublishLaserScan()
    {
        float angleMinRad = angleMin * Mathf.Deg2Rad;
        float angleMaxRad = angleMax * Mathf.Deg2Rad;
        float angleIncrement = (angleMaxRad - angleMinRad) / numRays;

        float[] ranges = new float[numRays];

        for (int i = 0; i < numRays; i++)
        {
            float angle = angleMin + i * (angleMax - angleMin) / numRays;
            //float angle = angleMax - i * (angleMax - angleMin) / numRays;
            

            // 1 Direzione del raggio in Unity (locale)
            Vector3 unityDir = Quaternion.Euler(0, -angle, 0) * Vector3.forward;

            // 2 Raycast in Unity (resta nello spazio Unity)
            RaycastHit hit;
            float distance = float.PositiveInfinity;
            if (Physics.Raycast(transform.position, unityDir, out hit, rangeMax, layerMask))
            {
                distance = Mathf.Clamp(hit.distance, rangeMin, rangeMax);
            }

            // 3 Converti la direzione in ROS frame (Unity → ROS mapping)
            // Unity Z → ROS X, Unity X → ROS -Y, Unity Y → ROS Z
            Vector3 rosDir = new Vector3(
                unityDir.z,   // ROS X
            -unityDir.x,   // ROS Y
                unityDir.y    // ROS Z
            );

            // 4 Assegna la distanza al LaserScan
            ranges[i] = distance; 
        }

        // Tempo Unix coerente con ROS2
        double unixTime = (System.DateTime.UtcNow - 
                        new System.DateTime(1970, 1, 1)).TotalSeconds;

        uint sec = (uint)System.Math.Floor(unixTime);
        uint nanosec = (uint)((unixTime - sec) * 1e9);

        LaserScanMsg scanMsg = new LaserScanMsg
        {
            header = new HeaderMsg
            {
                frame_id = frameId,
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)sec,
                    nanosec = nanosec
                }
            },
            angle_min    = angleMinRad,
            angle_max    = angleMaxRad,
            angle_increment = angleIncrement,
            time_increment  = 0f,
            scan_time    = publishInterval,
            range_min    = rangeMin,
            range_max    = rangeMax,
            ranges       = ranges,
            intensities  = new float[numRays]
        };

            ros.Publish(topicName, scanMsg);
        }

    // Visualizza i raggi nell'editor Unity (solo debug visivo)
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        for (int i = 0; i < numRays; i++)
        {
            float angle = angleMin + i * (angleMax - angleMin) / numRays;
            /*
            Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;
            Gizmos.DrawRay(transform.position, direction * rangeMax);
            */
            Vector3 unityDir = Quaternion.Euler(0, -angle, 0) * Vector3.forward;
            Gizmos.DrawRay(transform.position, unityDir * rangeMax);
        }
    }
}