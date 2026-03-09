using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;
using System.Collections.Generic;

/// <summary>
/// Visualizza le 3 traiettorie candidate in Unity come LineRenderer 3D.
/// - Le traiettorie vengono cancellate quando il robot raggiunge il goal
/// - Quando arriva un nuovo goal, le vecchie vengono sostituite
/// </summary>
public class CandidatePathVisualizer : MonoBehaviour
{
    [Header("Impostazioni Topic")]
    public string path0Topic = "/candidate_path_0";
    public string path1Topic = "/candidate_path_1";
    public string path2Topic = "/candidate_path_2";

    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui base_link")]
    public Transform robotBaseLink;

    [Header("Colori Traiettorie")]
    public Color colorPath0 = new Color(0f,   1f,   0.3f, 1f); // verde
    public Color colorPath1 = new Color(0.2f, 0.5f, 1f,   1f); // blu
    public Color colorPath2 = new Color(1f,   0.6f, 0f,   1f); // arancione

    [Header("Impostazioni Linea")]
    public float lineWidth        = 0.05f;
    public float lineHeightOffset = 0.05f;

    [Header("Traiettoria Selezionata")]
    public Color colorSelected   = new Color(1f, 1f, 0f, 1f);
    public float selectedWidth   = 0.1f;
    public int   selectedPathIndex = -1;

    [Header("Rilevamento Goal Raggiunto")]
    public float goalReachedThreshold = 0.4f;

    // LineRenderer
    private LineRenderer[] lineRenderers = new LineRenderer[3];
    private Color[]        pathColors;

    // Offset coordinate
    private Vector3 startPosition    = Vector3.zero;
    private bool    startPositionSet = false;

    // Stato goal
    private Vector2 robotPos    = Vector2.zero;
    private Vector2 currentGoal = Vector2.zero;
    private bool    hasGoal     = false;
    private int     pathsReceived = 0;

    void Start()
    {
        if (robotBaseLink != null)
        {
            startPosition    = robotBaseLink.position;
            startPositionSet = true;
        }
        else
        {
            startPosition    = Vector3.zero;
            startPositionSet = true;
            Debug.LogWarning("[CandidatePathVisualizer] robotBaseLink non assegnato — uso Vector3.zero.");
        }

        pathColors = new Color[] { colorPath0, colorPath1, colorPath2 };

        for (int i = 0; i < 3; i++)
            CreateLineRenderer(i);

        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PathMsg>(path0Topic, msg => OnPathReceived(msg, 0));
        ros.Subscribe<PathMsg>(path1Topic, msg => OnPathReceived(msg, 1));
        ros.Subscribe<PathMsg>(path2Topic, msg => OnPathReceived(msg, 2));
        ros.Subscribe<OdometryMsg>("/odom", OnOdomReceived);
        ros.Subscribe<PoseStampedMsg>("/goal_pose", OnGoalReceived);

        Debug.Log("[CandidatePathVisualizer] Pronto.");
    }

    void CreateLineRenderer(int index)
    {
        GameObject obj = new GameObject($"CandidatePath_{index}");
        obj.transform.SetParent(transform);

        LineRenderer lr = obj.AddComponent<LineRenderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        lr.material          = new Material(shader);
        lr.material.color    = pathColors[index];
        lr.startWidth        = lineWidth;
        lr.endWidth          = lineWidth;
        lr.useWorldSpace     = true;
        lr.positionCount     = 0;
        lr.numCapVertices    = 4;
        lr.numCornerVertices = 4;

        lineRenderers[index] = lr;
    }

    void OnGoalReceived(PoseStampedMsg msg)
    {
        currentGoal = new Vector2(
            (float)msg.pose.position.x,
            (float)msg.pose.position.y
        );
        hasGoal       = true;
        pathsReceived = 0;
        selectedPathIndex = -1;

        // Non nascondere subito — le nuove traiettorie sovrascriveranno quelle vecchie
        Debug.Log($"[CandidatePathVisualizer] Nuovo goal: x={currentGoal.x:F2}, y={currentGoal.y:F2}");
    }

    void OnOdomReceived(OdometryMsg msg)
    {
        robotPos = new Vector2(
            (float)msg.pose.pose.position.x,
            (float)msg.pose.pose.position.y
        );

        // Controlla se il robot ha raggiunto il goal
        if (hasGoal && Vector2.Distance(robotPos, currentGoal) < goalReachedThreshold)
        {
            hasGoal = false;
            HideAllPaths();
            Debug.Log("[CandidatePathVisualizer] Goal raggiunto — traiettorie cancellate.");
        }
    }

    void OnPathReceived(PathMsg msg, int index)
    {
        Debug.Log($"[CandidatePathVisualizer] Path {index}: {msg.poses.Length} pose ricevute.");

        if (msg.poses.Length == 0)
        {
            lineRenderers[index].positionCount = 0;
            return;
        }

        // Converti ROS → Unity
        List<Vector3> points = new List<Vector3>();
        foreach (var poseStamped in msg.poses)
        {
            float rosX = (float)poseStamped.pose.position.x;
            float rosY = (float)poseStamped.pose.position.y;

            Vector3 unityPos = new Vector3(
                startPosition.x - rosY,
                startPosition.y + lineHeightOffset,
                startPosition.z + rosX
            );
            points.Add(unityPos);
        }

        lineRenderers[index].positionCount = points.Count;
        lineRenderers[index].SetPositions(points.ToArray());

        pathsReceived++;
        UpdatePathStyles();
    }

    void UpdatePathStyles()
    {
        for (int i = 0; i < 3; i++)
        {
            if (lineRenderers[i] == null) continue;

            bool isSelected = (i == selectedPathIndex);
            lineRenderers[i].startWidth      = isSelected ? selectedWidth : lineWidth;
            lineRenderers[i].endWidth        = isSelected ? selectedWidth : lineWidth;
            lineRenderers[i].material.color  = isSelected ? colorSelected : pathColors[i];
        }
    }

    public void SelectPath(int index)
    {
        selectedPathIndex = index;
        UpdatePathStyles();
        Debug.Log($"[CandidatePathVisualizer] Traiettoria {index} selezionata.");
    }

    public Vector3[] GetSelectedPathPoints()
    {
        if (selectedPathIndex < 0 || selectedPathIndex > 2) return null;
        LineRenderer lr = lineRenderers[selectedPathIndex];
        if (lr == null || lr.positionCount == 0) return null;

        Vector3[] points = new Vector3[lr.positionCount];
        lr.GetPositions(points);
        return points;
    }

    public void HideAllPaths()
    {
        foreach (var lr in lineRenderers)
            if (lr != null) lr.positionCount = 0;
        selectedPathIndex = -1;
    }

    void OnDisable()
    {
        HideAllPaths();
    }
}