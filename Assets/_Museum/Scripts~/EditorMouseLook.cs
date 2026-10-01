using UnityEngine;
using UnityEngine.InputSystem;

/// Editor-only testing: hold the right mouse button and move to look around; left click = Cardboard button.
/// Put on an empty "EditorLook" object between the Player rig and the Main Camera. Does nothing on the phone.
public class EditorMouseLook : MonoBehaviour
{
    public float sensitivity = 0.15f;
    float yaw, pitch;

    void Update()
    {
#if UNITY_EDITOR
        var m = Mouse.current;
        if (m == null || !m.rightButton.isPressed) return;
        Vector2 d = m.delta.ReadValue();
        yaw += d.x * sensitivity;
        pitch = Mathf.Clamp(pitch - d.y * sensitivity, -80f, 80f);
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
#endif
    }
}
