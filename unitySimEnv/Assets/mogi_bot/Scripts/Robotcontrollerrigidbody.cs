using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

/// <summary>
/// Controller cinematico per mogi_bot basato su Rigidbody.
/// Muove il robot direttamente senza dipendere dalla fisica delle ruote.
/// Supporta rotazione su se stesso, movimento lineare e combinati.
/// Va attaccato al GameObject principale del robot (mogi_bot).
/// </summary>
public class RobotControllerRigidbody : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string topicName = "/cmd_vel";

    [Header("Parametri del Robot")]
    public float maxLinearSpeed  = 1.0f;  // m/s
    public float maxAngularSpeed = 2.0f;  // rad/s

    [Header("Riferimento Ruote (opzionale, solo visivo)")]
    [Tooltip("Trascina left_wheel per animazione visiva")]
    public Transform leftWheelVisual;
    [Tooltip("Trascina right_wheel per animazione visiva")]
    public Transform rightWheelVisual;
    public float wheelRadius = 0.1f;
    public float wheelSeparation = 0.3f;

    // Velocità ricevute da ROS
    private float linearSpeed  = 0f;
    private float angularSpeed = 0f;

    // Rigidbody del robot
    private Rigidbody rb;

    void Start()
    {
        // Sottoscrivi al topic /cmd_vel
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(topicName, OnTwistReceived);
        Debug.Log($"[RobotControllerRigidbody] In ascolto su: {topicName}");

        // Ottieni il Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            // Cerca nei figli
            rb = GetComponentInChildren<Rigidbody>();
        }

        if (rb == null)
        {
            Debug.LogError("[RobotControllerRigidbody] Nessun Rigidbody trovato! " +
                           "Aggiungi un Rigidbody al GameObject del robot.");
        }
        else
        {
            // Assicurati che sia cinematico
            rb.isKinematic = true;
            Debug.Log($"[RobotControllerRigidbody] Rigidbody trovato su: {rb.name}");
        }
    }

    void OnTwistReceived(TwistMsg msg)
    {
        linearSpeed  = Mathf.Clamp((float)msg.linear.x,  -maxLinearSpeed,  maxLinearSpeed);
        angularSpeed = Mathf.Clamp((float)msg.angular.z, -maxAngularSpeed, maxAngularSpeed);
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // --- Movimento del robot ---

        // Rotazione: angular.z positivo in ROS = antiorario = sinistra
        // In Unity Y positivo = orario, quindi invertiamo il segno
        float deltaAngle = -angularSpeed * Time.fixedDeltaTime * Mathf.Rad2Deg;
        Quaternion newRotation = rb.rotation * Quaternion.Euler(0f, deltaAngle, 0f);

        // Traslazione nella direzione forward del robot
        Vector3 newPosition = rb.position + 
                              rb.transform.forward * linearSpeed * Time.fixedDeltaTime;

        // MovePosition e MoveRotation sono i metodi corretti per Rigidbody cinematico
        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);

        // --- Animazione visiva delle ruote (opzionale) ---
        AnimateWheels();
    }

    void AnimateWheels()
    {
        if (leftWheelVisual == null || rightWheelVisual == null) return;

        // Calcola velocità ruote dalla cinematica differenziale
        float rightWheelSpeed = (linearSpeed + angularSpeed * wheelSeparation / 2f) / wheelRadius;
        float leftWheelSpeed  = (linearSpeed - angularSpeed * wheelSeparation / 2f) / wheelRadius;

        // Ruota le mesh delle ruote attorno all'asse X locale
        float rightDeg = rightWheelSpeed * Time.fixedDeltaTime * Mathf.Rad2Deg;
        float leftDeg  = leftWheelSpeed  * Time.fixedDeltaTime * Mathf.Rad2Deg;

        rightWheelVisual.Rotate(rightDeg, 0f, 0f, Space.Self);
        leftWheelVisual.Rotate(leftDeg,  0f, 0f, Space.Self);
    }
}