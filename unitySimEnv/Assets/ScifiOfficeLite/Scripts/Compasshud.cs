using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;

/// <summary>
/// Bussola VR che mostra l'orientamento del robot rispetto alla mappa.
/// Usa un Text ruotato o una UI Image per mostrare la direzione.
/// Va attaccato al Canvas World Space.
/// </summary>
public class CompassHUD : MonoBehaviour
{
    [Header("Riferimenti UI")]
    [Tooltip("RectTransform dell'ago della bussola da ruotare")]
    public RectTransform compassNeedle;
    [Tooltip("Text che mostra i gradi e la direzione cardinale")]
    public Text compassText;

    private float yawDegrees = 0f;

    void Start()
    {
        ROSConnection.GetOrCreateInstance()
            .Subscribe<OdometryMsg>("/odom", OnOdomReceived);
    }

    void OnOdomReceived(OdometryMsg msg)
    {
        float qx = (float)msg.pose.pose.orientation.x;
        float qy = (float)msg.pose.pose.orientation.y;
        float qz = (float)msg.pose.pose.orientation.z;
        float qw = (float)msg.pose.pose.orientation.w;

        // Estrai yaw dal quaternione ROS
        yawDegrees = Mathf.Atan2(2f * (qw * qz + qx * qy),
                                  1f - 2f * (qy * qy + qz * qz)) * Mathf.Rad2Deg;
    }

    void Update()
    {
        // Ruota l'ago della bussola
        if (compassNeedle != null)
            compassNeedle.localRotation = Quaternion.Euler(0, 0, yawDegrees);

        // Testo con gradi e direzione cardinale
        if (compassText != null)
        {
            string cardinal = GetCardinal(yawDegrees);
            compassText.text = $"{cardinal} {yawDegrees:F0}°";
        }
    }

    string GetCardinal(float deg)
    {
        // Normalizza tra 0 e 360
        float d = (deg % 360f + 360f) % 360f;

        if (d < 22.5f  || d >= 337.5f) return "N";
        if (d < 67.5f)                  return "NE";
        if (d < 112.5f)                 return "E";
        if (d < 157.5f)                 return "SE";
        if (d < 202.5f)                 return "S";
        if (d < 247.5f)                 return "SW";
        if (d < 292.5f)                 return "W";
        return "NW";
    }
}