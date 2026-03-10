using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System.Collections.Generic;

/// <summary>
/// PathExecutor — invia il path confermato a Nav2 per l'esecuzione.
///
/// Flusso:
///   1. PathEditor.OnPathConfirmed → riceve i waypoint Unity
///   2. Converte Unity → ROS e costruisce un nav_msgs/Path
///   3. Pubblica su /plan (per visualizzazione in RViz)
///   4. Invia NavigateToPose con l'ultimo waypoint come goal finale
///
/// Attacca a: stesso GameObject di CandidatePathVisualizer
/// </summary>
public class PathExecutor : MonoBehaviour
{
    [Header("Riferimenti")]
    public PathEditor              pathEditor;
    public CandidatePathVisualizer pathVisualizer;

    [Tooltip("base_link — serve per la conversione coordinate Unity → ROS")]
    public Transform robotBaseLink;

    [Header("Topic ROS")]
    public string editedPathTopic = "/edited_path";  // letto da path_follower_node.py
    public string planTopic       = "/plan";          // visualizzazione RViz

    [Header("Impostazioni")]
    [Tooltip("Densità punti interpolati tra i waypoint (punti per metro)")]
    public int pointsPerMeter = 5;

    // Publisher
    private ROSConnection ros;

    // Stato
    public bool IsExecuting { get; private set; } = false;

    // Evento
    public System.Action OnExecutionStarted;
    public System.Action OnExecutionComplete;

    // Posizione iniziale robot (offset ROS → Unity)
    private Vector3 startPosition = Vector3.zero;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PathMsg>(editedPathTopic);
        ros.RegisterPublisher<PathMsg>(planTopic);

        if (robotBaseLink != null)
            startPosition = robotBaseLink.position;

        // Sottoscrivi alla conferma del path editato
        if (pathEditor != null)
            pathEditor.OnPathConfirmed += OnPathConfirmed;
        else
            Debug.LogWarning("[PathExecutor] PathEditor non assegnato!");

        Debug.Log("[PathExecutor] Pronto.");
    }

    void OnPathConfirmed(Vector3[] waypointPositions)
    {
        if (waypointPositions == null || waypointPositions.Length == 0)
        {
            Debug.LogWarning("[PathExecutor] Waypoint vuoti!");
            return;
        }

        Debug.Log($"[PathExecutor] Path confermato con {waypointPositions.Length} waypoint. Esecuzione...");
        ExecutePath(waypointPositions);
    }

    public void ExecutePath(Vector3[] waypointPositions)
    {
        IsExecuting = true;
        OnExecutionStarted?.Invoke();

        // 1. Costruisce il path interpolato
        List<Vector3> pathPoints = InterpolateWaypoints(waypointPositions);

        // 2. Converte in ROS
        PathMsg rosPath = BuildRosPath(pathPoints);

        // 3. Pubblica su /plan per visualizzazione RViz
        ros.Publish(planTopic, rosPath);

        // 4. Pubblica su /edited_path — letto da path_follower_node.py che chiama FollowPath
        ros.Publish(editedPathTopic, rosPath);

        Debug.Log($"[PathExecutor] Path pubblicato: {pathPoints.Count} punti → /edited_path + /plan");

        IsExecuting = false;
        OnExecutionComplete?.Invoke();
    }

    List<Vector3> InterpolateWaypoints(Vector3[] waypoints)
    {
        List<Vector3> points = new List<Vector3>();

        // Catmull-Rom spline — stessa usata nel PathEditor per visualizzazione
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            Vector3 p0 = waypoints[Mathf.Max(i - 1, 0)];
            Vector3 p1 = waypoints[i];
            Vector3 p2 = waypoints[i + 1];
            Vector3 p3 = waypoints[Mathf.Min(i + 2, waypoints.Length - 1)];

            float segmentLength = Vector3.Distance(p1, p2);
            int   steps         = Mathf.Max(4, Mathf.RoundToInt(segmentLength * pointsPerMeter));

            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                points.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        points.Add(waypoints[waypoints.Length - 1]);
        return points;
    }

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    PathMsg BuildRosPath(List<Vector3> unityPoints)
    {
        PathMsg path = new PathMsg();
        path.header             = new HeaderMsg();
        path.header.frame_id    = "map";
        path.header.stamp       = new RosMessageTypes.BuiltinInterfaces.TimeMsg();

        path.poses = new PoseStampedMsg[unityPoints.Count];

        for (int i = 0; i < unityPoints.Count; i++)
        {
            Vector3 up = unityPoints[i];

            // Unity → ROS: rosX = up.z - start.z, rosY = -(up.x - start.x)
            float rosX = up.z - startPosition.z;
            float rosY = -(up.x - startPosition.x);

            // Calcola yaw dal punto successivo
            float yaw = 0f;
            if (i < unityPoints.Count - 1)
            {
                Vector3 next = unityPoints[i + 1];
                float   dx   = (next.z - startPosition.z) - rosX;
                float   dy   = -(next.x - startPosition.x) - rosY;
                yaw          = Mathf.Atan2(dy, dx);
            }
            else if (i > 0)
            {
                // Ultimo punto — stesso yaw del precedente
                Vector3 prev = unityPoints[i - 1];
                float   dx   = rosX - (prev.z - startPosition.z);
                float   dy   = rosY - (-(prev.x - startPosition.x));
                yaw          = Mathf.Atan2(dy, dx);
            }

            PoseStampedMsg pose = new PoseStampedMsg();
            pose.header           = path.header;
            pose.pose.position.x  = rosX;
            pose.pose.position.y  = rosY;
            pose.pose.position.z  = 0.0;
            pose.pose.orientation.x = 0.0;
            pose.pose.orientation.y = 0.0;
            pose.pose.orientation.z = Mathf.Sin(yaw / 2f);
            pose.pose.orientation.w = Mathf.Cos(yaw / 2f);

            path.poses[i] = pose;
        }

        return path;
    }

}