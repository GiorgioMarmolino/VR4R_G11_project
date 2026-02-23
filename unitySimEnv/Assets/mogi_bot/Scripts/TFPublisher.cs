using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Tf2;
using RosMessageTypes.Geometry;
using RosMessageTypes.BuiltinInterfaces;

/// <summary>
/// Pubblica le trasformazioni (TF) del robot su ROS2.
/// Va attaccato al GameObject principale del robot (mogi_bot).
/// Pubblica: map->odom->base_footprint->base_link->scan_link->camera_link
/// </summary>
public class TFPublisher : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string topicName = "/tf";
    public float publishRate = 20f; // Hz

    [Header("Riferimenti ai link del robot")]
    [Tooltip("Trascina qui base_link dalla Hierarchy")]
    public Transform baseLink;
    [Tooltip("Trascina qui scan_link dalla Hierarchy")]
    public Transform scanLink;
    [Tooltip("Trascina qui camera_link dalla Hierarchy")]
    public Transform cameraLink;

    private ROSConnection ros;
    private float publishInterval;
    private float lastPublishTime;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TFMessageMsg>(topicName);

        publishInterval = 1f / publishRate;
        lastPublishTime = 0f;

        Debug.Log($"[TFPublisher] Pubblicando TF su: {topicName}");

        if (baseLink == null)
            Debug.LogError("[TFPublisher] ERRORE: Assegna base_link nell'Inspector!");
        if (scanLink == null)
            Debug.LogError("[TFPublisher] ERRORE: Assegna scan_link nell'Inspector!");
    }

    void Update()
    {
        if (Time.time - lastPublishTime >= publishInterval)
        {
            PublishTF();
            lastPublishTime = Time.time;
        }
    }

    void PublishTF()
    {
        TimeMsg stamp = new TimeMsg
        {
            sec = (int)Time.time,
            nanosec = (uint)((Time.time - Mathf.Floor(Time.time)) * 1e9)
        };

        // Lista di tutte le trasformazioni da pubblicare
        var transforms = new System.Collections.Generic.List<TransformStampedMsg>();

        // 1. map -> odom (statica, odom coincide con map in assenza di odometria reale)
        transforms.Add(CreateTransform("map", "odom", Vector3.zero, Quaternion.identity, stamp));

        // 2. odom -> base_footprint (posizione del robot nel mondo)
        if (baseLink != null)
        {
            // Converti da coordinate Unity a ROS manualmente (Unity: Y-up, ROS: Z-up)
            Vector3 unityPos = baseLink.position;
            Vector3 rosPosition = new Vector3(-unityPos.z, -unityPos.x, unityPos.y);
            Quaternion unityRot = baseLink.rotation;
            Quaternion rosRotation = new Quaternion(-unityRot.z, -unityRot.x, unityRot.y, unityRot.w);
            transforms.Add(CreateTransform("odom", "base_footprint", rosPosition, rosRotation, stamp));
        }

        // 3. base_footprint -> base_link (offset verticale, di solito zero)
        transforms.Add(CreateTransform("base_footprint", "base_link", Vector3.zero, Quaternion.identity, stamp));

        // 4. base_link -> scan_link
        if (scanLink != null && baseLink != null)
        {
            Vector3 unityRelPos = baseLink.InverseTransformPoint(scanLink.position);
            Vector3 relPos = new Vector3(-unityRelPos.z, -unityRelPos.x, unityRelPos.y);
            Quaternion unityRelRot = Quaternion.Inverse(baseLink.rotation) * scanLink.rotation;
            Quaternion relRot = new Quaternion(-unityRelRot.z, -unityRelRot.x, unityRelRot.y, unityRelRot.w);
            transforms.Add(CreateTransform("base_link", "scan_link", relPos, relRot, stamp));
        }

        // 5. base_link -> camera_link
        if (cameraLink != null && baseLink != null)
        {
            Vector3 unityRelPos = baseLink.InverseTransformPoint(cameraLink.position);
            Vector3 relPos = new Vector3(-unityRelPos.z, -unityRelPos.x, unityRelPos.y);
            Quaternion unityRelRot = Quaternion.Inverse(baseLink.rotation) * cameraLink.rotation;
            Quaternion relRot = new Quaternion(-unityRelRot.z, -unityRelRot.x, unityRelRot.y, unityRelRot.w);
            transforms.Add(CreateTransform("base_link", "camera_link", relPos, relRot, stamp));
        }

        // Pubblica tutte le TF in un unico messaggio
        TFMessageMsg tfMessage = new TFMessageMsg
        {
            transforms = transforms.ToArray()
        };

        ros.Publish(topicName, tfMessage);
    }

    TransformStampedMsg CreateTransform(string parentFrame, string childFrame,
                                         Vector3 position, Quaternion rotation, TimeMsg stamp)
    {
        return new TransformStampedMsg
        {
            header = new RosMessageTypes.Std.HeaderMsg
            {
                frame_id = parentFrame,
                stamp = stamp
            },
            child_frame_id = childFrame,
            transform = new TransformMsg
            {
                translation = new Vector3Msg(position.x, position.y, position.z),
                rotation = new QuaternionMsg(rotation.x, rotation.y, rotation.z, rotation.w)
            }
        };
    }
}