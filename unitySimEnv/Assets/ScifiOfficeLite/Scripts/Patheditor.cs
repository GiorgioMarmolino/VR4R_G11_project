using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// PathEditor — modifica la traiettoria selezionata trascinando waypoint.
///
/// Funzionamento:
///   - Quando attivato, mostra N sfere equidistanti lungo il path selezionato
///   - Con il controller/mouse si afferra una sfera e la si trascina sul pavimento
///   - Il LineRenderer si aggiorna in tempo reale con una spline tra i waypoint
///   - Conferma con trigger lungo / tasto Enter → notifica PathExecutor
///
/// Attacca a: stesso GameObject di CandidatePathVisualizer
/// </summary>
public class PathEditor : MonoBehaviour
{
    [Header("Riferimenti")]
    public CandidatePathVisualizer pathVisualizer;
    public PathSelector            pathSelector;

    [Tooltip("Transform del controller destro")]
    public Transform rightController;
    public Camera    mainCamera;

    [Header("Impostazioni Waypoint")]
    [Tooltip("Numero di sfere waypoint mostrate per la modifica")]
    public int   waypointCount  = 10;
    public float waypointRadius = 0.15f;
    public Color waypointColor         = new Color(0f,   1f,   1f,  1f);
    public Color waypointHoverColor    = new Color(1f,   1f,   0f,  1f);
    public Color waypointGrabbedColor  = new Color(1f,   0.4f, 0f,  1f);

    [Header("Impostazioni Drag")]
    [Tooltip("Distanza massima dal controller per afferrare un waypoint")]
    public float grabRadius     = 0.4f;
    public float floorY         = 0f;    // Y del pavimento
    public float waypointHeight = 0.05f; // altezza waypoint dal pavimento

    [Header("Impostazioni Linea Editata")]
    public Color  editedPathColor = new Color(1f, 0.5f, 0f, 1f); // arancione
    public float  editedPathWidth = 0.08f;

    [Header("Controlli")]
    public KeyCode confirmKey = KeyCode.Return;
    public KeyCode cancelKey  = KeyCode.Escape;

    // Stato
    public bool IsActive { get; private set; } = false;

    // Waypoint
    private GameObject[]  waypointObjects;
    private int           hoveredWaypoint = -1;
    private int           grabbedWaypoint = -1;

    // Path editato
    private LineRenderer  editedLineRenderer;
    private Vector3[]     waypointPositions;

    // VR input
    private List<InputDevice> rightControllers = new List<InputDevice>();
    private bool prevTrigger    = false;
    private bool prevGrip       = false;
    private float triggerHoldTime = 0f;

    // Evento: path confermato
    public System.Action<Vector3[]> OnPathConfirmed;
    public System.Action            OnEditCancelled;

    void Start()
    {
        if (pathVisualizer == null) pathVisualizer = GetComponent<CandidatePathVisualizer>();
        if (pathSelector   == null) pathSelector   = GetComponent<PathSelector>();
        if (mainCamera     == null) mainCamera      = Camera.main;

        // Sottoscrivi alla selezione del path
        if (pathSelector != null)
            pathSelector.OnPathSelected += OnPathSelected;

        // LineRenderer per il path editato
        GameObject lineObj = new GameObject("EditedPath");
        lineObj.transform.SetParent(transform);
        editedLineRenderer = lineObj.AddComponent<LineRenderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        editedLineRenderer.material            = new Material(shader);
        editedLineRenderer.material.color      = editedPathColor;
        editedLineRenderer.startWidth          = editedPathWidth;
        editedLineRenderer.endWidth            = editedPathWidth;
        editedLineRenderer.useWorldSpace       = true;
        editedLineRenderer.positionCount       = 0;
        editedLineRenderer.numCapVertices      = 4;
        editedLineRenderer.numCornerVertices   = 4;
        editedLineRenderer.enabled             = false;

        Debug.Log("[PathEditor] Pronto. Seleziona un path per modificarlo.");
    }

    void OnPathSelected(int index)
    {
        // Path selezionato — aspetta che l'utente decida se modificare
        // L'attivazione avviene tramite Activate() chiamato da SelectionUIHUD
        Debug.Log($"[PathEditor] Path {index} selezionato, pronto per modifica.");
        Activate();
    }

