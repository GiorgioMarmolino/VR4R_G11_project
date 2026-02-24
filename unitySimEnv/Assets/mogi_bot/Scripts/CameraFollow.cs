using UnityEngine;

/// <summary>
/// Fa seguire la camera al robot mogi_bot.
/// Va attaccato alla Main Camera.
/// Trascina base_link del robot nel campo Target.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("Trascina qui base_link del robot dalla Hierarchy")]
    public Transform target;

    [Tooltip("Offset della camera rispetto al robot")]
    public Vector3 offset = new Vector3(0, 0.5f, -1f);

    [Tooltip("Se true la camera ruota insieme al robot, se false rimane fissa")]
    public bool rotateWithTarget = true;

    void LateUpdate()
    {
        if (target == null)
        {
            Debug.LogWarning("[CameraFollow] Nessun target assegnato!");
            return;
        }

        if (rotateWithTarget)
        {
            // Camera segue posizione E rotazione del robot
            transform.position = target.position + target.rotation * offset;
            transform.LookAt(target.position + target.up * 0.3f);
        }
        else
        {
            // Camera segue solo la posizione, non ruota
            transform.position = target.position + offset;
            transform.LookAt(target.position);
        }
    }
}