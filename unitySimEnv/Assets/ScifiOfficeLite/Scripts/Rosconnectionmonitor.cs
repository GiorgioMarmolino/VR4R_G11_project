using UnityEngine;
using Unity.Robotics.ROSTCPConnector;

/// <summary>
/// Monitor della connessione ROS2.
/// Espone una proprietà statica IsConnected affidabile.
/// Si basa su un topic pubblicato SOLO da ROS2 (non da Unity).
/// Attaccalo a qualsiasi GameObject nella scena.
/// </summary>
public class ROSConnectionMonitor : MonoBehaviour
{
    // Accessibile da qualsiasi script con ROSConnectionMonitor.IsConnected
    public static bool IsConnected { get; private set; } = false;

    [Header("Impostazioni")]
    [Tooltip("Secondi senza messaggi da ROS prima di segnare disconnesso")]
    public float timeoutSecs = 3f;

    private float lastMsgTime = -999f;

    void Start()
    {
        // Sottoscrivi SOLO a topic pubblicati da ROS2, non da Unity
        // /amcl_pose viene pubblicato da Nav2, mai da Unity
        ROSConnection.GetOrCreateInstance()
            .Subscribe<RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg>(
                "/amcl_pose", OnAmclReceived);

        // /clock viene pubblicato da ROS2 quando use_sim_time è attivo
        // Aggiungi altri topic ROS-only se amcl non è attivo
        InvokeRepeating("CheckTimeout", 1f, 1f);

        Debug.Log("[ROSConnectionMonitor] In ascolto su /amcl_pose per rilevare connessione ROS.");
    }

    void OnAmclReceived(RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg msg)
    {
        lastMsgTime  = Time.time;
        if (!IsConnected)
        {
            IsConnected = true;
            Debug.Log("[ROSConnectionMonitor] ROS2 connesso!");
        }
    }

    void CheckTimeout()
    {
        bool wasConnected = IsConnected;
        IsConnected = (Time.time - lastMsgTime) < timeoutSecs;

        if (wasConnected && !IsConnected)
            Debug.Log("[ROSConnectionMonitor] ROS2 disconnesso.");
    }
}