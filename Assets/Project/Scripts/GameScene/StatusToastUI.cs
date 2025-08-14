using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusToastUI : MonoBehaviour
{
    public static StatusToastUI I { get; private set; }

    [Header("Setup")]
    [Tooltip("Parent mit VerticalLayoutGroup o.ä.")]
    [SerializeField] private Transform listRoot;
    [Tooltip("Prefab mit TMP_Text (ein Einzel-Toast).")]
    [SerializeField] private GameObject toastPrefab;
    [Tooltip("Standard-Anzeigedauer in Sekunden.")]
    [SerializeField] private float defaultSeconds = 3f;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    public void Show(string msg, float seconds = -1f)
    {
        if (string.IsNullOrWhiteSpace(msg) || !toastPrefab || !listRoot) return;
        var go = Instantiate(toastPrefab, listRoot);
        var text = go.GetComponentInChildren<TMP_Text>();
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();

        if (text) text.text = msg;
        cg.alpha = 1f;
        StartCoroutine(FadeOutAndDestroy(go, cg, seconds > 0f ? seconds : defaultSeconds));
    }

    private IEnumerator FadeOutAndDestroy(GameObject go, CanvasGroup cg, float showSec)
    {
        yield return new WaitForSeconds(showSec);
        float t = 0f;
        const float fade = 0.25f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / fade;
            cg.alpha = 1f - t;
            yield return null;
        }
        Destroy(go);
    }
}
