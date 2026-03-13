using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;

/// <summary>
/// SelectionUIHUD — pannello tattico per la shared autonomy.
/// Guida l'utente attraverso: IDLE → SELECTING → SELECTED → EDITING → EXECUTING
///
/// Setup Canvas:
///   SelectionUIHUD (questo script)
///   ├── BannerPanel          — striscia in alto con stato corrente
///   │   ├── StatusLabel      — "SELECTING TRAJECTORY"
///   │   └── InstructionText  — istruzioni contestuali
///   ├── ActionPanel          — pannello centrale con bottoni
///   │   ├── PathInfoText     — info sul path selezionato
///   │   ├── ModifyButton     — [✎ MODIFICA]
///   │   └── ExecuteButton    — [▶ ESEGUI]
///   ├── EditPanel            — pannello durante editing
///   │   ├── EditInfoText     — waypoint modificati
///   │   ├── CancelButton     — [✗ ANNULLA]
///   │   └── ConfirmButton    — [✓ CONFERMA]
///   └── ExecutingPanel       — pannello durante navigazione
///       ├── NavStatusText    — "NAVIGATING"
///       └── NavInfoText      — distanza + ETA
/// </summary>
public class SelectionUIHUD : MonoBehaviour
{
    public enum UIState { Idle, Selecting, Selected, Editing, Executing }

    [Header("Riferimenti Script")]
    public CandidatePathVisualizer pathVisualizer;
    public PathSelector            pathSelector;
    public PathEditor              pathEditor;
    public PathExecutor            pathExecutor;

    [Header("Pannelli")]
    public GameObject bannerPanel;
    public GameObject actionPanel;
    public GameObject editPanel;
    public GameObject executingPanel;

    [Header("Banner")]
    public TextMeshProUGUI statusLabel;
    public TextMeshProUGUI instructionText;

    [Header("Action Panel")]
    public TextMeshProUGUI pathInfoText;
    public Button          modifyButton;
    public Button          executeButton;

    [Header("Edit Panel")]
    public TextMeshProUGUI editInfoText;
    public Button          cancelButton;
    public Button          confirmButton;

    [Header("Executing Panel")]
    public TextMeshProUGUI navStatusText;
    public TextMeshProUGUI navInfoText;

    [Header("Colori Stato")]
    public Color colorIdle      = new Color(0.3f,  0.3f,  0.3f,  0.8f);
    public Color colorSelecting = new Color(0f,    0.6f,  1f,    0.9f);  // blu
    public Color colorSelected  = new Color(0f,    1f,    0.7f,  0.9f);  // ciano
    public Color colorEditing   = new Color(1f,    0.6f,  0f,    0.9f);  // arancione
    public Color colorExecuting = new Color(0.2f,  1f,    0.3f,  0.9f);  // verde

    // Stato corrente
    public UIState CurrentState { get; private set; } = UIState.Idle;

    // Dati navigazione
    private Vector2 robotPos   = Vector2.zero;
    private Vector2 goalPos    = Vector2.zero;
    private float   robotSpeed = 0f;
    private bool    hasGoal    = false;

    // Animazione pulsante
    private float   pulseTimer = 0f;
    private Image   bannerBg;

    // Path selezionato
    private int    selectedPathIndex = -1;
    private float  selectedPathLength = 0f;

    void Start()
    {
        // Collega bottoni
        if (modifyButton  != null) modifyButton.onClick.AddListener(OnModifyClicked);
        if (executeButton != null) executeButton.onClick.AddListener(OnExecuteClicked);
        if (cancelButton  != null) cancelButton.onClick.AddListener(OnCancelClicked);
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);

        // Collega eventi
        if (pathSelector != null)
            pathSelector.OnPathSelected += OnPathSelected;

        if (pathEditor != null)
        {
            pathEditor.OnPathConfirmed  += _ => SetState(UIState.Executing);
            pathEditor.OnEditCancelled  += () => SetState(UIState.Selecting);
        }

