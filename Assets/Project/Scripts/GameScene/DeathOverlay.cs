using System.Collections;
using UnityEngine;
using TMPro; // TMP_Text

/// <summary>
/// Einfaches Fade-Overlay mit Countdown pro Client (kein Netcode nötig).
/// In der GameScene ein UI-Panel (CanvasGroup) mit zwei TMP_Texts anlegen und verdrahten.
/// </summary>
public class DeathOverlay : MonoBehaviour
{
    public static DeathOverlay I;

    [Header("Refs")]
    public CanvasGroup group;            // CanvasGroup am Overlay-Panel
    public TMP_Text titleText;           // "You died"
    public TMP_Text countdownText;       // "Respawning in: 3s"

    [Header("Timing")]
    public float fadeDuration = 0.25f;   // Sekunden für Fade In/Out

    Coroutine running;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (!group) group = GetComponent<CanvasGroup>();
        if (group)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        if (titleText && string.IsNullOrEmpty(titleText.text))
            titleText.text = "You died";
        if (countdownText) countdownText.text = "";
    }

    /// <summary>Overlay einblenden, Countdown anzeigen, dann wieder ausblenden.</summary>
    public void ShowFor(float seconds)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(ShowRoutine(seconds));
    }

    /// <summary>Overlay sofort ausblenden/abbrechen.</summary>
    public void HideImmediate()
    {
        if (running != null) StopCoroutine(running);
        SetAlpha(0f);
        if (countdownText) countdownText.text = "";
    }

    IEnumerator ShowRoutine(float seconds)
    {
        // Fade In
        yield return Fade(0f, 1f, fadeDuration);
        if (titleText) titleText.text = "You died";

        // Countdown (unscaled, damit Time.timeScale egal ist)
        float t = Mathf.Max(0f, seconds);
        while (t > 0f)
        {
            if (countdownText) countdownText.text = $"Respawning in: {Mathf.CeilToInt(t)}s";
            t -= Time.unscaledDeltaTime;
            yield return null;
        }
        if (countdownText) countdownText.text = "Respawning…";

        // Fade Out
        yield return Fade(1f, 0f, fadeDuration);
        if (countdownText) countdownText.text = "";
        running = null;
    }

    IEnumerator Fade(float from, float to, float dur)
    {
        if (!group) yield break;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            SetAlpha(a);
            yield return null;
        }
        SetAlpha(to);
    }

    void SetAlpha(float a)
    {
        if (!group) return;
        group.alpha = a;
        group.blocksRaycasts = a > 0.001f;
        group.interactable = a > 0.001f;
    }
}
