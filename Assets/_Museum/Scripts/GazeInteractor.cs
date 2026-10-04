using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

/// Anything the visitor can look at and select: artifacts, teleport points, panel buttons, portals.
public interface IGazeTarget
{
    void OnGazeEnter();
    void OnGazeExit();
    void OnSelect();
}

/// Touch state shared by the look controller and the interactor (phone mode).
public static class PhoneInput
{
    /// True while the current touch began on a 2D UI element (e.g. the Menu button), so it neither looks nor taps.
    public static bool TouchStartedOnUI;

    static readonly List<RaycastResult> hits = new List<RaycastResult>();

    public static bool OverUI(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = screenPos }, hits);
        return hits.Count > 0;
    }
}

/// Attach to the Main Camera of the Player rig.
/// VR mode (Cardboard running): selects what the centre reticle points at, on the viewer button or after
/// gazing for dwellTime seconds. Phone mode / Editor: tap (click) directly on an object to select it;
/// dragging looks around (EditorMouseLook).
public class GazeInteractor : MonoBehaviour
{
    [Tooltip("Layers: Interactive + Teleport (+ Default so walls block)")]
    public LayerMask gazeMask;
    public float maxDistance = 15f;
    public float tapMaxDistance = 30f;
    public float dwellTime = 2f;

    [Header("Reticle (child of camera)")]
    public Transform reticle;          // small dot/ring quad
    public Image dwellFill;            // Image type Filled / Radial360 on a world-space canvas at the reticle
    public float idleSize = 0.015f;    // size per metre of distance
    public float activeSize = 0.04f;
    public float defaultDistance = 3f;

    /// True when the latest selection came from gazing (dwell) rather than a button press or tap.
    public static bool SelectedByDwell;

    IGazeTarget current;
    float gazeTimer;
    bool fired;
    Camera cam;

    // tap tracking
    Vector2 pressPos;
    float pressTime;
    bool pressing, pressOnUI, moved;

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        bool vr = MuseumApp.XRActive;
        if (reticle) reticle.gameObject.SetActive(vr);
        if (vr) GazeUpdate();
        else
        {
            if (current != null) { current.OnGazeExit(); current = null; }
            TapUpdate();
        }
    }

    // ---------------------------------------------------------------- VR: gaze + viewer button
    void GazeUpdate()
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

        bool trigger = ViewerButtonPressed();
        if (trigger || (!fired && gazeTimer >= dwellTime))
        {
            fired = true;          // no repeat until the visitor looks away
            SelectedByDwell = !trigger;
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

    static bool ViewerButtonPressed()
    {
#if UNITY_EDITOR
        return false;
#else
        return Api.IsTriggerPressed;
#endif
    }

    // ---------------------------------------------------------------- phone / Editor: tap to select
    void TapUpdate()
    {
        bool down = false, up = false; Vector2 pos = default;
        var ts = Touchscreen.current;
        if (ts != null && (ts.primaryTouch.press.isPressed || ts.primaryTouch.press.wasReleasedThisFrame))
        {
            pos = ts.primaryTouch.position.ReadValue();
            down = ts.primaryTouch.press.wasPressedThisFrame;
            up = ts.primaryTouch.press.wasReleasedThisFrame;
        }
        else if (Mouse.current != null)
        {
            var m = Mouse.current;
            pos = m.position.ReadValue();
            down = m.leftButton.wasPressedThisFrame;
            up = m.leftButton.wasReleasedThisFrame;
        }

        if (down)
        {
            pressing = true; moved = false; pressPos = pos; pressTime = Time.unscaledTime;
            pressOnUI = PhoneInput.OverUI(pos);
            PhoneInput.TouchStartedOnUI = pressOnUI;
        }
        if (pressing && (pos - pressPos).magnitude > Screen.dpi * 0.12f + 6f) moved = true;
        if (up && pressing)
        {
            pressing = false;
            bool tap = !moved && !pressOnUI && Time.unscaledTime - pressTime < 0.6f;
            PhoneInput.TouchStartedOnUI = false;
            if (tap) TapAt(pos);
        }
    }

    void TapAt(Vector2 screenPos)
    {
        if (!cam) cam = GetComponent<Camera>();
        var ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, tapMaxDistance, gazeMask, QueryTriggerInteraction.Collide)) return;
        var target = hit.collider.GetComponentInParent<IGazeTarget>();
        if (target == null) return;
        SelectedByDwell = false;
        target.OnGazeEnter();
        target.OnSelect();
        target.OnGazeExit();
    }
}
