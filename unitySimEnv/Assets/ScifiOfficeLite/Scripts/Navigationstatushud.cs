using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Sensor;
using RosMessageTypes.Geometry;
using System.Collections.Generic;

/// <summary>
/// HUD di navigazione completo.
/// Connessione ROS rilevata tramite socket TCP reale — non si inganna.
/// </summary>
public class NavigationStatusHUD : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public float rosTimeoutSecs = 3f; // secondi senza messaggi = disconnesso

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
    public Text rosArrowsText;

    [Header("Impostazioni")]
    public float minObstacleDist = 0.5f;

    // Stato navigazione
    private string navStatus       = "IDLE";
    private float  distanceToGoal  = -1f;
    private float  eta             = -1f;
    private float  robotSpeed      = 0f;
    private Vector2 robotPos       = Vector2.zero;
    private Vector2 goalPos        = Vector2.zero;
    private bool    hasGoal        = false;

    // Ostacolo
    private float nearestObstacle = float.MaxValue;

    // Frequenze
    private float odomFreq = 0f;
    private float scanFreq = 0f;
    private float freqWindow = 1f;
    private List<float> odomTimes = new List<float>();
    private List<float> scanTimes = new List<float>();

    // Connessione ROS — rilevata via messaggi
    private bool  rosConnected        = false;
    private bool  firstMessageReceived = false;
    private float lastRosRx           = -999f;
    private bool  rxFlash             = false;
    private bool  txFlash             = false;
    private float rxFlashTime         = -999f;
    private float txFlashTime         = -999f;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>("/odom", OnOdomReceived);
        ros.Subscribe<LaserScanMsg>("/scan", OnScanReceived);
        ros.Subscribe<PoseStampedMsg>("/goal_pose", OnGoalReceived);
        ros.Subscribe<RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg>("/amcl_pose", OnAmclReceived);

        InvokeRepeating("UpdateFrequencies", 1f, 0.5f);
        InvokeRepeating("CheckROSConnection", 0.5f, 0.5f);
    }

    // ── Controlla connessione tramite ROSConnectionMonitor ───────────────────
    void CheckROSConnection()
    {
        bool wasConnected = rosConnected;
        rosConnected = ROSConnectionMonitor.IsConnected;

        if (!rosConnected)
        {
            rxFlashTime = -999f;
            odomTimes.Clear();
            scanTimes.Clear();
            odomFreq = 0f;
            scanFreq = 0f;
        }

        if (rosConnected && !wasConnected)
            Debug.Log("[NavigationStatusHUD] ROS connesso!");
        else if (!rosConnected && wasConnected)
            Debug.Log("[NavigationStatusHUD] ROS disconnesso.");
    }

    // ── Callbacks ROS ────────────────────────────────────────────────────────
    void OnOdomReceived(OdometryMsg msg)
    {
        firstMessageReceived = true;
        lastRosRx = Time.time;
        if (!rosConnected) return;

        // Flash RX max una volta ogni 0.3s
        if (Time.time - rxFlashTime > 0.3f)
            rxFlashTime = Time.time;

        robotPos   = new Vector2((float)msg.pose.pose.position.x,
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
        firstMessageReceived = true;
        lastRosRx = Time.time;
        if (!rosConnected) return;

        if (Time.time - rxFlashTime > 0.3f)
            rxFlashTime = Time.time;
        scanTimes.Add(Time.time);

        float minDist = float.MaxValue;
        foreach (float r in msg.ranges)
        {
            if (!float.IsNaN(r) && !float.IsInfinity(r) &&
                r > msg.range_min && r < msg.range_max)
                if (r < minDist) minDist = r;
        }
        nearestObstacle = minDist == float.MaxValue ? -1f : minDist;
    }

    void OnGoalReceived(PoseStampedMsg msg)
    {
        if (!rosConnected) return;
        txFlashTime = Time.time;
        goalPos     = new Vector2((float)msg.pose.position.x,
                                  (float)msg.pose.position.y);
        hasGoal     = true;
        navStatus   = "NAVIGATING";
    }

    void OnAmclReceived(RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg msg)
    {
        if (!rosConnected) return;
        rxFlashTime = Time.time;
    }

    // ── Logica navigazione ───────────────────────────────────────────────────
    void UpdateNavStatus()
    {
        if (!hasGoal) { navStatus = "IDLE"; distanceToGoal = -1f; eta = -1f; return; }

        distanceToGoal = Vector2.Distance(robotPos, goalPos);

        if (distanceToGoal < 0.3f)
        {
            navStatus = "GOAL REACHED";
            hasGoal   = false;
            eta       = 0f;
        }
        else if (nearestObstacle > 0f && nearestObstacle < minObstacleDist)
            navStatus = "OBSTACLE!";
        else
            navStatus = "NAVIGATING";

        eta = robotSpeed > 0.05f ? distanceToGoal / robotSpeed : -1f;
    }

    void UpdateFrequencies()
    {
        float windowStart = Time.time - freqWindow;
        odomTimes.RemoveAll(t => t < windowStart);
        scanTimes.RemoveAll(t => t < windowStart);
        odomFreq = rosConnected ? odomTimes.Count / freqWindow : 0f;
        scanFreq = rosConnected ? scanTimes.Count / freqWindow : 0f;
    }

    // ── UI ───────────────────────────────────────────────────────────────────
    void Update()
    {
        rxFlash = rosConnected && (Time.time - rxFlashTime) < 0.15f;
        txFlash = rosConnected && (Time.time - txFlashTime) < 0.15f;
        UpdateUI();
    }

    void UpdateUI()
    {
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

        if (distanceToGoalText != null)
            distanceToGoalText.text = hasGoal ? $"Goal: {distanceToGoal:F1} m" : "Goal: --";

        if (etaText != null)
            etaText.text = eta > 0f ? $"ETA: {eta:F0} s" : "ETA: --";

        if (nearestObstacleText != null)
        {
            string obsColor = nearestObstacle > 0f && nearestObstacle < minObstacleDist ? "#FF3333" :
                              nearestObstacle > 0f && nearestObstacle < minObstacleDist * 2f ? "#FFAA00" : "#00FF88";
            nearestObstacleText.text = nearestObstacle > 0f
                ? $"Obstacle: <color={obsColor}>{nearestObstacle:F2} m</color>"
                : "Obstacle: --";
        }

        if (odomFreqText != null)
            odomFreqText.text = $"Odom: {odomFreq:F1} Hz";

        if (scanFreqText != null)
            scanFreqText.text = $"Scan: {scanFreq:F1} Hz";

        if (rosConnectionText != null)
        {
            string connColor = rosConnected ? "#00FF88" : "#FF3333";
            string connStr   = rosConnected ? "CONNECTED" : "DISCONNECTED";
            rosConnectionText.text = $"ROS: <color={connColor}>{connStr}</color>";
        }

        if (rosArrowsText != null)
        {
            string txColor = txFlash ? "#00FFFF" : "#444444"; // ciano = TX
            string rxColor = rxFlash ? "#00FF88" : "#444444"; // verde  = RX
            rosArrowsText.text = $"<color={txColor}>▲TX</color>  <color={rxColor}>▼RX</color>";
        }
    }
}