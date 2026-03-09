using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using System.Collections.Generic;

/// <summary>
/// Visualizza le 3 traiettorie candidate in Unity come LineRenderer 3D.
/// 
/// Riceve i path da:
///   /candidate_path_0 → traiettoria ottimale (verde)
///   /candidate_path_1 → traiettoria sinistra (blu)
///   /candidate_path_2 → traiettoria destra   (arancione)
/// 
/// Converte le coordinate ROS → Unity e disegna le linee sul pavimento.
/// Va attaccato a un GameObject vuoto nella scena.
/// </summary>
public class CandidatePathVisualizer : MonoBehaviour
{
    [Header("Impostazioni Topic")]
    public string path0Topic = "/candidate_path_0";
    public string path1Topic = "/candidate_path_1";
    public string path2Topic = "/candidate_path_2";

    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui base_link — serve per la conversione coordinate ROS → Unity")]
    public Transform robotBaseLink;

    [Header("Colori Traiettorie")]
    public Color colorPath0 = new Color(0f,   1f,   0.3f, 1f); // verde  — ottimale
    public Color colorPath1 = new Color(0.2f, 0.5f, 1f,   1f); // blu    — sinistra
    public Color colorPath2 = new Color(1f,   0.6f, 0f,   1f); // arancione — destra

    [Header("Impostazioni Linea")]
    public float lineWidth       = 0.05f;
    public float lineHeightOffset = 0.05f; // altezza dal pavimento

    [Header("Traiettoria Selezionata")]
    public Color colorSelected    = new Color(1f, 1f, 0f, 1f); // giallo — selezionata
    public float selectedWidth    = 0.1f;
    public int   selectedPathIndex = -1; // -1 = nessuna selezionata

    // LineRenderer per ogni traiettoria
    private LineRenderer[] lineRenderers = new LineRenderer[3];
    private Color[]        pathColors;
    private bool[]         pathReceived  = new bool[3];

    // Posizione iniziale del robot (offset ROS → Unity)
    private Vector3 startPosition;
    private bool    startPositionSet = false;

    void Start()
    {
        // Salva posizione iniziale
        if (robotBaseLink != null)
        {
            startPosition    = robotBaseLink.position;
            startPositionSet = true;
        }
        else
        {
            Debug.LogWarning("[CandidatePathVisualizer] robotBaseLink non assegnato!");
        }

        pathColors = new Color[] { colorPath0, colorPath1, colorPath2 };

        // Crea i 3 LineRenderer
        for (int i = 0; i < 3; i++)
            CreateLineRenderer(i);

        // Sottoscrivi ai topic
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PathMsg>(path0Topic, msg => OnPathReceived(msg, 0));
        ros.Subscribe<PathMsg>(path1Topic, msg => OnPathReceived(msg, 1));
        ros.Subscribe<PathMsg>(path2Topic, msg => OnPathReceived(msg, 2));

        Debug.Log("[CandidatePathVisualizer] In ascolto sui topic candidate path.");
    }

    void CreateLineRenderer(int index)
    {
        GameObject obj = new GameObject($"CandidatePath_{index}");
        obj.transform.SetParent(transform);

        LineRenderer lr = obj.AddComponent<LineRenderer>();

        Shader urpShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpShader == null) urpShader = Shader.Find("Sprites/Default");

        Material mat = new Material(urpShader);
        mat.color = pathColors[index];

        lr.material        = mat;
        lr.startWidth      = lineWidth;
        lr.endWidth        = lineWidth;
        lr.useWorldSpace   = true;
        lr.positionCount   = 0;
        lr.numCapVertices  = 4;
        lr.numCornerVertices = 4;

        // Gradiente colore lungo la linea
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(pathColors[index], 0f),
                new GradientColorKey(pathColors[index], 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.5f, 0f),
                new GradientAlphaKey(1f,   1f)
            }
        );
        lr.colorGradient = gradient;

        lineRenderers[index] = lr;
    }

    void OnPathReceived(PathMsg msg, int index)
    {
        if (!startPositionSet) return;

        pathReceived[index] = true;

        if (msg.poses.Length == 0)
        {
            // Path vuoto — nascondi la linea
            lineRenderers[index].positionCount = 0;
            Debug.LogWarning($"[CandidatePathVisualizer] Path {index} vuoto.");
            return;
        }

        // Converti pose ROS → Unity
        List<Vector3> points = new List<Vector3>();
        foreach (var poseStamped in msg.poses)
        {
            float rosX = (float)poseStamped.pose.position.x;
            float rosY = (float)poseStamped.pose.position.y;

            // ROS: x=avanti, y=sinistra → Unity: z=avanti, x=-y_ros
            // Applica offset startPosition per allineare con la scena Unity
            Vector3 unityPos = new Vector3(
                startPosition.x - rosY,
                startPosition.y + lineHeightOffset,
                startPosition.z + rosX
            );
            points.Add(unityPos);
        }

        // Aggiorna LineRenderer
        lineRenderers[index].positionCount = points.Count;
        lineRenderers[index].SetPositions(points.ToArray());

        Debug.Log($"[CandidatePathVisualizer] Path {index}: {points.Count} punti disegnati.");

        // Aggiorna stile visivo
        UpdatePathStyles();
    }

    void UpdatePathStyles()
    {
        for (int i = 0; i < 3; i++)
        {
            if (lineRenderers[i] == null) continue;

            bool isSelected = (i == selectedPathIndex);

            float width = isSelected ? selectedWidth : lineWidth;
            lineRenderers[i].startWidth = width;
            lineRenderers[i].endWidth   = width;

            Color col = isSelected ? colorSelected : pathColors[i];
            lineRenderers[i].material.color = col;

            // Gradiente con alpha piena se selezionata
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(col, 0f),
                    new GradientColorKey(col, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(isSelected ? 1f : 0.5f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            lineRenderers[i].colorGradient = gradient;
        }
    }

    /// <summary>
    /// Seleziona una traiettoria (0, 1, 2) o deseleziona (-1).
    /// Chiamato da PathSelector.cs
    /// </summary>
    public void SelectPath(int index)
    {
        selectedPathIndex = index;
        UpdatePathStyles();
        Debug.Log($"[CandidatePathVisualizer] Traiettoria {index} selezionata.");
    }

    /// <summary>
    /// Restituisce i punti della traiettoria selezionata in coordinate Unity.
    /// Usato da PathEditor.cs per la modifica.
    /// </summary>
    public Vector3[] GetSelectedPathPoints()
    {
        if (selectedPathIndex < 0 || selectedPathIndex > 2) return null;
        LineRenderer lr = lineRenderers[selectedPathIndex];
        if (lr == null || lr.positionCount == 0) return null;

        Vector3[] points = new Vector3[lr.positionCount];
        lr.GetPositions(points);
        return points;
    }

    /// <summary>
    /// Nasconde tutte le traiettorie — chiamato dopo la conferma.
    /// </summary>
    public void HideAllPaths()
    {
        foreach (var lr in lineRenderers)
            if (lr != null) lr.positionCount = 0;
        selectedPathIndex = -1;
        Debug.Log("[CandidatePathVisualizer] Traiettorie nascoste.");
    }

    void OnDisable()
    {
        HideAllPaths();
    }
}