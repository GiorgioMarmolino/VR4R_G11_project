using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using TMPro;

/// <summary>
/// Bussola orizzontale scorrevole stile militare/CoD.
/// 
/// Setup nel Canvas:
///   CompassHUD (questo script)
///   └── CompassBar (RawImage — larghezza doppia, mascherata)
///       └── Mask (RectMask2D sul parent)
///   └── CenterMarker (Image — triangolino in basso al centro)
///   └── DegreeText (TextMeshPro — mostra i gradi attuali)
/// 
/// La texture della barra viene generata proceduralmente —
/// non servono sprite esterni.
/// </summary>
public class CompassHUD : MonoBehaviour
{
    [Header("Riferimenti UI")]
    [Tooltip("RawImage che contiene la barra scorrevole della bussola")]
    public RawImage compassBar;

    [Tooltip("TextMeshPro che mostra i gradi attuali sotto la barra")]
    public TextMeshProUGUI degreeText;

    [Tooltip("Image del marker centrale (triangolino)")]
    public Image centerMarker;

    [Header("Dimensioni Texture")]
    [Tooltip("Larghezza totale della texture (rappresenta 360°)")]
    public int textureWidth  = 1440; // 4px per grado
    public int textureHeight = 64;

    [Header("Colori")]
    public Color backgroundColor  = new Color(0.05f, 0.05f, 0.05f, 0.85f);
    public Color tickColorMajor   = new Color(1f,    1f,    1f,    1f);
    public Color tickColorMinor   = new Color(0.6f,  0.6f,  0.6f,  0.8f);
    public Color cardinalColor    = new Color(1f,    0.85f, 0.2f,  1f);   // giallo oro
    public Color northColor       = new Color(1f,    0.25f, 0.15f, 1f);   // rosso nord
    public Color centerLineColor  = new Color(0f,    0.9f,  1f,    1f);   // ciano

    [Header("Tick Settings")]
    public int majorTickInterval  = 45;  // ogni 45° tick grande + lettera
    public int minorTickInterval  = 15;  // ogni 15° tick medio
    public int tinyTickInterval   = 5;   // ogni 5°  tick piccolo

    // Stato
    private float yawDegrees     = 0f;
    private float smoothYaw      = 0f;
    private Texture2D compassTex;

    // Nomi cardinali
    private static readonly (int deg, string label)[] Cardinals = {
        (0,   "N"),
        (45,  "NE"),
        (90,  "E"),
        (135, "SE"),
        (180, "S"),
        (225, "SW"),
        (270, "W"),
        (315, "NW"),
    };

    void Start()
    {
        BuildTexture();

        ROSConnection.GetOrCreateInstance()
            .Subscribe<OdometryMsg>("/odom", OnOdomReceived);

        // Colore marker centrale
        if (centerMarker != null)
            centerMarker.color = centerLineColor;
    }

    void BuildTexture()
    {
        // Texture 2x larga per poter scorrere senza salti
        compassTex = new Texture2D(textureWidth * 2, textureHeight, TextureFormat.RGBA32, false);
        compassTex.wrapMode   = TextureWrapMode.Repeat;
        compassTex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[textureWidth * 2 * textureHeight];

        // Sfondo
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;

        // Disegna i tick per 720° (2 giri — wrap senza artefatti)
        for (int repeat = 0; repeat < 2; repeat++)
        {
            for (int deg = 0; deg < 360; deg++)
            {
                int px = Mathf.RoundToInt((repeat * 360 + deg) / 360f * textureWidth);
                px = Mathf.Clamp(px, 0, textureWidth * 2 - 1);

                bool isMajor    = (deg % majorTickInterval == 0);
                bool isMinor    = !isMajor && (deg % minorTickInterval == 0);
                bool isTiny     = !isMajor && !isMinor && (deg % tinyTickInterval == 0);
                bool isNorth    = (deg == 0);
                bool isCardinal = false;
                string label    = "";

                foreach (var c in Cardinals)
                {
                    if (c.deg == deg)
                    {
                        isCardinal = true;
                        label      = c.label;
                        break;
                    }
                }

                Color tickColor = isNorth ? northColor :
                                  isCardinal ? cardinalColor :
                                  isMajor ? tickColorMajor : tickColorMinor;

                int tickHeight = isMajor || isCardinal ? textureHeight :
                                 isMinor               ? textureHeight / 2 :
                                                         textureHeight / 4;

                // Disegna tick (dal basso verso l'alto)
                if (isMajor || isMinor || isTiny)
                {
                    int tickWidth = isMajor ? 3 : 1;
                    for (int tx = -tickWidth / 2; tx <= tickWidth / 2; tx++)
                    {
                        int col = Mathf.Clamp(px + tx, 0, textureWidth * 2 - 1);
                        for (int y = 0; y < tickHeight; y++)
                        {
                            float alpha = 1f - (float)y / tickHeight * 0.3f;
                            pixels[y * textureWidth * 2 + col] = new Color(
                                tickColor.r, tickColor.g, tickColor.b, tickColor.a * alpha);
                        }
                    }
                }

                // Disegna lettera cardinale come pattern di pixel
                if (isCardinal && label.Length > 0)
                {
                    DrawPixelLabel(pixels, px, label, isNorth ? northColor : cardinalColor);
                }
            }
        }

        // Linea centrale verticale (marker fisso — solo visivo)
        int cx = textureWidth; // centro della texture
        for (int y = 0; y < textureHeight; y++)
        {
            pixels[y * textureWidth * 2 + cx]     = centerLineColor;
            pixels[y * textureWidth * 2 + cx + 1] = centerLineColor;
        }

        compassTex.SetPixels(pixels);
        compassTex.Apply();

        if (compassBar != null)
            compassBar.texture = compassTex;
    }

