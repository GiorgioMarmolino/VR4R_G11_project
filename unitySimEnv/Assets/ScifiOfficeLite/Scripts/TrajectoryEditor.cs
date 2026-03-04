using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;

[RequireComponent(typeof(LineRenderer))]
public class TrajectoryEditor : MonoBehaviour
{
    [Header("Impostazioni")]
    [Tooltip("Trascina qui il prefab della sferetta")]
    public GameObject waypointPrefab;

    [Tooltip("Topic ROS2 da cui ricevere il path")]
    public string pathTopic = "/path";

    // Qui salveremo tutte le sferette che generiamo
    private List<Transform> waypoints = new List<Transform>();
    private LineRenderer lineRenderer;
    private ROSConnection ros;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();

        // Impostiamo l'estetica della linea (larga 5 cm)
        lineRenderer.startWidth = 0.05f;
        lineRenderer.endWidth = 0.05f;

        // Connessione a ROS2 e sottoscrizione al topic /path
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PathMsg>(pathTopic, OnPathReceived);
    }

    // Chiamata ogni volta che arriva un messaggio sul topic /path
    void OnPathReceived(PathMsg pathMsg)
    {
        // Puliamo le sferette vecchie
        foreach (Transform wp in waypoints)
        {
            Destroy(wp.gameObject);
        }
        waypoints.Clear();

        // Convertiamo ogni posa ROS in un punto Unity
        foreach (PoseStampedMsg pose in pathMsg.poses)
        {
            // ROS usa Z-forward, Unity usa Z-forward ma con Y e Z invertiti
            // Conversione: ROS(x, y, z) -> Unity(x, z, y)
            Vector3 pos = new Vector3(
                (float)pose.pose.position.x,
                (float)pose.pose.position.z,
                (float)pose.pose.position.y
            );

            if (waypointPrefab != null)
            {
                GameObject newPoint = Instantiate(waypointPrefab, pos, Quaternion.identity);
                newPoint.transform.SetParent(this.transform);
                waypoints.Add(newPoint.transform);
            }
            else
            {
                // Se non c'è il prefab, creiamo un oggetto vuoto come placeholder
                GameObject placeholder = new GameObject("Waypoint");
                placeholder.transform.position = pos;
                placeholder.transform.SetParent(this.transform);
                waypoints.Add(placeholder.transform);
            }
        }

        // Aggiorniamo subito la linea
        UpdateLine();
    }

    void UpdateLine()
    {
        lineRenderer.positionCount = waypoints.Count;
        for (int i = 0; i < waypoints.Count; i++)
        {
            lineRenderer.SetPosition(i, waypoints[i].position);
        }
    }

    void Update()
    {
        // Aggiorna la linea ogni frame
        // (utile se le sferette vengono spostate manualmente in VR)
        for (int i = 0; i < waypoints.Count; i++)
        {
            lineRenderer.SetPosition(i, waypoints[i].position);
        }
    }
}