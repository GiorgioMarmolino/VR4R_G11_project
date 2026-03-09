using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Sensor;
using RosMessageTypes.Geometry;
//using RosMessageTypes.Action;
using System.Collections.Generic;

/// <summary>
/// HUD di navigazione completo.
/// Mostra: stato navigazione, distanza goal, ETA, ostacolo più vicino,
/// frequenza topic odom/scan, stato connessione ROS.
/// </summary>
public class NavigationStatusHUD : MonoBehaviour
{
    [Header("Riferimenti UI — Navigazione")]
    public Text navStatusText;
    public Text distanceToGoalText;
    public Text etaText;
    public Text nearestObstacleText;

    [Header("Riferimenti UI — Topic Monitor")]
    public Text odomFreqText;
    public Text scanFreqText;

    [Header("Riferimenti UI — Connessione ROS")]
    public Text rosConnectionText;
    public Text rosArrowsText;        // frecce TX/RX

    [Header("Impostazioni")]
    public float robotSpeed = 0f;     // aggiornato da odom
    public float minObstacleDist = 0.5f; // soglia pericolo in metri

    // Stato navigazione
    private string navStatus = "IDLE";
    private float distanceToGoal = -1f;
    private float eta = -1f;

    // Posizione robot e goal
    private Vector2 robotPos  = Vector2.zero;
    private Vector2 goalPos   = Vector2.zero;
    private bool hasGoal      = false;

    // Ostacolo più vicino
    private float nearestObstacle = float.MaxValue;

    // Frequenze topic
    private float odomLastTime  = 0f;
    private float scanLastTime  = 0f;
    private float odomFreq      = 0f;
    private float scanFreq      = 0f;
    private int   odomCount     = 0;
    private int   scanCount     = 0;
    private float freqWindow    = 1f;
    private List<float> odomTimes = new List<float>();
    private List<float> scanTimes = new List<float>();

    // Connessione ROS
    private bool rosConnected   = false;
    private float lastRosRx     = 0f;
    private float lastRosTx     = 0f;
    private bool txFlash        = false;
    private bool rxFlash        = false;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>("/odom", OnOdomReceived);
        ros.Subscribe<LaserScanMsg>("/scan", OnScanReceived);
        ros.Subscribe<PoseStampedMsg>("/goal_pose", OnGoalReceived);
        ros.Subscribe<PoseStampedMsg>("/amcl_pose", OnAmclPoseReceived);

