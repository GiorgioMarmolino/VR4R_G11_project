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
/// - Punta il controller verso il pavimento per vedere il raggio e il marker
/// - Ruota il polso per orientare il robot nel punto di arrivo
/// - Premi il trigger per confermare il goal
/// - Il controller vibra come feedback aptico
/// Va attaccato al RightHand Controller nell'XR Origin.
/// </summary>
public class VRGoalSetter : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string goalTopic = "/goal_pose";
    public string mapFrame  = "map";

    [Header("Riferimenti")]
    [Tooltip("Layer del pavimento per il raycast")]
    public LayerMask floorLayerMask;
    [Tooltip("Prefab del marker sul pavimento (opzionale - ne crea uno di default)")]
    public GameObject markerPrefab;

    [Header("Impostazioni Raggio")]
    public float rayLength       = 10f;
    public Color rayColorValid   = new Color(0f, 1f, 0.5f, 1f);   // verde
    public Color rayColorInvalid = new Color(1f, 0.3f, 0.3f, 1f); // rosso
    public float rayWidth        = 0.005f;

    [Header("Impostazioni Marker")]
    public Color markerColor = new Color(0f, 1f, 0.5f, 0.8f);
    public float markerSize  = 0.3f;

    [Header("Feedback Aptico")]
    [Range(0f, 1f)] public float hapticsAmplitude = 0.5f;
    public float hapticsDuration = 0.1f;

    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui base_link — serve per calcolare le coordinate ROS corrette")]
    public Transform robotBaseLink;

    // Componenti interni
    private LineRenderer lineRenderer;
    private GameObject markerInstance;
    private GameObject arrowInstance;
    private ROSConnection ros;

    // Stato
    private bool isAiming       = false;
    private bool triggerPressed = false;
    private Vector3 goalPosition;
    private float goalYaw    = 0f;
    private Vector3 startPosition;

    // Controller XR
    private XRController xrController;
    private InputDevice rightHandDevice;

    void Awake()
    {
        // Crea un GameObject separato per il LineRenderer
        // NON usare quello del controller — è gestito da XR Interaction Toolkit
        GameObject lineObj = new GameObject("GoalRayLine");
        lineObj.transform.SetParent(transform);
        lineObj.transform.localPosition = Vector3.zero;

        lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth    = rayWidth;
        lineRenderer.endWidth      = rayWidth * 0.5f;
        lineRenderer.material      = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled       = false;

        // Crea il marker sul pavimento
        CreateMarker();
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(goalTopic);

        // Trova il controller XR
        xrController = GetComponent<XRController>();

        // Trova il dispositivo controller destro
        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices
        );
        if (devices.Count > 0)
            rightHandDevice = devices[0];

        // Salva posizione iniziale del robot (stesso sistema del TFPublisher)
        if (robotBaseLink != null)
            startPosition = robotBaseLink.position;
        else
        {
            startPosition = Vector3.zero;
            Debug.LogWarning("[VRGoalSetter] robotBaseLink non assegnato — le coordinate ROS potrebbero essere errate!");
        }

        Debug.Log($"[VRGoalSetter] Pronto. Pubblica goal su: {goalTopic}");
    }

    void CreateMarker()
    {
        if (markerPrefab != null)
        {
            markerInstance = Instantiate(markerPrefab);
        }
        else
        {
            // Crea un marker di default: cerchio + freccia
            markerInstance = new GameObject("GoalMarker");

            // Cerchio base
            GameObject circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            circle.transform.SetParent(markerInstance.transform);
            circle.transform.localPosition = Vector3.zero;
            circle.transform.localScale    = new Vector3(markerSize, 0.01f, markerSize);
            Destroy(circle.GetComponent<Collider>());

            // Usa URP shader
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Sprites/Default");

            Material mat = new Material(urpShader);
            mat.color = markerColor;
            circle.GetComponent<Renderer>().material = mat;

            // Freccia direzione
            arrowInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrowInstance.transform.SetParent(markerInstance.transform);
            arrowInstance.transform.localPosition = new Vector3(0, 0.02f, markerSize * 0.4f);
            arrowInstance.transform.localScale    = new Vector3(0.05f, 0.05f, markerSize * 0.5f);
            Destroy(arrowInstance.GetComponent<Collider>());

            Material arrowMat = new Material(urpShader);
            arrowMat.color = new Color(1f, 0.8f, 0f, 1f); // giallo
            arrowInstance.GetComponent<Renderer>().material = arrowMat;
        }

        markerInstance.SetActive(false);
    }

    void Update()
    {
        UpdateControllerDevice();
        HandleRayCast();
        HandleInput();
    }

    void UpdateControllerDevice()
    {
        if (!rightHandDevice.isValid)
        {
            List<InputDevice> devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                devices
            );
            if (devices.Count > 0)
                rightHandDevice = devices[0];
        }
    }

    void HandleRayCast()
    {
        Camera cam = Camera.main;
        Ray ray;

        if (cam != null)
        {
            // Ray dal centro dello schermo — origine sempre aggiornata con la camera
            Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0);
            ray = cam.ScreenPointToRay(screenCenter);
            // Sposta l'origine leggermente avanti per evitare z-fighting col pavimento
            ray = new Ray(ray.origin + ray.direction * 0.5f, ray.direction);
        }
        else
        {
            ray = new Ray(transform.position, transform.forward);
        }

        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, rayLength))
        {
            isAiming     = true;
            goalPosition = hit.point;

            // Yaw dalla camera — direzione in cui si guarda
            goalYaw = cam != null ? cam.transform.eulerAngles.y : transform.eulerAngles.y;

            // Mostra il raggio solo se il punto colpito è abbastanza lontano
            float distToHit = Vector3.Distance(ray.origin, hit.point);
            if (distToHit > 1.0f)
            {
                lineRenderer.enabled = true;
                lineRenderer.SetPosition(0, ray.origin);
                lineRenderer.SetPosition(1, hit.point - ray.direction * 0.05f); // stop prima del pavimento
                lineRenderer.startColor = rayColorValid;
                lineRenderer.endColor   = rayColorValid;
            }
            else
            {
                lineRenderer.enabled = false;
            }

            // Marker alzato di 0.1f per evitare z-fighting
            if (markerInstance != null)
            {
                markerInstance.SetActive(true);
                markerInstance.transform.position = hit.point + Vector3.up * 0.1f;
                markerInstance.transform.rotation = Quaternion.Euler(0, goalYaw, 0);

                float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 5f);
                markerInstance.transform.localScale = Vector3.one * pulse;
            }
        }
        else
        {
            isAiming = false;
            if (markerInstance != null) markerInstance.SetActive(false);

            // Raggio rosso
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, ray.origin);
            lineRenderer.SetPosition(1, ray.origin + ray.direction * rayLength);
            lineRenderer.startColor = rayColorInvalid;
            lineRenderer.endColor   = new Color(rayColorInvalid.r, rayColorInvalid.g, rayColorInvalid.b, 0f);
        }
    }

    void HandleInput()
    {
        // Leggi il trigger del controller destro (Meta Quest fisico)
        bool triggerValue = false;
        rightHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerValue);

        // Fallback tastiera — premi T per inviare il goal (utile con XR Device Simulator)
        if (Input.GetKeyDown(KeyCode.T))
            triggerValue = true;

        // Trigger premuto per la prima volta
        if (triggerValue && !triggerPressed)
        {
            triggerPressed = true;

            if (isAiming)
            {
                PublishGoal();
                TriggerHaptics();
                Debug.Log("[VRGoalSetter] Goal inviato con T!");
            }
            else
            {
                Debug.Log("[VRGoalSetter] T premuto ma non sto puntando al pavimento!");
            }
        }

        if (!triggerValue)
            triggerPressed = false;
    }

    void PublishGoal()
    {
        // Converti posizione Unity → ROS (stesso sistema usato in TFPublisher)
        // Unity: Y-up Left-handed → ROS: Z-up Right-handed
        // startPosition è la posizione iniziale del robot — serve per avere coordinate relative all'origine ROS
        Vector3 unityPos = goalPosition - startPosition;
        float rosX = unityPos.z;
        float rosY = -unityPos.x;

        // Converti yaw Unity → ROS
        // In Unity Y+ è su e rotazione oraria è positiva
        // In ROS Z+ è su e rotazione antioraria è positiva
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
        {
            rightHandDevice.SendHapticImpulse(0, hapticsAmplitude, hapticsDuration);
        }
    }

    void OnDisable()
    {
        if (lineRenderer != null)
            lineRenderer.enabled = false;
        if (markerInstance != null)
            markerInstance.SetActive(false);
    }
}