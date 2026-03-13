using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System.Collections.Generic;

/// <summary>
/// Imposta il goal del robot in VR usando il controller Meta Quest.
/// - Guarda verso il pavimento per vedere il marker
/// - Premi il trigger del controller destro per confermare il goal
/// - Il controller vibra come feedback aptico
/// Va attaccato al RightHand Controller nell'XR Origin.
/// </summary>
public class VRGoalSetter : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string goalTopic = "/goal_pose_request"; // invece di /goal_pose
    public string mapFrame  = "map";

    [Header("Impostazioni Marker")]
    public Color markerColor = new Color(0f, 1f, 0.5f, 0.8f);
    public float markerSize  = 0.3f;

    [Header("Feedback Aptico")]
    [Range(0f, 1f)] public float hapticsAmplitude = 0.5f;
    public float hapticsDuration = 0.1f;

    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui base_link — serve per calcolare le coordinate ROS corrette")]
    public Transform robotBaseLink;

    [Header("Raycast")]
    public float rayLength = 10f;

    // Componenti interni
    private GameObject markerInstance;
    private GameObject arrowInstance;
    private ROSConnection ros;

    // Stato
    private bool isAiming          = false;
    private bool triggerWasPressed = false;
    private Vector3 goalPosition;
    private float goalYaw      = 0f;
    private Vector3 startPosition;

    // Controller XR
    private InputDevice rightHandDevice;

    void Awake()
    {
        CreateMarker();
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(goalTopic);

        TryFindRightController();

        if (robotBaseLink != null)
            startPosition = robotBaseLink.position;
        else
        {
            startPosition = Vector3.zero;
            Debug.LogWarning("[VRGoalSetter] robotBaseLink non assegnato!");
        }

        Debug.Log($"[VRGoalSetter] Pronto. Pubblica goal su: {goalTopic}");
    }

    void TryFindRightController()
    {
        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices
        );
        if (devices.Count > 0)
            rightHandDevice = devices[0];
    }

    void CreateMarker()
    {
        markerInstance = new GameObject("GoalMarker");

        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null) urpShader = Shader.Find("Sprites/Default");

        // Cerchio base
        GameObject circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        circle.transform.SetParent(markerInstance.transform);
        circle.transform.localPosition = Vector3.zero;
        circle.transform.localScale    = new Vector3(markerSize, 0.005f, markerSize);
        Destroy(circle.GetComponent<Collider>());
        Material mat = new Material(urpShader);
        mat.color = markerColor;
        circle.GetComponent<Renderer>().material = mat;

        // Freccia direzione
        arrowInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arrowInstance.transform.SetParent(markerInstance.transform);
        arrowInstance.transform.localPosition = new Vector3(0, 0.01f, markerSize * 0.4f);
        arrowInstance.transform.localScale    = new Vector3(0.05f, 0.02f, markerSize * 0.5f);
        Destroy(arrowInstance.GetComponent<Collider>());
        Material arrowMat = new Material(urpShader);
        arrowMat.color = new Color(1f, 0.8f, 0f, 1f);
        arrowInstance.GetComponent<Renderer>().material = arrowMat;

        markerInstance.SetActive(false);
    }

    void Update()
    {
        if (!rightHandDevice.isValid)
            TryFindRightController();

        HandleRayCast();
        HandleInput();
    }

    void HandleRayCast()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Ray dal centro dello schermo nella direzione della camera
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0);
        Ray ray = cam.ScreenPointToRay(screenCenter);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayLength))
        {
            isAiming     = true;
            goalPosition = hit.point;
            goalYaw      = cam.transform.eulerAngles.y;

            if (markerInstance != null)
            {
                markerInstance.SetActive(true);
                // Alza il marker sopra il punto colpito per evitare z-fighting
                markerInstance.transform.position = hit.point + Vector3.up * 0.15f;
                markerInstance.transform.rotation = Quaternion.Euler(0, goalYaw, 0);

                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 4f);
                markerInstance.transform.localScale = Vector3.one * pulse;
            }
        }
        else
        {
            isAiming = false;
            if (markerInstance != null)
                markerInstance.SetActive(false);
        }
    }

    void HandleInput()
    {
        // Trigger del controller destro fisico (Meta Quest)
        bool triggerPressed = false;
        rightHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);

        // Fallback tastiera — Spazio per XR Device Simulator
        if (Input.GetKeyDown(KeyCode.Space))
            triggerPressed = true;

        // Rising edge — invia solo al momento della pressione
        if (triggerPressed && !triggerWasPressed)
        {
            if (isAiming)
            {
                PublishGoal();
                TriggerHaptics();
            }
            else
            {
                Debug.Log("[VRGoalSetter] Trigger premuto ma non punta a nessuna superficie!");
            }
        }

        triggerWasPressed = triggerPressed;
    }

    void PublishGoal()
    {
        // Converti posizione Unity → ROS
        // Unity: Y-up Left-handed → ROS: Z-up Right-handed
        Vector3 unityPos = goalPosition - startPosition;
        float rosX = unityPos.z;
        float rosY = -unityPos.x;

        // Converti yaw Unity → ROS
        float rosYaw = -goalYaw * Mathf.Deg2Rad;
        Quaternion rosRotation = new Quaternion(0, 0, Mathf.Sin(rosYaw * 0.5f), Mathf.Cos(rosYaw * 0.5f));

        double unixTime = (System.DateTime.UtcNow -
                          new System.DateTime(1970, 1, 1)).TotalSeconds;
        uint sec     = (uint)System.Math.Floor(unixTime);
        uint nanosec = (uint)((unixTime - sec) * 1e9);

        PoseStampedMsg goalMsg = new PoseStampedMsg
        {
            header = new HeaderMsg
            {
                frame_id = mapFrame,
                stamp    = new TimeMsg { sec = (int)sec, nanosec = nanosec }
            },
            pose = new PoseMsg
            {
                position    = new PointMsg(rosX, rosY, 0),
                orientation = new QuaternionMsg(rosRotation.x, rosRotation.y, rosRotation.z, rosRotation.w)
            }
        };

        ros.Publish(goalTopic, goalMsg);
        Debug.Log($"[VRGoalSetter] Goal ROS: X={rosX:F2}, Y={rosY:F2}, Yaw={rosYaw * Mathf.Rad2Deg:F1}°");
    }

    void TriggerHaptics()
    {
        if (rightHandDevice.isValid)
            rightHandDevice.SendHapticImpulse(0, hapticsAmplitude, hapticsDuration);
    }

    void OnDisable()
    {
        if (markerInstance != null)
            markerInstance.SetActive(false);
    }
}