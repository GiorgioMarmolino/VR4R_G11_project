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
    [SerializeField] private Rigidbody robotRigidbody;

    [Header("📊 Covariance Settings (lower = more trust)")]
    [SerializeField] [Range(0.0001f, 1f)] private float positionCovariance = 0.1f;
    [SerializeField] [Range(0.0001f, 1f)] private float orientationCovariance = 0.5f; // più alto = slam si fida meno dell'odometria in rotazione
    [SerializeField] [Range(0.0001f, 1f)] private float twistCovariance = 0.1f;

    private ROSConnection ros;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool isInitialized = false;

    void Awake()
    {
        if (baseLink == null)
            baseLink = this.transform;

        if (robotRigidbody == null)
            robotRigidbody = baseLink.GetComponent<Rigidbody>();
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        if (ros != null)
        {
            ros.RegisterPublisher<OdometryMsg>(odomTopicName);
            startPosition = baseLink.position;
            startRotation = baseLink.rotation;
            isInitialized = true;
            Debug.Log($"[OdometryPublisher] Inizializzato: pubblicando su '{odomTopicName}'");
        }
        else
        {
            Debug.LogError("[OdometryPublisher] ROSConnection non trovato!");
        }
    }

    void Update()
    {
        if (!isInitialized || ros == null) return;
        PublishOdometry();
    }

    private void PublishOdometry()
    {
        // Calcola posa relativa
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

        // Timestamp
        double unixTime = (System.DateTime.UtcNow -
                          new System.DateTime(1970, 1, 1)).TotalSeconds;
        uint sec = (uint)System.Math.Floor(unixTime);
        uint nanosec = (uint)((unixTime - sec) * 1e9);
        TimeMsg stamp = new TimeMsg { sec = (int)sec, nanosec = nanosec };

        // Velocità dal Rigidbody
        Vector3 linearVelocity = Vector3.zero;
        Vector3 angularVelocity = Vector3.zero;

        if (robotRigidbody != null)
        {
            linearVelocity = robotRigidbody.velocity;
            angularVelocity = robotRigidbody.angularVelocity;
        }

        // Converti velocità Unity -> ROS
        Vector3 rosLinearVel  = new Vector3(linearVelocity.z,   -linearVelocity.x,   linearVelocity.y);
        Vector3 rosAngularVel = new Vector3(angularVelocity.z, -angularVelocity.x, -angularVelocity.y);

        var odomMsg = new OdometryMsg
        {
            header = new HeaderMsg { stamp = stamp, frame_id = frameId },
            child_frame_id = childFrameId,

            pose = new PoseWithCovarianceMsg
            {
                pose = new PoseMsg
                {
                    position    = new PointMsg(rosPosition.x, rosPosition.y, rosPosition.z),
                    orientation = new QuaternionMsg(rosRotation.x, rosRotation.y, rosRotation.z, rosRotation.w)
                },
                covariance = BuildDiagonalCovariance(positionCovariance, orientationCovariance)
            },

            twist = new TwistWithCovarianceMsg
            {
                twist = new TwistMsg
                {
                    linear  = new Vector3Msg(rosLinearVel.x,  rosLinearVel.y,  rosLinearVel.z),
                    angular = new Vector3Msg(rosAngularVel.x, rosAngularVel.y, rosAngularVel.z)
                },
                covariance = BuildDiagonalCovariance(twistCovariance, twistCovariance)
            }
        };

        ros.Publish(odomTopicName, odomMsg);
    }

    private double[] BuildDiagonalCovariance(float posVar, float rotVar)
    {
        var cov = new double[36];
        cov[0]  = posVar;  // x
        cov[7]  = posVar;  // y
        cov[14] = posVar;  // z
        cov[21] = rotVar;  // roll
        cov[28] = rotVar;  // pitch
        cov[35] = rotVar;  // yaw ← il più importante per le rotazioni
        return cov;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        positionCovariance    = Mathf.Max(positionCovariance,    0.0001f);
        orientationCovariance = Mathf.Max(orientationCovariance, 0.0001f);
        twistCovariance       = Mathf.Max(twistCovariance,       0.0001f);
    }
#endif
}