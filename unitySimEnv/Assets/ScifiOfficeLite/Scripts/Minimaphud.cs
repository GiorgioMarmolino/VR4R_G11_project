using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;

/// <summary>
/// Minimappa VR che visualizza la mappa ROS2 da /map (slam_toolbox).
/// Mostra la mappa costruita in tempo reale con la posizione del robot.
/// Va attaccato al GameObject del Canvas World Space.
/// Richiede un RawImage UI per visualizzare la mappa.
/// </summary>
public class MinimapHUD : MonoBehaviour
{
    [Header("Impostazioni ROS")]
    public string mapTopic  = "/map";
    public string odomTopic = "/odom";

    [Header("Riferimenti UI")]
    [Tooltip("Trascina qui un RawImage per visualizzare la mappa")]
    public RawImage mapImage;

    [Header("Impostazioni Mappa")]
    [Tooltip("Dimensione in pixel della texture della mappa")]
    public int textureSize = 256;
    [Tooltip("Colore dello sfondo (zona sconosciuta)")]
    public Color unknownColor  = new Color(0.5f, 0.5f, 0.5f, 1f); // grigio
    [Tooltip("Colore delle zone libere")]
    public Color freeColor     = new Color(1f, 1f, 1f, 1f);        // bianco
    [Tooltip("Colore degli ostacoli")]
    public Color occupiedColor = new Color(0f, 0f, 0f, 1f);        // nero
    [Tooltip("Colore del robot sulla mappa")]
    public Color robotColor    = new Color(1f, 0f, 0f, 1f);        // rosso
    [Tooltip("Dimensione del punto robot in pixel")]
    public int robotDotSize = 4;

    // Dati mappa
    private int mapWidth  = 0;
    private int mapHeight = 0;
    private float mapResolution = 0.05f;
    private Vector2 mapOrigin = Vector2.zero;
    private sbyte[] mapData;
    private bool mapDirty = false;

    // Posizione robot
    private float robotX = 0f;
    private float robotY = 0f;

    // Texture
    private Texture2D mapTexture;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<OccupancyGridMsg>(mapTopic, OnMapReceived);
        ros.Subscribe<OdometryMsg>(odomTopic, OnOdometryReceived);

        Debug.Log($"[MinimapHUD] In ascolto su: {mapTopic} e {odomTopic}");
    }

    void OnMapReceived(OccupancyGridMsg msg)
    {
        mapWidth      = (int)msg.info.width;
        mapHeight     = (int)msg.info.height;
        mapResolution = msg.info.resolution;
        mapOrigin     = new Vector2(
            (float)msg.info.origin.position.x,
            (float)msg.info.origin.position.y
        );

        // Converti i dati della mappa
        mapData = new sbyte[msg.data.Length];
        for (int i = 0; i < msg.data.Length; i++)
            mapData[i] = (sbyte)msg.data[i];

        mapDirty = true;
    }

    void OnOdometryReceived(OdometryMsg msg)
    {
        robotX = (float)msg.pose.pose.position.x;
        robotY = (float)msg.pose.pose.position.y;
        mapDirty = true;
    }

    void Update()
    {
        if (!mapDirty || mapData == null || mapWidth == 0 || mapHeight == 0) return;

        UpdateMapTexture();
        mapDirty = false;
    }

    void UpdateMapTexture()
    {
        // Crea o ridimensiona la texture se necessario
        if (mapTexture == null || mapTexture.width != mapWidth || mapTexture.height != mapHeight)
        {
            mapTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            mapTexture.filterMode = FilterMode.Point;
        }

        Color[] pixels = new Color[mapWidth * mapHeight];

        // Disegna la mappa
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                int index = y * mapWidth + x;
                sbyte value = mapData[index];

                if (value == -1)
                    pixels[index] = unknownColor;   // sconosciuto
                else if (value == 0)
                    pixels[index] = freeColor;       // libero
                else
                    pixels[index] = occupiedColor;   // ostacolo
            }
        }

        // Disegna il robot come punto rosso
        int robotPixelX = Mathf.RoundToInt((robotX - mapOrigin.x) / mapResolution);
        int robotPixelY = Mathf.RoundToInt((robotY - mapOrigin.y) / mapResolution);

        for (int dy = -robotDotSize; dy <= robotDotSize; dy++)
        {
            for (int dx = -robotDotSize; dx <= robotDotSize; dx++)
            {
                int px = robotPixelX + dx;
                int py = robotPixelY + dy;

                if (px >= 0 && px < mapWidth && py >= 0 && py < mapHeight)
                {
                    // Cerchio
                    if (dx * dx + dy * dy <= robotDotSize * robotDotSize)
                        pixels[py * mapWidth + px] = robotColor;
                }
            }
        }

        mapTexture.SetPixels(pixels);
        mapTexture.Apply();

        if (mapImage != null)
            mapImage.texture = mapTexture;
    }
}