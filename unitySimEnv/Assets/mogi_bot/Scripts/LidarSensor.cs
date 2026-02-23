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
    public int numRays = 360;
    [Tooltip("Distanza minima di rilevamento in metri")]
    public float rangeMin = 0.1f;
    [Tooltip("Distanza massima di rilevamento in metri")]
    public float rangeMax = 10f;
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

            // Direzione del raggio nel piano orizzontale (Unity usa Y come asse verticale)
            Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;

            RaycastHit hit;
            if (Physics.Raycast(transform.position, direction, out hit, rangeMax, layerMask))
            {
                ranges[i] = hit.distance >= rangeMin ? hit.distance : float.PositiveInfinity;
            }
            else
            {
                ranges[i] = float.PositiveInfinity;
            }
        }

        // Costruisci il messaggio LaserScan
        LaserScanMsg scanMsg = new LaserScanMsg
        {
            header = new HeaderMsg
            {
                frame_id = frameId,
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec = (int)Time.time,
                    nanosec = (uint)((Time.time - Mathf.Floor(Time.time)) * 1e9)
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
            Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;
            Gizmos.DrawRay(transform.position, direction * rangeMax);
        }
    }
}