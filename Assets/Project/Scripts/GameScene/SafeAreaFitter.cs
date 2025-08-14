using UnityEngine;

[ExecuteAlways]
public class SafeAreaFitter : MonoBehaviour
{
    Rect lastSafe;

    void OnEnable() { Apply(); }
    void Update()
    {
        if (Screen.safeArea != lastSafe) Apply();
    }

    void Apply()
    {
        var rt = (RectTransform)transform;
        var sa = Screen.safeArea;
        lastSafe = sa;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            return;

        Vector2 anchorMin = sa.position;
        Vector2 anchorMax = sa.position + sa.size;

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }
}