        InvokeRepeating("UpdateFrequencies", 1f, 0.5f);
        InvokeRepeating("CheckROSConnection", 1f, 1f);
    }

    void OnOdomReceived(OdometryMsg msg)
    {
        rosConnected = true;
        lastRosRx    = Time.time;
        rxFlash      = true;

        robotPos = new Vector2((float)msg.pose.pose.position.x,
                               (float)msg.pose.pose.position.y);
        robotSpeed = Mathf.Sqrt(
            (float)(msg.twist.twist.linear.x * msg.twist.twist.linear.x) +
            (float)(msg.twist.twist.linear.y * msg.twist.twist.linear.y)
        );

        odomTimes.Add(Time.time);
        UpdateNavStatus();
    }

    void OnScanReceived(LaserScanMsg msg)
    {
        lastRosRx = Time.time;
        rxFlash   = true;
        scanTimes.Add(Time.time);

        // Calcola ostacolo più vicino
        float minDist = float.MaxValue;
        foreach (float r in msg.ranges)
        {
            if (!float.IsNaN(r) && !float.IsInfinity(r) &&
                r > msg.range_min && r < msg.range_max)
            {
                if (r < minDist) minDist = r;
            }
        }
        nearestObstacle = minDist == float.MaxValue ? -1f : minDist;
    }

    void OnGoalReceived(PoseStampedMsg msg)
    {
        lastRosTx = Time.time;
        txFlash   = true;
        goalPos   = new Vector2((float)msg.pose.position.x,
                                (float)msg.pose.position.y);
        hasGoal   = true;
        navStatus = "NAVIGATING";
    }

    void OnAmclPoseReceived(PoseStampedMsg msg)
    {
        lastRosRx = Time.time;
        rxFlash   = true;
    }

    void UpdateNavStatus()
    {
        if (!hasGoal)
        {
            navStatus = "IDLE";
            distanceToGoal = -1f;
            eta = -1f;
            return;
        }

        distanceToGoal = Vector2.Distance(robotPos, goalPos);

        if (distanceToGoal < 0.3f)
        {
            navStatus = "GOAL REACHED";
            hasGoal   = false;
            eta       = 0f;
        }
        else if (nearestObstacle > 0f && nearestObstacle < minObstacleDist)
        {
            navStatus = "OBSTACLE!";
        }
        else
        {
            navStatus = "NAVIGATING";
        }

        // ETA
        if (robotSpeed > 0.05f)
            eta = distanceToGoal / robotSpeed;
        else
            eta = -1f;
    }

    void UpdateFrequencies()
    {
        float now = Time.time;
        float windowStart = now - freqWindow;

        odomTimes.RemoveAll(t => t < windowStart);
        scanTimes.RemoveAll(t => t < windowStart);

        odomFreq = odomTimes.Count / freqWindow;
        scanFreq = scanTimes.Count / freqWindow;
    }

    void CheckROSConnection()
    {
        rosConnected = (Time.time - lastRosRx) < 3f;
    }

    void Update()
    {
        UpdateUI();

        // Reset flash dopo un frame
        if (rxFlash) Invoke("ResetRxFlash", 0.1f);
        if (txFlash) Invoke("ResetTxFlash", 0.1f);
    }

    void ResetRxFlash() { rxFlash = false; }
    void ResetTxFlash() { txFlash = false; }

    void UpdateUI()
    {
        // Stato navigazione
        if (navStatusText != null)
        {
            string color = navStatus switch
            {
                "NAVIGATING"   => "#00FF88",
                "GOAL REACHED" => "#00FFFF",
                "OBSTACLE!"    => "#FF3333",
                _              => "#AAAAAA"
            };
            navStatusText.text = $"Status: <color={color}>{navStatus}</color>";
        }

        // Distanza al goal
        if (distanceToGoalText != null)
        {
            distanceToGoalText.text = hasGoal
                ? $"Goal: {distanceToGoal:F1} m"
                : "Goal: --";
        }

        // ETA
        if (etaText != null)
        {
            etaText.text = eta > 0f
                ? $"ETA: {eta:F0} s"
                : "ETA: --";
        }

        // Ostacolo più vicino
        if (nearestObstacleText != null)
        {
            string obsColor = nearestObstacle < minObstacleDist ? "#FF3333" :
                              nearestObstacle < minObstacleDist * 2f ? "#FFAA00" : "#00FF88";
            nearestObstacleText.text = nearestObstacle > 0f
                ? $"Obstacle: <color={obsColor}>{nearestObstacle:F2} m</color>"
                : "Obstacle: --";
        }

        // Frequenze topic
        if (odomFreqText != null)
            odomFreqText.text = $"Odom: {odomFreq:F1} Hz";

        if (scanFreqText != null)
            scanFreqText.text = $"Scan: {scanFreq:F1} Hz";

        // Connessione ROS
        if (rosConnectionText != null)
        {
            string connColor = rosConnected ? "#00FF88" : "#FF3333";
            string connStr   = rosConnected ? "CONNECTED" : "DISCONNECTED";
            rosConnectionText.text = $"ROS: <color={connColor}>{connStr}</color>";
        }

        // Frecce TX/RX
        if (rosArrowsText != null)
        {
            string txColor = txFlash ? "#00FFFF" : "#444444";
            string rxColor = rxFlash ? "#00FF88" : "#444444";
            rosArrowsText.text = $"<color={txColor}>▲TX</color>  <color={rxColor}>▼RX</color>";
        }
    }
}