    /// <summary>
    /// Attiva l'editor sul path selezionato corrente.
    /// Chiamato da SelectionUIHUD quando l'utente sceglie "Modifica".
    /// </summary>
    public void Activate()
    {
        Debug.Log("[PathSelector] Activate() chiamato!");
        int selectedIndex = pathVisualizer.selectedPathIndex;
        if (selectedIndex < 0)
        {
            Debug.LogWarning("[PathEditor] Nessun path selezionato!");
            return;
        }

        Vector3[] pathPoints = pathVisualizer.GetSelectedPathPoints();
        if (pathPoints == null || pathPoints.Length == 0)
        {
            Debug.LogWarning("[PathEditor] Path selezionato vuoto!");
            return;
        }

        IsActive = true;
        if (pathSelector != null) pathSelector.Deactivate();

        // Crea i waypoint equidistanti lungo il path
        CreateWaypoints(pathPoints);

        // Mostra il path editato
        editedLineRenderer.enabled = true;
        UpdateEditedPath();

        Debug.Log($"[PathEditor] Editor attivo su path {selectedIndex} con {waypointCount} waypoint.");
    }

    void CreateWaypoints(Vector3[] pathPoints)
    {
        // Distruggi waypoint precedenti
        DestroyWaypoints();

        waypointObjects   = new GameObject[waypointCount];
        waypointPositions = new Vector3[waypointCount];

        // Calcola lunghezza totale del path
        float totalLength = 0f;
        for (int i = 0; i < pathPoints.Length - 1; i++)
            totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);

        float segmentLength = totalLength / (waypointCount - 1);

        // Posiziona waypoint equidistanti
        int   ptIdx       = 0;
        float accumulated = 0f;

