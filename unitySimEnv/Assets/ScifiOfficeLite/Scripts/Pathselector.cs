using UnityEngine;
using UnityEngine.XR;
using Unity.Robotics.ROSTCPConnector;
using System.Collections.Generic;

/// <summary>
/// PathSelector — seleziona una delle 3 traiettorie candidate con ray casting.
///
/// Funzionamento:
///   - Ray dal controller destro (o dalla camera in modalità PC)
///   - Hover sul path più vicino al ray → diventa giallo pulsante
///   - Trigger / Click sinistro → seleziona il path
///   - Chiama CandidatePathVisualizer.SelectPath(i)
///   - Notifica PathEditor dello stato
///
/// Attacca a: stesso GameObject di CandidatePathVisualizer
/// </summary>
public class PathSelector : MonoBehaviour
{
    [Header("Riferimenti")]
    public CandidatePathVisualizer pathVisualizer;

    [Tooltip("Transform del controller destro (Right Controller in XR Origin)")]
    public Transform rightController;

    [Tooltip("Camera principale — usata come fallback PC")]
    public Camera mainCamera;

    [Header("Impostazioni Ray")]
    public float rayMaxDistance   = 20f;
    public float pathHoverRadius  = 0.3f; // distanza max dal path per hover

    [Header("Feedback Visivo")]
    public Color hoverColor    = new Color(1f, 1f, 0f, 1f);   // giallo hover
    public Color selectedColor = new Color(0f, 1f, 1f, 1f);   // ciano selezionato

    [Header("Stato")]
    public bool isActive = true; // disattivato durante editing/esecuzione

    // Stato interno
    private int  hoveredPath  = -1;
    private int  selectedPath = -1;

    // Colori originali per restore hover
    private Color[] originalColors;

    // VR input
    private List<InputDevice> rightControllers = new List<InputDevice>();
    private bool prevTrigger = false;

    // Evento: path selezionato
    public System.Action<int> OnPathSelected;

    // LineRenderer del ray visivo
    private LineRenderer rayLine;

    void Start()
    {
        if (pathVisualizer == null)
            pathVisualizer = GetComponent<CandidatePathVisualizer>();

        if (mainCamera == null)
            mainCamera = Camera.main;

        originalColors = new Color[]
        {
            pathVisualizer.colorPath0,
            pathVisualizer.colorPath1,
            pathVisualizer.colorPath2
        };

        // Ray visivo
        rayLine = gameObject.AddComponent<LineRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        rayLine.material       = new Material(shader);
        rayLine.material.color = new Color(1f, 1f, 1f, 0.4f);
        rayLine.startWidth     = 0.005f;
        rayLine.endWidth       = 0.002f;
        rayLine.positionCount  = 2;
        rayLine.useWorldSpace  = true;
        rayLine.enabled        = false;

        Debug.Log("[PathSelector] Pronto. Punta il controller verso una traiettoria.");
    }

    void Update()
    {
        if (!isActive) { rayLine.enabled = false; return; }

        HandleRayCast();
        HandleInput();
    }

    void HandleRayCast()
    {
        // Origine e direzione del ray
        Vector3 rayOrigin;
        Vector3 rayDir;

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            rightControllers);

        bool hasController = rightControllers.Count > 0;

        if (hasController && rightController != null)
        {
            rayOrigin = rightController.position;
            rayDir    = rightController.forward;
        }
        else
        {
            // Fallback PC — ray dalla camera verso il mouse
            Ray mouseRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            rayOrigin    = mouseRay.origin;
            rayDir       = mouseRay.direction;
        }

        // Aggiorna ray visivo
        rayLine.enabled = hasController;
        if (hasController)
        {
            rayLine.SetPosition(0, rayOrigin);
            rayLine.SetPosition(1, rayOrigin + rayDir * rayMaxDistance);
        }

        // Trova il path più vicino al ray
        int   closestPath = -1;
        float closestDist = pathHoverRadius;

