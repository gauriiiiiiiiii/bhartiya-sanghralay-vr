using UnityEngine;

/// Keeps a full-screen UI panel inside the phone's safe area (notch, rounded corners, home indicator).
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    Rect last;

    void Update()
    {
        var sa = Screen.safeArea;
        if (sa == last || Screen.width == 0) return;
        last = sa;
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
