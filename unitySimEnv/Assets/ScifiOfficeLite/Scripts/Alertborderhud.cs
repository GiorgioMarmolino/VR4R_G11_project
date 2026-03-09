using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;

/// <summary>
/// Alert visivo VR — colora il bordo dello schermo in base allo stato del robot.
/// 
/// Verde  → tutto ok
/// Giallo → ostacolo vicino (warning)
/// Rosso  → ostacolo molto vicino / robot bloccato (danger)
/// Blu    → goal raggiunto
/// Grigio → ROS disconnesso
/// 
/// Va attaccato al Canvas World Space. Richiede un'Image UI a bordo schermo.
/// </summary>
public class AlertBorderHUD : MonoBehaviour
{
    [Header("Riferimenti UI")]
    [Tooltip("Image UI che copre il bordo dello schermo (usa un Sprite con bordi)")]
    public Image borderImage;

    [Header("Soglie")]
    public float dangerDistance  = 0.4f;  // metri — rosso
    public float warningDistance = 0.8f;  // metri — giallo
    public float rosTimeoutSecs  = 3f;    // secondi senza messaggi → grigio

    [Header("Colori")]
    public Color colorOk         = new Color(0f,    1f,    0.4f,  0.3f); // verde
    public Color colorWarning    = new Color(1f,    0.7f,  0f,    0.4f); // giallo
    public Color colorDanger     = new Color(1f,    0.1f,  0.1f,  0.6f); // rosso
    public Color colorGoalReached= new Color(0f,    0.8f,  1f,    0.5f); // blu
    public Color colorDisconnected = new Color(0.5f, 0.5f, 0.5f, 0.3f); // grigio

    [Header("Animazione")]
    public float pulseSpeed = 3f; // velocità pulsazione in stato danger

    // Stato interno
    private float nearestObstacle = float.MaxValue;
    private float lastRosTime     = 0f;
    private bool  goalReached     = false;
    private float goalReachedTime = 0f;

    public enum AlertLevel { Ok, Warning, Danger, GoalReached, Disconnected }
    private AlertLevel currentAlert = AlertLevel.Disconnected;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<LaserScanMsg>("/scan", OnScanReceived);
        ros.Subscribe<OdometryMsg>("/odom", OnOdomReceived);
        ros.Subscribe<PoseStampedMsg>("/goal_pose", OnGoalReceived);

        if (borderImage != null)
            borderImage.enabled = true;
    }

    void OnScanReceived(LaserScanMsg msg)
    {
        lastRosTime = Time.time;
        float minDist = float.MaxValue;
        foreach (float r in msg.ranges)
        {
            if (!float.IsNaN(r) && !float.IsInfinity(r) &&
                r > msg.range_min && r < msg.range_max)
            {
                if (r < minDist) minDist = r;
            }
        }
        nearestObstacle = minDist == float.MaxValue ? float.MaxValue : minDist;
    }

    void OnOdomReceived(OdometryMsg msg) { lastRosTime = Time.time; }

    void OnGoalReceived(PoseStampedMsg msg)
    {
        lastRosTime = Time.time;
        goalReached = false;
    }

    public void NotifyGoalReached()
    {
        goalReached     = true;
        goalReachedTime = Time.time;
    }

    void Update()
    {
        UpdateAlertLevel();
        UpdateBorderVisual();
    }

    void UpdateAlertLevel()
    {
        // ROS disconnesso
        if (Time.time - lastRosTime > rosTimeoutSecs)
        {
            currentAlert = AlertLevel.Disconnected;
            return;
        }

        // Goal raggiunto (mostra per 3 secondi)
        if (goalReached && Time.time - goalReachedTime < 3f)
        {
            currentAlert = AlertLevel.GoalReached;
            return;
        }
        else if (goalReached)
        {
            goalReached = false;
        }

        // Ostacoli
        if (nearestObstacle < dangerDistance)
            currentAlert = AlertLevel.Danger;
        else if (nearestObstacle < warningDistance)
            currentAlert = AlertLevel.Warning;
        else
            currentAlert = AlertLevel.Ok;
    }

    void UpdateBorderVisual()
    {
        if (borderImage == null) return;

        Color targetColor;
        switch (currentAlert)
        {
            case AlertLevel.Warning:
                targetColor = colorWarning;
                break;
            case AlertLevel.Danger:
                // Pulsazione rapida in caso di pericolo
                float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
                targetColor = Color.Lerp(colorDanger,
                    new Color(colorDanger.r, colorDanger.g, colorDanger.b, 0.1f), pulse);
                borderImage.color = targetColor;
                return;
            case AlertLevel.GoalReached:
                targetColor = colorGoalReached;
                break;
            case AlertLevel.Disconnected:
                targetColor = colorDisconnected;
                break;
            default:
                targetColor = colorOk;
                break;
        }

        // Transizione morbida
        borderImage.color = Color.Lerp(borderImage.color, targetColor, Time.deltaTime * 5f);
    }

    // Proprietà pubblica per altri script
    public AlertLevel CurrentAlert => currentAlert;
}