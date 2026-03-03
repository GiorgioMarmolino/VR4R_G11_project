using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;

public class OdometryTopicPublisher : MonoBehaviour
{
    [Header("⚙️ ROS2 Settings")]
    [SerializeField] private string odomTopicName = "/odom";
    [SerializeField] private string frameId = "odom";
    [SerializeField] private string childFrameId = "base_footprint";

    [Header("🤖 Robot Settings")]
    [SerializeField] private Transform baseLink;
    [SerializeField] private Rigidbody robotRigidbody;  // ← NUOVO: Riferimento al Rigidbody

    [Header("📊 Covariance Settings (lower = more trust)")]
    [SerializeField] [Range(0.0001f, 1f)] private float positionCovariance = 0.01f;
    [SerializeField] [Range(0.0001f, 1f)] private float orientationCovariance = 0.01f;
    [SerializeField] [Range(0.0001f, 1f)] private float twistCovariance = 0.001f; // ← NUOVO: Covarianza per twist

    // Internal variables
    private ROSConnection ros;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool isInitialized = false;

    void Awake()
    {
        if (baseLink == null)
            baseLink = this.transform;
        
        // Auto-rileva il Rigidbody se non assegnato manualmente
        if (robotRigidbody == null)
            robotRigidbody = baseLink.GetComponent<Rigidbody>();
    }

    void Start()
    {
        // Get ROSConnection singleton
        ros = ROSConnection.GetOrCreateInstance();
        
        if (ros != null)
        {
            // Register the topic
            ros.RegisterPublisher<OdometryMsg>(odomTopicName);
            
            // Save initial position as odometry zero
            startPosition = baseLink.position;
            startRotation = baseLink.rotation;
            isInitialized = true;
            
            Debug.Log($"[OdometryPublisher] Inizializzato: pubblicando su '{odomTopicName}'");
        }
        else
        {
            Debug.LogError("[OdometryPublisher] ROSConnection.instance non trovato! Assicurati che ROS-TCP-Connector sia configurato.");
        }
    }

    void Update()
    {
        if (!isInitialized || ros == null) return;
        
        PublishOdometry();
    }

    private void PublishOdometry()
    {
        // === Calcola posa relativa (STESSA LOGICA del tuo TFPublisher) ===
        Vector3 unityPos = baseLink.position - startPosition;
        Vector3 rosPosition = new Vector3(unityPos.z, -unityPos.x, unityPos.y);
        
        Quaternion unityRot = Quaternion.Inverse(startRotation) * baseLink.rotation;
        Quaternion rosRotation = new Quaternion(
            unityRot.z,
            -unityRot.x,
            -unityRot.y,
            unityRot.w
        );
        
        // Normalizza: w deve essere positivo
        if (rosRotation.w < 0)
        {
            rosRotation = new Quaternion(
                -rosRotation.x,
                -rosRotation.y,
                -rosRotation.z,
                -rosRotation.w
            );
        }

        // === Timestamp ROS2 (STESSO METODO del tuo TFPublisher) ===
        double unixTime = (System.DateTime.UtcNow - 
                          new System.DateTime(1970, 1, 1)).TotalSeconds;

        uint sec = (uint)System.Math.Floor(unixTime);
        uint nanosec = (uint)((unixTime - sec) * 1e9);

        TimeMsg stamp = new TimeMsg
        {
            sec = (int)sec,
            nanosec = nanosec
        };

        // === CALCOLA VELOCITÀ DAL RIGIDBODY ===
        Vector3 linearVelocity = Vector3.zero;
        Vector3 angularVelocity = Vector3.zero;
        
        if (robotRigidbody != null)
        {
            // Leggi le velocità dal Rigidbody (coordinate Unity: Y-up, Z-forward)
            linearVelocity = robotRigidbody.velocity;
            angularVelocity = robotRigidbody.angularVelocity;
        }
        
        // Converti velocità da Unity a ROS (Z-up, X-forward)
        // Unity: X=Right, Y=Up, Z=Forward  →  ROS: X=Forward, Y=Left, Z=Up
        Vector3 rosLinearVel = new Vector3(linearVelocity.z, -linearVelocity.x, linearVelocity.y);
        Vector3 rosAngularVel = new Vector3(angularVelocity.z, -angularVelocity.x, -angularVelocity.y);

        // === Costruisci messaggio Odometry ===
        var odomMsg = new OdometryMsg
        {
            header = new HeaderMsg
            {
                stamp = stamp,
                frame_id = frameId
            },
            child_frame_id = childFrameId,
            
            pose = new PoseWithCovarianceMsg
            {
                pose = new PoseMsg
                {
                    position = new PointMsg
                    {
                        x = (double)rosPosition.x,
                        y = (double)rosPosition.y,
                        z = (double)rosPosition.z
                    },
                    orientation = new QuaternionMsg
                    {
                        x = (double)rosRotation.x,
                        y = (double)rosRotation.y,
                        z = (double)rosRotation.z,
                        w = (double)rosRotation.w
                    }
                },
                covariance = BuildDiagonalCovariance(positionCovariance, orientationCovariance)
            },
            
            twist = new TwistWithCovarianceMsg
            {
                twist = new TwistMsg  // ← NUOVO: Popoliamo il twist con le velocità reali
                {
                    linear = new Vector3Msg
                    {
                        x = (double)rosLinearVel.x,
                        y = (double)rosLinearVel.y,
                        z = (double)rosLinearVel.z
                    },
                    angular = new Vector3Msg
                    {
                        x = (double)rosAngularVel.x,
                        y = (double)rosAngularVel.y,
                        z = (double)rosAngularVel.z  // ← QUESTO ORA SEGNA LA VELOCITÀ ANGOLARE REALE!
                    }
                },
                covariance = BuildDiagonalCovariance(twistCovariance, twistCovariance)
            }
        };

        // === Pubblica! ===
        ros.Publish(odomTopicName, odomMsg);
    }

    // === Helper: Crea matrice di covarianza diagonale 6x6 (36 elementi, row-major) ===
    private double[] BuildDiagonalCovariance(float posVar, float rotVar)
    {
        var cov = new double[36];
        
        // Posizione (x, y, z)
        cov[0] = posVar;   // x
        cov[7] = posVar;   // y
        cov[14] = posVar;  // z
        
        // Rotazione (roll, pitch, yaw)
        cov[21] = rotVar;  // roll
        cov[28] = rotVar;  // pitch
        cov[35] = rotVar;  // yaw
        
        return cov;
    }

    // === Editor Utilities (opzionale, per validazione in Unity Editor) ===
    #if UNITY_EDITOR
    void OnValidate()
    {
        positionCovariance = Mathf.Max(positionCovariance, 0.0001f);
        orientationCovariance = Mathf.Max(orientationCovariance, 0.0001f);
        twistCovariance = Mathf.Max(twistCovariance, 0.0001f);
    }
    #endif
}