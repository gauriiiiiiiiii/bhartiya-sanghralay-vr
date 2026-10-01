using Google.XR.Cardboard;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// Anything the visitor can look at and select: artifacts, teleport points, panel buttons.
public interface IGazeTarget
{
    void OnGazeEnter();
    void OnGazeExit();
    void OnSelect();
}

/// Attach to the Main Camera of the Cardboard Player rig.
/// Selects on Cardboard button (screen tap) or after gazing for dwellTime seconds.
public class GazeInteractor : MonoBehaviour
{
    [Tooltip("Layers: Interactive + Teleport")]
    public LayerMask gazeMask;
    public float maxDistance = 15f;
    public float dwellTime = 2f;

    [Header("Reticle (child of camera)")]
    public Transform reticle;          // small dot/ring quad, unlit, always-on-top material
    public Image dwellFill;            // Image type Filled / Radial360 on a world-space canvas at the reticle
    public float idleSize = 0.015f;    // size per metre of distance
    public float activeSize = 0.04f;
    public float defaultDistance = 3f;

    IGazeTarget current;
    float gazeTimer;
    bool fired;

    void Update()
    {
        var ray = new Ray(transform.position, transform.forward);
        IGazeTarget hitTarget = null;
        float dist = defaultDistance;

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, gazeMask, QueryTriggerInteraction.Collide))
        {
            hitTarget = hit.collider.GetComponentInParent<IGazeTarget>();
            dist = hit.distance;
        }

        if (!ReferenceEquals(hitTarget, current))
        {
            current?.OnGazeExit();
            current = hitTarget;
            current?.OnGazeEnter();
            gazeTimer = 0f;
            fired = false;
        }

        UpdateReticle(dist);

        if (current == null)
        {
            if (dwellFill) dwellFill.fillAmount = 0f;
            return;
        }

        if (!fired) gazeTimer += Time.deltaTime;
        if (dwellFill) dwellFill.fillAmount = fired ? 0f : Mathf.Clamp01(gazeTimer / dwellTime);

        if (TriggerPressed() || (!fired && gazeTimer >= dwellTime))
        {
            fired = true;          // no repeat until the visitor looks away
            current.OnSelect();
        }
    }

    void UpdateReticle(float dist)
    {
        if (!reticle) return;
        float d = Mathf.Max(dist - 0.02f, 0.3f);
        reticle.position = transform.position + transform.forward * d;
        reticle.rotation = transform.rotation;
        float size = (current != null ? activeSize : idleSize) * d;   // constant angular size
        reticle.localScale = Vector3.Lerp(reticle.localScale, Vector3.one * size, Time.deltaTime * 12f);
    }

    static bool TriggerPressed()
    {
#if UNITY_EDITOR
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Api.IsTriggerPressed;
#endif
    }
}
