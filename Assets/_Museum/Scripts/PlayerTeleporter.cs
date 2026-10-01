using System.Collections;
using UnityEngine;

/// Attach to the Player rig root. Moves the rig with a quick fade to avoid motion sickness.
public class PlayerTeleporter : MonoBehaviour
{
    public static PlayerTeleporter Instance { get; private set; }

    public Transform playerRig;        // the Cardboard Player root
    public Transform headCamera;       // Main Camera under the rig
    [Tooltip("World-space canvas 0.3 m in front of the camera (child of camera) with a full black Image.")]
    public CanvasGroup fade;
    public float fadeTime = 0.2f;

    bool busy;

    void Awake()
    {
        Instance = this;
        if (!playerRig) playerRig = transform;
        if (fade) fade.alpha = 0f;
    }

    /// floorPos: point on the floor. faceYaw: optional world yaw the visitor should face on arrival.
    public void TeleportTo(Vector3 floorPos, float? faceYaw = null)
    {
        if (!busy) StartCoroutine(DoTeleport(floorPos, faceYaw));
    }

    IEnumerator DoTeleport(Vector3 p, float? faceYaw)
    {
        busy = true;
        yield return Fade(0f, 1f);

        playerRig.position = new Vector3(p.x, playerRig.position.y, p.z);
        if (faceYaw.HasValue && headCamera)
        {
            // Rotate the rig so the head (which the visitor controls) ends up facing faceYaw.
            float headYaw = headCamera.localEulerAngles.y;
            playerRig.rotation = Quaternion.Euler(0f, faceYaw.Value - headYaw, 0f);
        }

        yield return Fade(1f, 0f);
        busy = false;
    }

    IEnumerator Fade(float from, float to)
    {
        if (!fade) yield break;
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            fade.alpha = Mathf.Lerp(from, to, t / fadeTime);
            yield return null;
        }
        fade.alpha = to;
    }
}
