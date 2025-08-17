using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Diagnostics;
using System.IO;

public class BootUpdateManager : MonoBehaviour
{
    [Header("Versionseinstellungen")]
    private string currentVersion;
    [Tooltip("GitHub API Endpoint für die neueste Release")]
    public string apiUrl = "https://api.github.com/repos/TiefSeeTaucher69/SpaceJo/releases/latest";

    [Header("UI")]
    [Tooltip("Panel mit Update-Hinweisen und Buttons")]
    public GameObject updatePanel;
    public Text updateText;
    public Button updateButton;
    public Button skipButton;
    [Tooltip("Text für Release Notes")]
    public Text releaseNotesText;

    [Header("Szenen")]
    [Tooltip("Wird geladen, wenn kein Update anliegt oder nach Skip")]
    public string loginSceneName = "LoginScene";

    private string installerUrl = "";
    private string installerFilePath = "";

    void Start()
    {
        UnityEngine.Debug.Log("Aktueller Build: " + Application.version);

        currentVersion = "v" + Application.version;

        if (updatePanel) updatePanel.SetActive(false);

        StartCoroutine(CheckForUpdate());
    }

    IEnumerator CheckForUpdate()
    {
        var request = UnityWebRequest.Get(apiUrl);
        request.SetRequestHeader("User-Agent", "UnityUpdateChecker");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var json = request.downloadHandler.text;
            UnityEngine.Debug.Log("GitHub API Antwort erhalten.");

            GitHubRelease latest = JsonUtility.FromJson<GitHubRelease>(json);

            UnityEngine.Debug.Log($"Neueste Version: {latest.tag_name}, Aktuelle Version: {currentVersion}");

            if (IsNewerVersion(latest.tag_name, currentVersion) && latest.assets != null && latest.assets.Length > 0)
            {
                installerUrl = latest.assets[0].browser_download_url;

                if (updateText) updateText.text = $"Ein neues Update ({latest.tag_name}) ist verfügbar!";
                if (releaseNotesText) releaseNotesText.text = latest.body;
                if (updatePanel) updatePanel.SetActive(true);

                if (updateButton)
                {
                    updateButton.onClick.RemoveAllListeners();
                    updateButton.onClick.AddListener(() => StartCoroutine(DownloadAndInstall()));
                }

                if (skipButton)
                {
                    skipButton.onClick.RemoveAllListeners();
                    skipButton.onClick.AddListener(() =>
                    {
                        if (updatePanel) updatePanel.SetActive(false);
                        LoadLoginScene();
                    });
                }

                UnityEngine.Debug.Log("Update-Panel angezeigt");
            }
            else
            {
                // Kein Update -> immer LoginScene
                LoadLoginScene();
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("Update-Prüfung fehlgeschlagen: " + request.error);
            // Bei Fehler ebenfalls direkt zur LoginScene
            LoadLoginScene();
        }
    }

    IEnumerator DownloadAndInstall()
    {
        if (updateText) updateText.text = "Lädt neue Version, Spiel NICHT manuell schließen...";

        string tempPath = Path.Combine(Application.persistentDataPath, "UpdateInstaller.exe");
        installerFilePath = tempPath;

        var request = UnityWebRequest.Get(installerUrl);
        request.downloadHandler = new DownloadHandlerFile(tempPath);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            UnityEngine.Debug.Log("Installer heruntergeladen, starte Installation...");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerFilePath,
                    UseShellExecute = true,
                    Verb = "runas" // Adminrechte anfordern
                });
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("Konnte Installer nicht starten: " + e.Message);
            }

            Application.Quit();
        }
        else
        {
            UnityEngine.Debug.LogError("Download fehlgeschlagen: " + request.error);
            // Optional: zurück zur LoginScene oder Retry anbieten
            LoadLoginScene();
        }
    }

    private void LoadLoginScene()
    {
        if (string.IsNullOrWhiteSpace(loginSceneName))
        {
            UnityEngine.Debug.LogError("[BootUpdateManager] loginSceneName ist leer. Szene wird nicht geladen.");
            return;
        }

        UnityEngine.Debug.Log($"[BootUpdateManager] Lade Szene: {loginSceneName}");
        SceneManager.LoadScene(loginSceneName);
    }

    [System.Serializable]
    public class GitHubRelease
    {
        public string tag_name;
        public string body;
        public Asset[] assets;
    }

    [System.Serializable]
    public class Asset
    {
        public string browser_download_url;
    }

    private bool IsNewerVersion(string latest, string current)
    {
        latest = string.IsNullOrEmpty(latest) ? "" : latest.TrimStart('v');
        current = string.IsNullOrEmpty(current) ? "" : current.TrimStart('v');

        if (System.Version.TryParse(latest, out var latestVersion) &&
            System.Version.TryParse(current, out var currentVersion))
        {
            return latestVersion > currentVersion;
        }

        UnityEngine.Debug.LogWarning("Versionsvergleich fehlgeschlagen!");
        return false;
    }
}
