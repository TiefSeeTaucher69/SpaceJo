// Assets/Project/Scripts/Common/HostLeaveManager.cs
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HostLeaveManager : MonoBehaviour
{
    public static HostLeaveManager I { get; private set; }

    [Header("Config")]
    [Tooltip("Name der Szene, die als Main Menu geladen werden soll.")]
    [SerializeField] private string mainMenuSceneName = "MainMenuScene";

    [Tooltip("Kurze Verzögerung (Sek.) bei 'graceful leave', damit RPCs rausgehen.")]
    [SerializeField] private float hostNotifyDelay = 0.25f;

    private bool isQuittingToMenu;

    // --- Autostart: sicherstellen, dass der Manager immer existiert ---
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<HostLeaveManager>() != null) return;
        var go = new GameObject("[HostLeaveManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<HostLeaveManager>();
        Debug.Log("[HostLeaveManager] Auto-created at startup.");
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnServerStopped += OnServerStopped;
        }
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
        }
    }

    void Update()
    {
        // Zusätzlicher Fallback (z.B. harte Transportfehler):
        // Wenn wir Client sind und nicht mehr verbunden -> Menü.
        var nm = NetworkManager.Singleton;
        if (!isQuittingToMenu && nm != null && nm.IsClient && !nm.IsConnectedClient)
        {
            Debug.Log("[HostLeaveManager] Update detected: not connected to host anymore.");
            GoToMenu("Connection to host lost");
        }
    }

    // Wird auch bei Alt+F4/Window-Close aufgerufen (sofern kein Crash/Task-Kill)
    void OnApplicationQuit()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsHost)
        {
            // Höflichkeits-RPC; falls das OS direkt schließt, fängt der Client den Disconnect-Fallback ab.
            try
            {
                Debug.Log("[HostLeaveManager] OnApplicationQuit on Host -> broadcast HostLeaving.");
                GameEventRelay.Instance?.ServerBroadcastHostLeaving();
            }
            catch { /* ignore */ }
        }
    }

    // --- Public API (für Buttons etc., funktioniert aber nicht nur dann) ---
    public static void HostQuitToMenu()
    {
        if (I == null) { Debug.LogWarning("[HostLeaveManager] Instance missing."); return; }
        I.StartCoroutine(I.HostQuitFlow());
    }

    public void OnHostLeavingRpcReceived()
    {
        Debug.Log("[HostLeaveManager] HostLeavingClientRpc received on client.");
        GoToMenu("Host left the session");
    }

    // --- Network Callbacks ---
    private void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsClient && !nm.IsServer && clientId == nm.LocalClientId)
        {
            Debug.Log("[HostLeaveManager] OnClientDisconnect: server (host) went away.");
            GoToMenu("Disconnected from host");
        }
    }

    private void OnServerStopped(bool _)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsHost)
        {
            Debug.Log("[HostLeaveManager] OnServerStopped on Host -> go to menu.");
            GoToMenu("Server stopped");
        }
    }

    // --- Flows ---
    private IEnumerator HostQuitFlow()
    {
        if (isQuittingToMenu) yield break;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost)
        {
            Debug.Log("[HostLeaveManager] HostQuitFlow called (not host) -> local menu.");
            GoToMenu("Leaving session");
            yield break;
        }

        if (GameEventRelay.Instance != null && GameEventRelay.Instance.IsServer)
        {
            Debug.Log("[HostLeaveManager] Broadcasting HostLeavingClientRpc to clients...");
            GameEventRelay.Instance.ServerBroadcastHostLeaving();
        }
        else
        {
            Debug.LogWarning("[HostLeaveManager] GameEventRelay missing or not server; skip broadcast.");
        }

        // kurze Gnadenfrist, damit RPC die Clients erreicht
        yield return new WaitForSecondsRealtime(hostNotifyDelay);

        GoToMenu("You left the session (Host)");
    }

    private void GoToMenu(string reason)
    {
        if (isQuittingToMenu) return;
        isQuittingToMenu = true;

        Debug.Log($"[HostLeaveManager] GoToMenu. Reason='{reason}'");

        if (StatusToastUI.I != null) StatusToastUI.I.Show(reason, 2f);

        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsClient || nm.IsServer))
        {
            Debug.Log("[HostLeaveManager] Shutting down Netcode...");
            nm.Shutdown();
        }

        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.Log($"[HostLeaveManager] Loading menu scene '{mainMenuSceneName}'...");
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning("[HostLeaveManager] mainMenuSceneName not set.");
        }
    }
}