        for (int i = 0; i < 3; i++)
        {
            float dist = GetMinDistanceToPath(i, rayOrigin, rayDir);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestPath = i;
            }
        }

        // Aggiorna hover
        if (closestPath != hoveredPath)
        {
            // Ripristina colore path precedente
            if (hoveredPath >= 0 && hoveredPath != selectedPath)
                pathVisualizer.SetPathColor(hoveredPath, originalColors[hoveredPath]);

            hoveredPath = closestPath;

            // Applica colore hover
            if (hoveredPath >= 0 && hoveredPath != selectedPath)
                pathVisualizer.SetPathColor(hoveredPath, hoverColor);
        }

        Debug.Log($"[PathSelector] hoveredPath={hoveredPath}, closestDist={closestDist:F3}"); 
        Debug.Log($"[PathSelector] rayOrigin={rayOrigin}, rayDir={rayDir}");
        for (int i = 0; i < 3; i++)
        {
            Vector3[] pts = pathVisualizer.GetPathPoints(i);
            float dist = GetMinDistanceToPath(i, rayOrigin, rayDir);
            Debug.Log($"[PathSelector] Path {i}: punti={pts?.Length ?? 0}, dist={dist:F3}");
        }
    }

    float GetMinDistanceToPath(int pathIndex, Vector3 rayOrigin, Vector3 rayDir)
    {
        Vector3[] points = pathVisualizer.GetPathPoints(pathIndex);
        if (points == null || points.Length == 0) return float.MaxValue;

        float minDist = float.MaxValue;

        // Controlla ogni segmento del path
        for (int i = 0; i < points.Length - 1; i++)
        {
            float d = DistanceRayToSegment(rayOrigin, rayDir, points[i], points[i + 1]);
            if (d < minDist) minDist = d;
        }

        return minDist;
    }

    float DistanceRayToSegment(Vector3 rayOrigin, Vector3 rayDir,
                                Vector3 segA, Vector3 segB)
    {
        // Distanza minima tra un ray infinito e un segmento
        Vector3 segDir    = segB - segA;
        Vector3 originDiff = segA - rayOrigin;

        float a = Vector3.Dot(rayDir, rayDir);
        float b = Vector3.Dot(rayDir, segDir);
        float c = Vector3.Dot(segDir, segDir);
        float d = Vector3.Dot(rayDir, originDiff);
        float e = Vector3.Dot(segDir, originDiff);

        float denom = a * c - b * b;
        float t, s;

        if (Mathf.Abs(denom) < 1e-6f)
        {
            t = 0f;
            s = Mathf.Clamp01(-e / c);
        }
        else
        {
            t = (b * e - c * d) / denom;
            s = Mathf.Clamp01((a * e - b * d) / denom);
        }

        t = Mathf.Max(0f, t);

        Vector3 closestOnRay = rayOrigin + rayDir * t;
        Vector3 closestOnSeg = segA + segDir * s;

        return Vector3.Distance(closestOnRay, closestOnSeg);
    }

    void HandleInput()
    {
        bool triggerPressed = false;

        // VR trigger
        foreach (var dev in rightControllers)
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool val))
                triggerPressed = val;

        // PC fallback
        if (Input.GetMouseButtonDown(0))
            triggerPressed = true;

        bool triggerDown = triggerPressed && !prevTrigger;
        prevTrigger      = triggerPressed;

        if (triggerDown && hoveredPath >= 0)
            SelectPath(hoveredPath);
    }

    void SelectPath(int index)
    {
        // Deseleziona il precedente
        if (selectedPath >= 0)
            pathVisualizer.SetPathColor(selectedPath, originalColors[selectedPath]);

        selectedPath = index;
        hoveredPath  = -1;

        pathVisualizer.SelectPath(selectedPath);
        pathVisualizer.SetPathColor(selectedPath, selectedColor);

        Debug.Log($"[PathSelector] Path {selectedPath} selezionato.");
        OnPathSelected?.Invoke(selectedPath);
    }

    public void Deactivate()
    {
        isActive       = false;
        rayLine.enabled = false;
    }

    public void Activate()
    {
        isActive    = true;
        selectedPath = -1;
        hoveredPath  = -1;
    }
}