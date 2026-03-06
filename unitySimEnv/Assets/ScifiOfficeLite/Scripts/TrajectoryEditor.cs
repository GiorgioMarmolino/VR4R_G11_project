using System.Collections.Generic;
using UnityEngine;
// Le librerie di ROS che ora funzioneranno!
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Nav; // Il tipo di messaggio (Path)
 
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryEditor : MonoBehaviour
{
    [Header("Impostazioni Base")]
    public GameObject waypointPrefab;
    [Header("Impostazioni ROS")]
    public string subscribeTopic = "/plan"; // Il topic del tuo collega!
 
    private List<Transform> waypoints = new List<Transform>();
    private LineRenderer lineRenderer;
 
    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = 0.05f;
        lineRenderer.endWidth = 0.05f;
 
        // Diciamo a ROSConnection di mettersi in ascolto sul topic "/plan"
        ROSConnection.GetOrCreateInstance().Subscribe<PathMsg>(subscribeTopic, OnTrajectoryReceived);
        Debug.Log("In ascolto sul topic: " + subscribeTopic);
    }
 
    // QUESTA FUNZIONE SCATTA SOLO QUANDO ARRIVA LA TRAIETTORIA DA ROS
    void OnTrajectoryReceived(PathMsg incomingMessage)
    {
        Debug.Log("BOOM! Traiettoria ricevuta! Punti totali: " + incomingMessage.poses.Length);
 
        // 1. PULIZIA: Cancelliamo le vecchie sferette se ce n'erano
        foreach (Transform vecchiasfera in waypoints)
        {
            Destroy(vecchiasfera.gameObject);
        }
        waypoints.Clear();
 
        // 2. CREAZIONE: Leggiamo i nuovi punti veri
        foreach (var poseStamped in incomingMessage.poses)
        {
            // MAGIA: .From<FLU>() converte le coordinate di ROS (Forward-Left-Up) in quelle di Unity!
            Vector3 posizioneUnity = poseStamped.pose.position.From<FLU>();
 
            // Creiamo la sferetta nella posizione corretta
            GameObject newPoint = Instantiate(waypointPrefab, posizioneUnity, Quaternion.identity);
            // Mettiamo le sferette "dentro" il TrajectoryManager per tenere pulita la Hierarchy
            newPoint.transform.SetParent(this.transform); 
            waypoints.Add(newPoint.transform);
        }
 
        // Aggiorniamo il numero di punti per la linea
        lineRenderer.positionCount = waypoints.Count;
    }
 
    void Update()
    {
        // Teniamo la linea incollata alle sferette ogni frame (così se le sposti in VR la linea le segue)
        for (int i = 0; i < waypoints.Count; i++)
        {
            lineRenderer.SetPosition(i, waypoints[i].position);
        }
    }
}