        if (pathExecutor != null)
        {
            pathExecutor.OnExecutionStarted  += () => SetState(UIState.Executing);
            pathExecutor.OnExecutionComplete += () => { };
        }

        // ROS — odom per distanza/ETA
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<RosMessageTypes.Nav.OdometryMsg>("/odom", OnOdomReceived);
        ros.Subscribe<RosMessageTypes.Geometry.PoseStampedMsg>("/goal_pose_request", OnGoalReceived);
        //ros.Subscribe<RosMessageTypes.Geometry.PoseStampedMsg>("/goal_pose", OnGoalReceived);

        bannerBg = bannerPanel?.GetComponent<Image>();

        SetState(UIState.Idle);
        Debug.Log("[SelectionUIHUD] Pronto.");
    }

    void OnOdomReceived(RosMessageTypes.Nav.OdometryMsg msg)
    {
        robotPos = new Vector2(
            (float)msg.pose.pose.position.x,
            (float)msg.pose.pose.position.y);
        robotSpeed = Mathf.Sqrt(
            (float)(msg.twist.twist.linear.x * msg.twist.twist.linear.x) +
            (float)(msg.twist.twist.linear.y * msg.twist.twist.linear.y));

        // Controlla goal raggiunto
        if (hasGoal && CurrentState == UIState.Executing &&
            Vector2.Distance(robotPos, goalPos) < 0.4f)
        {
            hasGoal = false;
            SetState(UIState.Idle);
        }
    }

    void OnGoalReceived(RosMessageTypes.Geometry.PoseStampedMsg msg)
    {
        goalPos = new Vector2(
            (float)msg.pose.position.x,
            (float)msg.pose.position.y);
        hasGoal = true;
        SetState(UIState.Selecting);
    }

    void OnPathSelected(int index)
    {
        selectedPathIndex = index;

        // Calcola lunghezza del path selezionato
        Vector3[] pts = pathVisualizer?.GetPathPoints(index);
        selectedPathLength = 0f;
        if (pts != null)
            for (int i = 0; i < pts.Length - 1; i++)
                selectedPathLength += Vector3.Distance(pts[i], pts[i + 1]);

        SetState(UIState.Selected);
    }

    // ── Bottoni ──────────────────────────────────────────────────────────────

    void OnModifyClicked()
    {
        SetState(UIState.Editing);
        if (pathEditor != null) pathEditor.Activate();
    }

    void OnExecuteClicked()
    {
        SetState(UIState.Executing);
        if (pathExecutor != null)
        {
            Vector3[] pts = pathVisualizer?.GetSelectedPathPoints();
            if (pts != null) pathExecutor.ExecutePath(pts);
        }
        // Freezing del visualizer
        if (pathVisualizer != null) pathVisualizer.Freeze();
    }

    void OnCancelClicked()
    {
        if (pathEditor != null) pathEditor.CancelEdit();
        SetState(UIState.Selecting);
    }

    void OnConfirmClicked()
    {
        if (pathEditor != null) pathEditor.ConfirmPath();
        // SetState(Executing) viene chiamato da pathEditor.OnPathConfirmed
    }

    // ── Gestione stati ───────────────────────────────────────────────────────

    public void SetState(UIState newState)
    {
        CurrentState = newState;
        UpdatePanels();
        UpdateContent();
        Debug.Log($"[SelectionUIHUD] Stato: {newState}");
    }

    void UpdatePanels()
    {
        if (bannerPanel    != null) bannerPanel.SetActive(
            CurrentState != UIState.Idle);
        if (actionPanel    != null) actionPanel.SetActive(
            CurrentState == UIState.Selected);
        if (editPanel      != null) editPanel.SetActive(
            CurrentState == UIState.Editing);
        if (executingPanel != null) executingPanel.SetActive(
            CurrentState == UIState.Executing);
    }

    void UpdateContent()
    {
        switch (CurrentState)
        {
            case UIState.Idle:
                break;

            case UIState.Selecting:
                SetBannerColor(colorSelecting);
                if (statusLabel     != null) statusLabel.text     = "◈ SELECTING TRAJECTORY";
                if (instructionText != null) instructionText.text =
                    "Point toward a path and press TRIGGER to select";
                if (pathSelector    != null) pathSelector.Activate();
                break;

            case UIState.Selected:
                SetBannerColor(colorSelected);
                if (statusLabel     != null) statusLabel.text     = "✓ TRAJECTORY SELECTED";
                if (instructionText != null) instructionText.text =
                    $"PATH {selectedPathIndex} — Choose action below";

                string pathName = selectedPathIndex switch {
                    0 => "OPTIMAL",
                    1 => "LEFT VARIANT",
                    2 => "RIGHT VARIANT",
                    _ => "UNKNOWN"
                };
                float eta = robotSpeed > 0.05f ? selectedPathLength / robotSpeed : -1f;
                string etaStr = eta > 0 ? $"{eta:F0}s" : "--";

                if (pathInfoText != null)
                    pathInfoText.text =
                        $"<size=11><color=#888888>ROUTE</color></size>\n" +
                        $"<b>{pathName}</b>\n\n" +
                        $"<size=11><color=#888888>LENGTH</color></size>  " +
                        $"<color=#00FFB2>{selectedPathLength:F1}m</color>\n" +
                        $"<size=11><color=#888888>ETA</color></size>     " +
                        $"<color=#00FFB2>~{etaStr}</color>";
                break;

            case UIState.Editing:
                SetBannerColor(colorEditing);
                if (statusLabel     != null) statusLabel.text     = "✎ EDITING TRAJECTORY";
                if (instructionText != null) instructionText.text =
                    "Drag waypoints to reshape path — GRIP or CONFIRM when done";
                if (editInfoText    != null) editInfoText.text    =
                    $"<color=#888888>WAYPOINTS</color>  " +
                    $"<color=#FF9900>{pathEditor?.waypointCount ?? 0} points</color>\n\n" +
                    "Drag spheres to modify\nENTER to confirm  •  ESC to cancel";
                break;

            case UIState.Executing:
                SetBannerColor(colorExecuting);
                if (statusLabel  != null) statusLabel.text  = "▶ EXECUTING";
                if (instructionText != null) instructionText.text = "Robot navigating to goal...";
                break;
        }
    }

    void SetBannerColor(Color color)
    {
        if (bannerBg != null) bannerBg.color = color;
    }

    void Update()
    {
        pulseTimer += Time.deltaTime;

        // Aggiorna info navigazione in real-time
        if (CurrentState == UIState.Executing && navInfoText != null)
        {
            float dist = Vector2.Distance(robotPos, goalPos);
            float eta  = robotSpeed > 0.05f ? dist / robotSpeed : -1f;
            string etaStr = eta > 0 ? $"~{eta:F0}s" : "--";

            // Pulse sull'icona
            float alpha = 0.6f + 0.4f * Mathf.Sin(pulseTimer * 3f);
            string pulse = $"<alpha=#{(int)(alpha * 255):X2}>";

            navStatusText.text = $"{pulse}▶</color> NAVIGATING";
            navInfoText.text   =
                $"<color=#888888>DISTANCE</color>  <color=#00FF44>{dist:F1}m</color>\n" +
                $"<color=#888888>ETA     </color>  <color=#00FF44>{etaStr}</color>\n" +
                $"<color=#888888>SPEED   </color>  <color=#00FF44>{robotSpeed:F2}m/s</color>";
        }

        // Keyboard shortcut: M=modifica, E=esegui, C=cancella, Enter=conferma
        if (CurrentState == UIState.Selected)
        {
            if (Input.GetKeyDown(KeyCode.M)) OnModifyClicked();
            if (Input.GetKeyDown(KeyCode.E)) OnExecuteClicked();
        }
    }
}//