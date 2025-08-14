using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingViewTMP : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Image ring;          // Radial-Filled Image
    [SerializeField] TMP_Text percent;    // "8%"
    [SerializeField] TMP_Text status;     // "Loading.../Downloading.../Please wait..."

    [Header("Spinner (falls kein Fortschritt gesetzt)")]
    [SerializeField] bool spinIfNoProgress = true;
    [SerializeField] float spinsPerSecond = 0.6f;

    float? progress; // null => unbekannt -> Spinner

    void Update()
    {
        if (progress == null && spinIfNoProgress && ring)
            ring.fillAmount = Mathf.Repeat(Time.unscaledTime * spinsPerSecond, 1f);
    }

    public void ClearProgress()
    {
        progress = null;
        if (percent) percent.text = "";
    }

    public void SetProgress(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        progress = t01;

        if (ring) ring.fillAmount = t01;
        if (percent) percent.text = Mathf.RoundToInt(t01 * 100f) + "%";

        if (!status) return;
        if (t01 <= 0.33f) status.text = "Loading...";
        else if (t01 <= 0.67f) status.text = "Downloading...";
        else status.text = "Please wait...";
    }

    public void SetStatus(string text)
    {
        if (status) status.text = text ?? "";
    }
}
