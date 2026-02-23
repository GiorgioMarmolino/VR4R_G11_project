using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

/// <summary>
/// Controlla il mogi_bot ricevendo messaggi Twist da ROS2 sul topic /cmd_vel
/// e applicando le velocità agli Articulation Body delle ruote.
/// </summary>
public class RobotController : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    [Tooltip("Nome del topic ROS2 su cui arrivano i comandi di velocità")]
    public string topicName = "/cmd_vel";

    [Header("Riferimenti alle Ruote")]
    [Tooltip("Trascinare qui l'oggetto left_wheel dalla Hierarchy")]
    public ArticulationBody leftWheel;
    [Tooltip("Trascinare qui l'oggetto right_wheel dalla Hierarchy")]
    public ArticulationBody rightWheel;

    [Header("Parametri del Robot")]
    [Tooltip("Distanza tra le due ruote in metri (misurata dall'URDF: 0.15 * 2 = 0.3)")]
    public float wheelSeparation = 0.3f;
    [Tooltip("Raggio delle ruote in metri (dall'URDF: 0.1)")]
    public float wheelRadius = 0.1f;
    [Tooltip("Velocità massima delle ruote in gradi al secondo")]
    public float maxWheelSpeed = 300f;

    // Variabili interne per memorizzare la velocità ricevuta
    private float linearSpeed = 0f;
    private float angularSpeed = 0f;

    void Start()
    {
        // Ottieni il componente ROSConnection e registra il subscriber
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(topicName, OnTwistReceived);
        Debug.Log($"[RobotController] In ascolto su topic: {topicName}");

        // Verifica che le ruote siano state assegnate
        if (leftWheel == null || rightWheel == null)
        {
            Debug.LogError("[RobotController] ERRORE: Assegna leftWheel e rightWheel nell'Inspector!");
        }
    }

    /// <summary>
    /// Callback chiamata ogni volta che arriva un messaggio Twist da ROS2
    /// </summary>
    void OnTwistReceived(TwistMsg msg)
    {
        // Leggi velocità lineare (avanti/indietro) e angolare (rotazione)
        linearSpeed = (float)msg.linear.x;
        angularSpeed = (float)msg.angular.z;
        Debug.Log($"Ricevuto: linear={linearSpeed}, angular={angularSpeed}");
    }

    void FixedUpdate()
    {
        if (leftWheel == null || rightWheel == null) return;

        // Calcolo cinematica differenziale
        // Velocità ruota destra e sinistra in m/s
        float rightWheelSpeed = (linearSpeed + angularSpeed * wheelSeparation / 2f);
        float leftWheelSpeed  = (linearSpeed - angularSpeed * wheelSeparation / 2f);

        // Converti da m/s a gradi/s
        float rightDegPerSec = (rightWheelSpeed / wheelRadius) * Mathf.Rad2Deg;
        float leftDegPerSec  = (leftWheelSpeed  / wheelRadius) * Mathf.Rad2Deg;

        // Limita la velocità massima
        rightDegPerSec = Mathf.Clamp(rightDegPerSec, -maxWheelSpeed, maxWheelSpeed);
        leftDegPerSec  = Mathf.Clamp(leftDegPerSec,  -maxWheelSpeed, maxWheelSpeed);

        // Applica la velocità agli Articulation Body
        SetWheelVelocity(rightWheel, rightDegPerSec);
        SetWheelVelocity(leftWheel,  leftDegPerSec);
    }

    /// <summary>
    /// Imposta la velocità di rotazione di una ruota tramite ArticulationBody
    /// </summary>
    void SetWheelVelocity(ArticulationBody wheel, float degPerSec)
    {
        ArticulationDrive drive = wheel.xDrive;
        drive.targetVelocity = degPerSec;
        drive.driveType = ArticulationDriveType.Velocity;
        wheel.xDrive = drive;
    }
}