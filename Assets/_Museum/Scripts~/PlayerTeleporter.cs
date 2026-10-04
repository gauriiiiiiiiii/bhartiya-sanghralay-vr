using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Attach to the Player rig root. Moves the rig with a quick fade to avoid motion sickness,
/// and loads other museum scenes (wings / lobby) behind a fade.
public class PlayerTeleporter : MonoBehaviour
{
    public static PlayerTeleporter Instance { get; private set; }

    /// Set before loading a scene: the visitor is placed at the GameObject "Arrive_<tag>" in the new scene.
    public static string PendingArrival;

    public Transform playerRig;        // the Player root
    public Transform headCamera;       // Main Camera under the rig
    [Tooltip("World-space canvas 0.3 m in front of the camera (child of camera) with a full black Image.")]
    public CanvasGroup fade;
    public float fadeTime = 0.2f;
    public float sceneFadeTime = 0.45f;

    bool busy;

    void Awake()
    {
        Instance = this;
        if (!playerRig) playerRig = transform;
        if (fade) fade.alpha = 1f;               // every scene starts black and fades in
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(PendingArrival))
        {
            var target = GameObject.Find("Arrive_" + PendingArrival);
            if (target) Place(target.transform.position, target.transform.eulerAngles.y);
            PendingArrival = null;
        }
        StartCoroutine(Fade(1f, 0f, sceneFadeTime));
    }

    /// floorPos: point on the floor. faceYaw: optional world yaw the visitor should face on arrival.
    public void TeleportTo(Vector3 floorPos, float? faceYaw = null)
    {
        if (!busy) StartCoroutine(DoTeleport(floorPos, faceYaw));
    }

    /// Fades out and loads another scene (it must be in Build Settings).
    public void LoadScene(string sceneName, string arrivalTag)
    {
        if (!busy) StartCoroutine(DoLoad(sceneName, arrivalTag));
    }

    IEnumerator DoTeleport(Vector3 p, float? faceYaw)
    {
        busy = true;
        yield return Fade(0f, 1f, fadeTime);
        Place(p, faceYaw);
        yield return Fade(1f, 0f, fadeTime);
        busy = false;
    }

    IEnumerator DoLoad(string sceneName, string arrivalTag)
    {
        busy = true;
        if (InfoPanel.Instance) InfoPanel.Instance.Close();
        yield return Fade(0f, 1f, sceneFadeTime);
        PendingArrival = arrivalTag;
        var op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null) { PendingArrival = null; yield return Fade(1f, 0f, fadeTime); busy = false; }
    }

    void Place(Vector3 p, float? faceYaw)
    {
        playerRig.position = new Vector3(p.x, playerRig.position.y, p.z);
        if (faceYaw.HasValue && headCamera)
        {
            // Rotate the rig so the head (which the visitor controls) ends up facing faceYaw.
            float headYaw = headCamera.eulerAngles.y - playerRig.eulerAngles.y;
            playerRig.rotation = Quaternion.Euler(0f, faceYaw.Value - headYaw, 0f);
        }
    }

    IEnumerator Fade(float from, float to, float time)
    {
        if (!fade) yield break;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            fade.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        fade.alpha = to;
    }
}