    void DrawPixelLabel(Color[] pixels, int centerX, string label, Color color)
    {
        // Font pixel 5x7 minimal per le lettere cardinali
        int startY = textureHeight - 12;
        int charW  = 4;
        int charH  = 6;

        foreach (char ch in label)
        {
            bool[,] glyph = GetGlyph(ch);
            if (glyph == null) continue;

            int offsetX = centerX - (label.Length * charW) / 2;
            for (int gy = 0; gy < charH; gy++)
            {
                for (int gx = 0; gx < charW; gx++)
                {
                    if (glyph[gy, gx])
                    {
                        int px2 = Mathf.Clamp(offsetX + gx, 0, textureWidth * 2 - 1);
                        int py  = Mathf.Clamp(startY + gy,  0, textureHeight - 1);
                        pixels[py * textureWidth * 2 + px2] = color;
                    }
                }
            }
            offsetX += charW + 1;
        }
    }

    bool[,] GetGlyph(char c)
    {
        // Font pixel 4x6 per N, S, E, W + combinazioni
        switch (c)
        {
            case 'N': return new bool[,] {
                {true,false,false,true},
                {true,true,false,true},
                {true,false,true,true},
                {true,false,false,true},
                {true,false,false,true},
                {true,false,false,true} };
            case 'S': return new bool[,] {
                {false,true,true,true},
                {true,false,false,false},
                {false,true,true,false},
                {false,false,false,true},
                {false,false,false,true},
                {true,true,true,false} };
            case 'E': return new bool[,] {
                {true,true,true,true},
                {true,false,false,false},
                {true,true,true,false},
                {true,false,false,false},
                {true,false,false,false},
                {true,true,true,true} };
            case 'W': return new bool[,] {
                {true,false,false,true},
                {true,false,false,true},
                {true,false,true,true},
                {true,true,false,true},
                {true,true,false,true},
                {true,false,false,true} };
            default: return null;
        }
    }

    void OnOdomReceived(OdometryMsg msg)
    {
        float qx = (float)msg.pose.pose.orientation.x;
        float qy = (float)msg.pose.pose.orientation.y;
        float qz = (float)msg.pose.pose.orientation.z;
        float qw = (float)msg.pose.pose.orientation.w;

        yawDegrees = Mathf.Atan2(2f * (qw * qz + qx * qy),
                                   1f - 2f * (qy * qy + qz * qz)) * Mathf.Rad2Deg;

        // Normalizza 0-360
        yawDegrees = (yawDegrees + 360f) % 360f;
    }

    void Update()
    {
        // Smooth interpolation
        smoothYaw = Mathf.LerpAngle(smoothYaw, yawDegrees, Time.deltaTime * 8f);
        float normalizedYaw = (smoothYaw + 360f) % 360f;

        // Scorri la texture: UV.x va da 0 a 1 per 360°
        if (compassBar != null)
        {
            float uvX = normalizedYaw / 360f;
            compassBar.uvRect = new Rect(uvX - 0.25f, 0f, 0.5f, 1f);
        }

        // Testo gradi
        if (degreeText != null)
        {
            string cardinal = GetCardinalName(normalizedYaw);
            degreeText.text = $"<color=#00E5FF>{cardinal}</color>  {normalizedYaw:F0}°";
        }
    }

    string GetCardinalName(float deg)
    {
        if (deg < 22.5f || deg >= 337.5f)  return "N";
        if (deg < 67.5f)                    return "NE";
        if (deg < 112.5f)                   return "E";
        if (deg < 157.5f)                   return "SE";
        if (deg < 202.5f)                   return "S";
        if (deg < 247.5f)                   return "SW";
        if (deg < 292.5f)                   return "W";
        return "NW";
    }

    void OnDestroy()
    {
        if (compassTex != null)
            Destroy(compassTex);
    }
}