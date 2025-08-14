using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingOverlay : MonoBehaviour
{
    public static LoadingOverlay I { get; private set; }

    [Header("Refs")]
    [SerializeField] GameObject panelRoot;   // ← dieses Panel wird aktiviert/deaktiviert
    [SerializeField] CanvasGroup group;      // am PanelRoot
    [SerializeField] LoadingViewTMP view;    // an vica

    [Header("Behaviour")]
    [SerializeField] float fadeDuration = 0.15f;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!group && panelRoot) group = panelRoot.GetComponent<CanvasGroup>();
        if (panelRoot) panelRoot.SetActive(false);  // Start: unsichtbar
        if (group) { group.alpha = 0f; group.blocksRaycasts = false; }
    }

    // ---------- Public API ----------
    public async Task Show(string label = "Loading...")
    {
        view?.SetStatus(label);
        view?.ClearProgress();

        if (panelRoot && !panelRoot.activeSelf) panelRoot.SetActive(true);
        if (group) { group.alpha = 0f; group.blocksRaycasts = true; }
        await FadeTo(1f);
    }

    public async Task Hide()
    {
        await FadeTo(0f);
        if (group) group.blocksRaycasts = false;
        if (panelRoot && panelRoot.activeSelf) panelRoot.SetActive(false);
        view?.ClearProgress();
    }

    public void SetProgress(float t01) => view?.SetProgress(t01);

    public async Task ShowWhile(Task task, string label = "Loading...")
    {
        await Show(label);
        try { await task; }
        finally { await Hide(); }
    }

    public void SetStatus(string text) => view?.SetStatus(text);

    public async Task LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, string label = "Loading...")
    {
        await Show(label);

        var op = SceneManager.LoadSceneAsync(sceneName, mode);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            SetProgress(Mathf.Clamp01(op.progress / 0.9f));
            await Task.Yield();
        }

        SetProgress(1f);
        await Task.Yield();

        op.allowSceneActivation = true;   // Szene aktivieren
        await Task.Yield();

        await Hide();
    }

    // ---------- intern ----------
    async Task FadeTo(float target)
    {
        if (!group) return;

        float start = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += (fadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeDuration);
            group.alpha = Mathf.Lerp(start, target, t);
            await Task.Yield();
        }
        group.alpha = target;
    }
}
