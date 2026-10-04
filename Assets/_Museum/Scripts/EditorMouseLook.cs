using UnityEngine;
using UnityEngine.InputSystem;

/// Look control when the Cardboard display is not running (phone mode and the Editor):
/// drag one finger on the phone, or hold the right mouse button and move in the Editor.
/// Sits on the "EditorLook" object between the Player rig and the Main Camera; in VR mode it stays neutral.
public class EditorMouseLook : MonoBehaviour
{
    public float mouseSensitivity = 0.15f;
    [Tooltip("Degrees per screen-height of finger travel")]
    public float touchDegreesPerScreen = 90f;
    float yaw, pitch;

    void Update()
    {
        if (MuseumApp.XRActive)
        {
            if (transform.localRotation != Quaternion.identity) { transform.localRotation = Quaternion.identity; yaw = pitch = 0f; }
            return;
        }
        Vector2 d = Vector2.zero;
        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch.press.isPressed && !PhoneInput.TouchStartedOnUI)
        {
            var delta = ts.primaryTouch.delta.ReadValue();
            float k = touchDegreesPerScreen / Mathf.Max(1, Screen.height);
            d = new Vector2(-delta.x * k, -delta.y * k);          // drag the world: finger right turns left
        }
#if UNITY_EDITOR
        var m = Mouse.current;
        if (m != null && m.rightButton.isPressed) d = m.delta.ReadValue() * mouseSensitivity;
#endif
        if (d == Vector2.zero) return;
        yaw += d.x;
        pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
