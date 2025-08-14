using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public class LobbyUI : MonoBehaviour
{
    [Header("UI")]
    public Toggle readyToggle;
    public Button startBtn;
    public Transform playerListContainer;
    public GameObject playerListItemPrefab;

    [Header("Lobby Info (TMP)")]
    [Tooltip("TMP_Text, das die Lobby-ID anzeigt.")]
    public TMP_Text lobbyIdText;
    [Tooltip("Button, der die Lobby-ID in die Zwischenablage kopiert.")]
    public Button copyLobbyIdBtn;
    [Tooltip("Optionales TMP_Text für kurze Hinweise wie 'Kopiert!' (leer lassen, wenn nicht benötigt).")]
    public TMP_Text infoText;

    [Header("Exit")]
    [Tooltip("Button: zurück ins Hauptmenü (Host: Lobby schließen, Client: verlassen).")]
    public Button leaveToMenuBtn;

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "GameScene";

    private Lobby lobby;
    private bool joiningRelay = false;
    private bool leaving = false;
    private const string RelayProtocol = "dtls"; // oder "udp", aber bei beiden Seiten gleich!

    private bool IsHost => lobby != null && lobby.HostId == AuthenticationService.Instance.PlayerId;

    void Awake()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback += id => Debug.Log($"[NM] ClientConnected: {id}");
            nm.OnClientDisconnectCallback += id => Debug.Log($"[NM] ClientDisconnected: {id}");
        }
    }

    async void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[LobbyUI] Kein NetworkManager gefunden.");
            return;
        }

        nm.NetworkConfig.EnableSceneManagement = true;
        nm.NetworkConfig.ConnectionApproval = false;
        nm.ConnectionApprovalCallback = null;

        Debug.Log($"[LobbyUI] NM @ {nm.gameObject.name} | IsServer={nm.IsServer} IsClient={nm.IsClient} IsHost={nm.IsHost}");
        Debug.Log($"[LobbyUI] SceneManagement={nm.NetworkConfig.EnableSceneManagement}  Approval={nm.NetworkConfig.ConnectionApproval}");

        lobby = LobbyCache.Current;
        if (lobby == null)
        {
            Debug.LogWarning("[LobbyUI] Keine Lobby im Cache – zurück zum MainMenu.");
            SceneLoader.I.Load(AppScene.MainMenuScene);
            return;
        }

        // UI-Listener
        if (readyToggle != null)
            readyToggle.onValueChanged.AddListener(async v => await OnReadyChanged(v));
        if (startBtn != null)
            startBtn.onClick.AddListener(async () => await HostStartGame());
        if (copyLobbyIdBtn != null)
            copyLobbyIdBtn.onClick.AddListener(CopyLobbyIdToClipboard);
        if (leaveToMenuBtn != null)
            leaveToMenuBtn.onClick.AddListener(() => { _ = LeaveToMenu(); });

        if (startBtn != null) startBtn.gameObject.SetActive(IsHost);

        await EnsureOwnNameInPlayerData();

        if (readyToggle != null) readyToggle.isOn = GetOwnReadyFromLobby();

        // Lobby-ID anzeigen
        RefreshLobbyHeader();

        RefreshPlayerList();

        Debug.Log("[LobbyUI] Polling gestartet.");
        InvokeRepeating(nameof(PollLobby), 1f, 1f);
    }

    // ---- Lobby Header / ID-Anzeige ----
    void RefreshLobbyHeader()
    {
        if (lobbyIdText != null)
        {
            lobbyIdText.text = lobby != null ? lobby.Id : "-";
        }
        if (infoText != null) infoText.text = ""; // Hinweis zurücksetzen
    }

    void CopyLobbyIdToClipboard()
    {
        if (lobby == null) return;

        GUIUtility.systemCopyBuffer = lobby.Id;
        if (infoText != null)
        {
            infoText.text = "Lobby-ID kopiert!";
            CancelInvoke(nameof(ClearInfo));
            Invoke(nameof(ClearInfo), 2f);
        }
        Debug.Log($"[LobbyUI] Lobby-ID in Zwischenablage kopiert: {lobby.Id}");
    }

    void ClearInfo()
    {
        if (infoText != null) infoText.text = "";
    }

    bool GetOwnReadyFromLobby()
    {
        if (lobby == null) return false;
        string myId = AuthenticationService.Instance.PlayerId;
        foreach (var p in lobby.Players)
        {
            if (p.Id != myId) continue;
            if (p.Data != null && p.Data.TryGetValue("ready", out var rd))
                return rd.Value == "1";
        }
        return false;
    }

    async Task EnsureOwnNameInPlayerData()
    {
        if (lobby == null) return;

        string myId = AuthenticationService.Instance.PlayerId;
        string myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

        Player me = null;
        foreach (var p in lobby.Players)
            if (p.Id == myId) { me = p; break; }

        bool needsUpdate = (me == null) ||
                           (me.Data == null) ||
                           !me.Data.TryGetValue("name", out var nd) ||
                           string.IsNullOrWhiteSpace(nd.Value) ||
                           nd.Value != myName;

        if (!needsUpdate) return;

        try
        {
            await LobbyService.Instance.UpdatePlayerAsync(
                lobby.Id,
                myId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "name", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, myName) }
                    }
                });

            lobby = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
            LobbyCache.Current = lobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"[LobbyUI] Name-Set fehlgeschlagen: {e.Message}");
        }
    }

    async void PollLobby()
    {
        if (lobby == null) { CancelInvoke(nameof(PollLobby)); return; }

        try
        {
            lobby = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
            LobbyCache.Current = lobby;

            // ID aktualisieren (falls gewechselt)
            RefreshLobbyHeader();

            RefreshPlayerList();

            if (!IsHost && !joiningRelay && lobby.Data != null &&
                lobby.Data.TryGetValue("joinCode", out var code) &&
                !string.IsNullOrEmpty(code.Value))
            {
                joiningRelay = true;
                Debug.Log($"[LobbyUI Client] JoinCode entdeckt: {code.Value} → Join Relay");
                CancelInvoke(nameof(PollLobby));
                await ClientJoinRelay(code.Value);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyUI] Lobby-Refresh Fehler: {e.Message}");
            CancelInvoke(nameof(PollLobby));
        }
    }

    void RefreshPlayerList()
    {
        if (playerListContainer == null) return;

        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        if (lobby == null) return;

        foreach (var p in lobby.Players)
        {
            var go = Instantiate(playerListItemPrefab, playerListContainer);
            string name = SafeGetName(p);
            bool ready = SafeGetReady(p);

            var item = go.GetComponent<PlayerListItem>();
            if (item != null)
            {
                item.Set(name, ready);
            }
            else
            {
                var texts = go.GetComponentsInChildren<TMP_Text>();
                if (texts.Length > 0) texts[0].text = name;
                if (texts.Length > 1) texts[1].text = ready ? "Ready" : "Not Ready";
            }
        }
    }

    string SafeGetName(Player p)
    {
        if (p?.Data != null && p.Data.TryGetValue("name", out var nameData) && !string.IsNullOrWhiteSpace(nameData.Value))
            return nameData.Value;

        if (p != null && p.Id == AuthenticationService.Instance.PlayerId)
            return string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

        return p?.Id ?? "(Unbekannt)";
    }

    bool SafeGetReady(Player p)
    {
        if (p?.Data != null && p.Data.TryGetValue("ready", out var readyData))
            return readyData.Value == "1";
        return false;
    }

    async Task OnReadyChanged(bool isReady)
    {
        if (lobby == null) return;

        try
        {
            string myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

            await LobbyService.Instance.UpdatePlayerAsync(
                lobby.Id,
                AuthenticationService.Instance.PlayerId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady ? "1" : "0") },
                        { "name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,  myName) }
                    }
                });
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyUI] OnReadyChanged Fehler: {e.Message}");
        }
    }

    async Task HostStartGame()
    {
        if (!IsHost || lobby == null) return;

        foreach (var p in lobby.Players)
        {
            if (p.Data == null || !p.Data.TryGetValue("ready", out var d) || d.Value != "1")
            {
                Debug.Log("[LobbyUI Host] Nicht alle Spieler sind ready.");
                return;
            }
        }

        // --- NEU: Overlay sofort anzeigen; Schließen übernimmt GameLoadSync in GameScene
        if (LoadingOverlay.I != null)
        {
            await LoadingOverlay.I.Show("Spiel startet…");
            LoadingOverlay.I.SetProgress(0f);
        }

        Debug.Log("[LobbyUI Host] Erstelle Relay-Allocation…");
        var alloc = await RelayService.Instance.CreateAllocationAsync(lobby.MaxPlayers - 1);
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
        Debug.Log($"[LobbyUI Host] JoinCode: {joinCode}");

        await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
        {
            Data = new Dictionary<string, DataObject> {
                { "joinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
            }
        });
        Debug.Log("[LobbyUI Host] JoinCode in Lobby gespeichert.");

        var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        var relayData = AllocationUtils.ToRelayServerData(alloc, RelayProtocol);
        transport.SetRelayServerData(relayData);

        NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = false;
        NetworkManager.Singleton.ConnectionApprovalCallback = null;

        NetworkManager.Singleton.StartHost();
        Debug.Log($"[LobbyUI Host] Lade Network-Scene: {gameSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        // Kein Hide() hier – GameLoadSync blendet bei ALL READY aus.
    }

    async Task ClientJoinRelay(string joinCode)
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // --- NEU: Overlay anzeigen; Schließen übernimmt GameLoadSync in GameScene
        if (LoadingOverlay.I != null)
        {
            await LoadingOverlay.I.Show("Verbinde mit Spiel…");
            LoadingOverlay.I.SetProgress(0f);
        }

        try
        {
            Debug.Log($"[LobbyUI Client] Join Allocation… (code={joinCode})");
            var joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            var relayData = AllocationUtils.ToRelayServerData(joinAlloc, RelayProtocol);
            transport.SetRelayServerData(relayData);

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = false;
            NetworkManager.Singleton.ConnectionApprovalCallback = null;

            Debug.Log("[LobbyUI Client] StartClient()…");
            if (!NetworkManager.Singleton.StartClient())
            {
                Debug.LogError("[LobbyUI Client] StartClient fehlgeschlagen!");
                if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide(); // Fehlerfall -> nicht hängen bleiben
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback += id =>
            {
                if (id == NetworkManager.Singleton.LocalClientId)
                    Debug.Log("[LobbyUI Client] Erfolgreich mit Host verbunden");
            };
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyUI Client] Relay-Join Fehler: {e.Message}");
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide(); // Fehlerfall
        }
    }

    // --- Verlassen/Schließen & zurück ins Menü ---
    async Task LeaveToMenu()
    {
        if (leaving) return;
        leaving = true;

        if (leaveToMenuBtn) leaveToMenuBtn.interactable = false;
        CancelInvoke(nameof(PollLobby));

        try
        {
            if (lobby != null)
            {
                if (IsHost)
                {
                    Debug.Log("[LobbyUI] Host schließt Lobby …");
                    await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                }
                else
                {
                    Debug.Log("[LobbyUI] Client verlässt Lobby …");
                    await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"[LobbyUI] Leave/Close Lobby: {e.Message}");
            // Fallback: weiter räumen und ins Menü
        }
        finally
        {
            LobbyCache.Current = null;

            // Networking sauber stoppen, falls bereits aktiv (Client/Host/Server)
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                try { nm.Shutdown(); }
                catch { /* ignore */ }
            }

            // Zurück ins Hauptmenü
            SceneLoader.I.Load(AppScene.MainMenuScene);
        }
    }

    void OnDisable()
    {
        CancelInvoke(nameof(PollLobby));
        Debug.Log("[LobbyUI] Polling gestoppt (OnDisable).");
    }
}
