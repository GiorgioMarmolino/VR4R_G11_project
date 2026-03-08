using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;

/// <summary>
/// Widget VR che mostra:
/// - Velocità lineare X e angolare Z
/// - Posizione X Y del robot
/// - Goal pose
/// Va attaccato al Canvas World Space.
/// </summary>
public class RobotVelocityHUD : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string odomTopic = "/odom";

    [Header("Riferimenti UI - Velocità")]
    [Tooltip("Trascina qui il Text per la velocità lineare")]
    public Text linearVelocityText;
    [Tooltip("Trascina qui il Text per la velocità angolare")]
    public Text angularVelocityText;

    [Header("Riferimenti UI - Posizione e Goal")]
    [Tooltip("Trascina qui il Text per la posizione")]
    public Text positionText;
    [Tooltip("Trascina qui il Text per il goal pose")]
    public Text goalPoseText;

    [Header("Formattazione")]
    public string linearLabel  = "Linear X:";
    public string angularLabel = "Angular Z:";
    public string unit         = "m/s";

    // Velocità
    private float linearX  = 0f;
    private float angularZ = 0f;

    // Posizione
    private float posX = 0f;
    private float posY = 0f;
    public Text yawText;
    private float yawDegrees = 0f;

    // Goal
    private float goalX  = 0f;
    private float goalY  = 0f;
    private bool hasGoal = false;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OdometryMsg>(odomTopic, OnOdometryReceived);
        ros.Subscribe<RosMessageTypes.Geometry.PoseStampedMsg>("/goal_pose", OnGoalPoseReceived);
        Debug.Log($"[RobotVelocityHUD] In ascolto su: {odomTopic} e /goal_pose");
    }

    void OnOdometryReceived(OdometryMsg msg)
    {
        linearX  = (float)msg.twist.twist.linear.x;
        angularZ = (float)msg.twist.twist.angular.z;
        posX     = (float)msg.pose.pose.position.x;
        posY     = (float)msg.pose.pose.position.y;
        // Calcola yaw dal quaternione
        float qx = (float)msg.pose.pose.orientation.x;
        float qy = (float)msg.pose.pose.orientation.y;
        float qz = (float)msg.pose.pose.orientation.z;
        float qw = (float)msg.pose.pose.orientation.w;

        // Formula per estrarre yaw da quaternione ROS
        yawDegrees = Mathf.Atan2(2f * (qw * qz + qx * qy), 1f - 2f * (qy * qy + qz * qz)) * Mathf.Rad2Deg;
    }

    void OnGoalPoseReceived(RosMessageTypes.Geometry.PoseStampedMsg msg)
    {
        goalX   = (float)msg.pose.position.x;
        goalY   = (float)msg.pose.position.y;
        hasGoal = true;
    }

    void Update()
    {
        if (linearVelocityText != null)
            linearVelocityText.text  = $"{linearLabel} {linearX:F2} {unit}";

        if (angularVelocityText != null)
            angularVelocityText.text = $"{angularLabel} {angularZ:F2} rad/s";

        if (positionText != null)
            positionText.text = $"Pos: X={posX:F2} Y={posY:F2} m";

        if (goalPoseText != null)
            goalPoseText.text = hasGoal
                ? $"Goal: X={goalX:F2} Y={goalY:F2} m"
                : "Goal: nessuno";
        if (yawText != null)
            yawText.text = $"Yaw: {yawDegrees:F1}°";
    }
}