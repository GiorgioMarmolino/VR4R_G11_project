using System.Collections.Generic;
using UnityEngine;

// Questa riga dice a Unity: "Ehi, se mi attacchi a un oggetto, assicurati che abbia anche un LineRenderer!"
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryEditor : MonoBehaviour
{
    [Header("Impostazioni")]
    [Tooltip("Trascina qui il prefab della sferetta")]
    public GameObject waypointPrefab;
    
    // Qui salveremo tutte le sferette che generiamo
    private List<Transform> waypoints = new List<Transform>();
    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();

        // Impostiamo l'estetica della linea (larga 5 cm)
        lineRenderer.startWidth = 0.05f;
        lineRenderer.endWidth = 0.05f;

        // 1. CREIAMO LA TRAIETTORIA FINTA (Poi qui arriveranno i dati da ROS)
        Vector3[] fakePath = new Vector3[] {
            new Vector3(0, 1, 1),
            new Vector3(1, 1, 2),
            new Vector3(0, 1, 3),
            new Vector3(-1, 1, 4)
        };

        // 2. GENERIAMO LE SFERETTE
        foreach (Vector3 pos in fakePath)
        {
            // Clona il prefab nella posizione finta
            GameObject newPoint = Instantiate(waypointPrefab, pos, Quaternion.identity);
            
            // Per tenere la scena pulita, mettiamo le sferette "figlie" di questo manager
            newPoint.transform.SetParent(this.transform);
            
            waypoints.Add(newPoint.transform);
        }

        // Diciamo alla linea quanti punti deve unire
        lineRenderer.positionCount = waypoints.Count;
    }

    void Update()
    {
        // 3. LA MAGIA: Aggiorna la linea ogni singolo frame
        // Se l'utente sposta una sfera in VR (o col mouse), la linea la segue!
        for (int i = 0; i < waypoints.Count; i++)
        {
            lineRenderer.SetPosition(i, waypoints[i].position);
        }
    }
}