        for (int w = 0; w < waypointCount; w++)
        {
            float targetDist = w * segmentLength;

            // Avanza lungo il path fino alla distanza target
            while (ptIdx < pathPoints.Length - 2)
            {
                float segLen = Vector3.Distance(pathPoints[ptIdx], pathPoints[ptIdx + 1]);
                if (accumulated + segLen >= targetDist) break;
                accumulated += segLen;
                ptIdx++;
            }

            // Interpolazione sul segmento corrente
            Vector3 pos;
            if (ptIdx >= pathPoints.Length - 1)
            {
                pos = pathPoints[pathPoints.Length - 1];
            }
            else
            {
                float segLen = Vector3.Distance(pathPoints[ptIdx], pathPoints[ptIdx + 1]);
                float t      = segLen > 0 ? (targetDist - accumulated) / segLen : 0f;
                pos          = Vector3.Lerp(pathPoints[ptIdx], pathPoints[ptIdx + 1], t);
            }

            pos.y = floorY + waypointHeight;
            waypointPositions[w] = pos;

            // Crea sfera
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name       = $"Waypoint_{w}";
            sphere.transform.SetParent(transform);
            sphere.transform.position   = pos;
            sphere.transform.localScale = Vector3.one * waypointRadius * 2f;
            Destroy(sphere.GetComponent<Collider>());

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            sphere.GetComponent<Renderer>().material = new Material(shader);
            sphere.GetComponent<Renderer>().material.color = waypointColor;

            waypointObjects[w] = sphere;
        }
    }

    void Update()
    {
        if (!IsActive) return;

        HandleInput();
        UpdateHover();

        // Conferma con tasto
        if (Input.GetKeyDown(confirmKey)) ConfirmPath();
        if (Input.GetKeyDown(cancelKey))  CancelEdit();
    }

    void HandleInput()
    {
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            rightControllers);

        bool triggerPressed = false;
        bool gripPressed    = false;

        foreach (var dev in rightControllers)
        {
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool t)) triggerPressed = t;
            if (dev.TryGetFeatureValue(CommonUsages.gripButton,   out bool g)) gripPressed    = g;
        }

        // Fallback PC — tasto sinistro mouse per grab
        if (Input.GetMouseButton(0)) triggerPressed = true;

        bool triggerDown = triggerPressed && !prevTrigger;
        bool triggerUp   = !triggerPressed && prevTrigger;
        bool gripDown    = gripPressed && !prevGrip;

        prevTrigger = triggerPressed;
        prevGrip    = gripPressed;

        // Grab waypoint
        if (triggerDown && hoveredWaypoint >= 0)
        {
            grabbedWaypoint = hoveredWaypoint;
            SetWaypointColor(grabbedWaypoint, waypointGrabbedColor);
        }

        // Rilascia waypoint
        if (triggerUp && grabbedWaypoint >= 0)
        {
            SetWaypointColor(grabbedWaypoint, waypointColor);
            grabbedWaypoint = -1;
        }

        // Drag waypoint grabbato
        if (grabbedWaypoint >= 0)
            DragWaypoint(grabbedWaypoint);

        // Grip = conferma path
        if (gripDown) ConfirmPath();

        // Trigger hold per conferma VR
        if (triggerPressed && grabbedWaypoint < 0)
        {
            triggerHoldTime += Time.deltaTime;
            if (triggerHoldTime > 1.5f)
            {
                triggerHoldTime = 0f;
                ConfirmPath();
            }
        }
        else
            triggerHoldTime = 0f;
    }

    void UpdateHover()
    {
        Vector3 pointerPos = GetPointerPosition();
        int     newHovered = -1;
        float   minDist    = grabRadius;

        for (int i = 0; i < waypointCount; i++)
        {
            if (waypointObjects[i] == null) continue;
            float d = Vector3.Distance(pointerPos, waypointPositions[i]);
            if (d < minDist)
            {
                minDist    = d;
                newHovered = i;
            }
        }

        if (newHovered != hoveredWaypoint)
        {
            if (hoveredWaypoint >= 0 && hoveredWaypoint != grabbedWaypoint)
                SetWaypointColor(hoveredWaypoint, waypointColor);
            hoveredWaypoint = newHovered;
            if (hoveredWaypoint >= 0 && hoveredWaypoint != grabbedWaypoint)
                SetWaypointColor(hoveredWaypoint, waypointHoverColor);
        }
    }

    void DragWaypoint(int index)
    {
        // Proietta il controller sul piano del pavimento
        Vector3 pointerPos = GetPointerPosition();
        pointerPos.y = floorY + waypointHeight;

        waypointPositions[index]             = pointerPos;
        waypointObjects[index].transform.position = pointerPos;

        UpdateEditedPath();
    }

    Vector3 GetPointerPosition()
    {
        // VR: usa la posizione del controller
        if (rightControllers.Count > 0 && rightController != null)
            return rightController.position;

        // PC: ray dal mouse sul piano Y=floorY
        Ray   ray   = mainCamera.ScreenPointToRay(Input.mousePosition);
        float enter = 0f;
        Plane floor = new Plane(Vector3.up, new Vector3(0, floorY, 0));
        if (floor.Raycast(ray, out enter))
            return ray.GetPoint(enter);

        return mainCamera.transform.position + mainCamera.transform.forward * 2f;
    }

    void UpdateEditedPath()
    {
        if (waypointPositions == null) return;

        // Catmull-Rom spline tra i waypoint
        List<Vector3> splinePoints = new List<Vector3>();

        for (int i = 0; i < waypointPositions.Length - 1; i++)
        {
            Vector3 p0 = waypointPositions[Mathf.Max(i - 1, 0)];
            Vector3 p1 = waypointPositions[i];
            Vector3 p2 = waypointPositions[i + 1];
            Vector3 p3 = waypointPositions[Mathf.Min(i + 2, waypointPositions.Length - 1)];

            int steps = 10;
            for (int s = 0; s < steps; s++)
            {
                float t   = s / (float)steps;
                splinePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        splinePoints.Add(waypointPositions[waypointPositions.Length - 1]);

        editedLineRenderer.positionCount = splinePoints.Count;
        editedLineRenderer.SetPositions(splinePoints.ToArray());
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

    void SetWaypointColor(int index, Color color)
    {
        if (waypointObjects[index] == null) return;
        waypointObjects[index].GetComponent<Renderer>().material.color = color;
    }

    public void ConfirmPath()
    {
        Debug.Log("[PathEditor] Path confermato!");
        // Nascondi le 3 candidate ma mantieni linea editata visibile
        pathVisualizer.HideAllPaths();
        pathVisualizer.Freeze();
        // Notifica goal all'ultimo waypoint — serve per rilevare goal raggiunto
        if (pathVisualizer != null && waypointPositions != null && waypointPositions.Length > 0)
            pathVisualizer.SetEditedGoal(waypointPositions[waypointPositions.Length - 1]);
        OnPathConfirmed?.Invoke(waypointPositions);
        DeactivateKeepLine();
    }

    void DeactivateKeepLine()
    {
        IsActive = false;
        DestroyWaypoints();
        // NON disabilitiamo editedLineRenderer — rimane visibile
        // PathSelector rimane disattivo finché non arriva nuovo goal
    }

    public void CancelEdit()
    {
        Debug.Log("[PathEditor] Modifica annullata.");
        if (pathVisualizer != null) pathVisualizer.Unfreeze();
        OnEditCancelled?.Invoke();
        Deactivate();
    }

    /// <summary>Nasconde la linea editata — chiamato da PathExecutor dopo l'esecuzione.</summary>
    public void HideEditedPath()
    {
        editedLineRenderer.enabled       = false;
        editedLineRenderer.positionCount = 0;
        if (pathVisualizer != null) pathVisualizer.Unfreeze();
        if (pathSelector   != null) pathSelector.Activate();
    }

    public void Deactivate()
    {
        IsActive = false;
        DestroyWaypoints();
        editedLineRenderer.enabled = false;
        editedLineRenderer.positionCount = 0;
        if (pathSelector != null) pathSelector.Activate();
        Debug.Log("[PathEditor] Deactivate() chiamato!");
    }

    void DestroyWaypoints()
    {
        if (waypointObjects == null) return;
        foreach (var wp in waypointObjects)
            if (wp != null) Destroy(wp);
        waypointObjects = null;
    }

    void OnDestroy() => DestroyWaypoints();
}