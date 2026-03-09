using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

/// <summary>
/// Visualizza i punti del LiDAR come sfere AR nella scena 3D.
/// I punti vengono proiettati nello spazio reale intorno al robot.
/// Va attaccato a un GameObject vuoto nella scena.
/// </summary>
public class LidarARVisualizer : MonoBehaviour
{
    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui scan_link")]
    public Transform scanLink;

    [Header("Impostazioni Punti")]
    public int   maxPoints   = 360;
    public float pointSize   = 0.04f;
    public float pointHeight = 0f;    // altezza fissa dei punti (0 = piano del LiDAR)

    [Header("Colori per distanza")]
    public Color colorNear = new Color(1f, 0.2f, 0.2f, 1f);   // rosso — vicino
    public Color colorMid  = new Color(1f, 0.8f, 0f, 1f);      // giallo — medio
    public Color colorFar  = new Color(0f, 1f, 0.5f, 1f);      // verde — lontano

    [Header("Soglie distanza (m)")]
    public float nearThreshold = 1.0f;
    public float midThreshold  = 3.0f;

    private GameObject[] pointObjects;
    private Renderer[]   pointRenderers;
    private Material     pointMaterial;
    private bool         initialized = false;

    void Start()
    {
        ROSConnection.GetOrCreateInstance()
            .Subscribe<LaserScanMsg>("/scan", OnScanReceived);

        InitializePoints();
        Debug.Log("[LidarARVisualizer] Pronto.");
    }

    void InitializePoints()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        pointMaterial = new Material(shader);

        pointObjects   = new GameObject[maxPoints];
        pointRenderers = new Renderer[maxPoints];

        for (int i = 0; i < maxPoints; i++)
        {
            GameObject pt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pt.name = $"LidarPoint_{i}";
            pt.transform.SetParent(transform);
            pt.transform.localScale = Vector3.one * pointSize;
            Destroy(pt.GetComponent<Collider>());

            Material mat = new Material(pointMaterial);
            pt.GetComponent<Renderer>().material = mat;
            pt.SetActive(false);

            pointObjects[i]   = pt;
            pointRenderers[i] = pt.GetComponent<Renderer>();
        }

        initialized = true;
    }

    void OnScanReceived(LaserScanMsg msg)
    {
        if (!initialized || scanLink == null) return;

        int count = Mathf.Min(msg.ranges.Length, maxPoints);

        for (int i = 0; i < maxPoints; i++)
        {
            if (i >= count)
            {
                pointObjects[i].SetActive(false);
                continue;
            }

            float range = msg.ranges[i];

            // Salta punti non validi
            if (float.IsNaN(range) || float.IsInfinity(range) ||
                range < msg.range_min || range > msg.range_max)
            {
                pointObjects[i].SetActive(false);
                continue;
            }

            // Calcola angolo del punto
            float angle = msg.angle_min + i * msg.angle_increment;

            // Converti da coordinate polar → 3D nello spazio di scan_link
            // In ROS: x=avanti, y=sinistra → Unity: z=avanti, x=-y_ros
            float rosX = range * Mathf.Cos(angle);
            float rosY = range * Mathf.Sin(angle);

            Vector3 localPos = new Vector3(-rosY, 0f, rosX);
            Vector3 worldPos = scanLink.TransformPoint(localPos);
            worldPos.y = scanLink.position.y + pointHeight;

            pointObjects[i].transform.position = worldPos;

            // Colore per distanza
            Color col;
            if (range < nearThreshold)
                col = colorNear;
            else if (range < midThreshold)
                col = Color.Lerp(colorNear, colorMid,
                        (range - nearThreshold) / (midThreshold - nearThreshold));
            else
                col = Color.Lerp(colorMid, colorFar,
                        Mathf.Clamp01((range - midThreshold) / midThreshold));

            pointRenderers[i].material.color = col;
            pointObjects[i].SetActive(true);
        }
    }
}