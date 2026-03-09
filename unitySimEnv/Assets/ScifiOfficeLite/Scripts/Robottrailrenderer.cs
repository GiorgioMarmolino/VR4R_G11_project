using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using System.Collections.Generic;

/// <summary>
/// Traccia il percorso del robot nella scena 3D.
/// Disegna una linea continua che mostra dove è stato il robot.
/// Va attaccato a un GameObject vuoto nella scena.
/// </summary>
public class RobotTrailRenderer : MonoBehaviour
{
    [Header("Riferimento Robot")]
    [Tooltip("Trascina qui base_link")]
    public Transform robotBaseLink;

    [Header("Impostazioni Traccia")]
    public int   maxTrailPoints  = 500;
    public float minPointDist    = 0.1f;  // distanza minima tra punti (m)
    public float trailWidth      = 0.03f;
    public float trailHeight     = 0.05f; // altezza dal pavimento

    [Header("Colori")]
    public Color trailColorStart = new Color(0f, 1f, 0.5f, 0.8f);
    public Color trailColorEnd   = new Color(0f, 0.5f, 1f, 0.3f);

    [Header("Controlli")]
    public KeyCode clearKey = KeyCode.C; // tasto per cancellare la traccia

    private LineRenderer lineRenderer;
    private List<Vector3> trailPoints = new List<Vector3>();
    private Vector3 lastPoint = Vector3.zero;
    private bool initialized = false;

    void Start()
    {
        // Setup LineRenderer
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null) urpShader = Shader.Find("Sprites/Default");

        lineRenderer.material        = new Material(urpShader);
        lineRenderer.startWidth      = trailWidth;
        lineRenderer.endWidth        = trailWidth * 0.3f;
        lineRenderer.useWorldSpace   = true;
        lineRenderer.positionCount   = 0;
        lineRenderer.numCapVertices  = 4;

        if (robotBaseLink != null)
        {
            lastPoint   = robotBaseLink.position;
            initialized = true;
        }

        Debug.Log("[RobotTrailRenderer] Traccia percorso attiva. Premi C per cancellare.");
    }

    void Update()
    {
        if (!initialized || robotBaseLink == null) return;

        // Cancella traccia con tasto C
        if (Input.GetKeyDown(clearKey))
            ClearTrail();

        Vector3 currentPos = robotBaseLink.position;
        currentPos.y = trailHeight;

        // Aggiungi punto solo se il robot si è mosso abbastanza
        if (Vector3.Distance(currentPos, lastPoint) >= minPointDist)
        {
            trailPoints.Add(currentPos);
            lastPoint = currentPos;

            // Limita numero di punti
            if (trailPoints.Count > maxTrailPoints)
                trailPoints.RemoveAt(0);

            UpdateLineRenderer();
        }
    }

    void UpdateLineRenderer()
    {
        lineRenderer.positionCount = trailPoints.Count;
        lineRenderer.SetPositions(trailPoints.ToArray());

        // Gradiente colore lungo la traccia
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(trailColorEnd,   0f),
                new GradientColorKey(trailColorStart, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.2f, 0f),
                new GradientAlphaKey(0.9f, 1f)
            }
        );
        lineRenderer.colorGradient = gradient;
    }

    public void ClearTrail()
    {
        trailPoints.Clear();
        lineRenderer.positionCount = 0;
        Debug.Log("[RobotTrailRenderer] Traccia cancellata.");
    }
}