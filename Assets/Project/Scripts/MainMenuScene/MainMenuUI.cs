using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;                 // Button
using TMPro;                          // TMP
using UnityEngine.SceneManagement;    // Scene-Load mit Progress
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication;

public class MainMenuUI : MonoBehaviour
{
    [Header("Main Buttons")]
    public Button hostBtn;
    public Button quickBtn;

    [Header("Join by ID UI (TMP)")]
    [Tooltip("Button im Main Menu, der das Join-Panel öffnet.")]
    public Button openJoinPanelBtn;
    [Tooltip("Panel-GO, das die ID-Eingabe enthält.")]
    public GameObject joinPanel;
    [Tooltip("TMP InputField für die Lobby-ID.")]
    public TMP_InputField lobbyIdInput;
    [Tooltip("Button im Panel: Join ausführen.")]
    public Button joinConfirmBtn;
    [Tooltip("Button im Panel: Panel schließen/abbrechen.")]
    public Button joinCancelBtn;
    [Tooltip("Optional: TMP-Text für Status/Fehlerausgaben.")]
    public TMP_Text feedbackText;

    [Header("Quit")]
    [Tooltip("Beendet das Spiel vollständig.")]
    public Button quitGameBtn;

    private bool heartbeatRunning;

    void Start()
    {
        // Basis-Buttons
        if (hostBtn) hostBtn.onClick.AddListener(() => { _ = HostLobby(); });
        if (quickBtn) quickBtn.onClick.AddListener(() => { _ = QuickJoin(); });

        // Join-by-ID UI
        if (openJoinPanelBtn) openJoinPanelBtn.onClick.AddListener(() => ToggleJoinPanel(true));
        if (joinCancelBtn) joinCancelBtn.onClick.AddListener(() => ToggleJoinPanel(false));
        if (joinConfirmBtn) joinConfirmBtn.onClick.AddListener(() => { _ = JoinById(); });

        // Quit Game
        if (quitGameBtn)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL kann nicht „quitten“ -> Button ausblenden
            quitGameBtn.gameObject.SetActive(false);
#else
            quitGameBtn.onClick.AddListener(QuitGame);
#endif
        }

        // Panel initial aus
        if (joinPanel) joinPanel.SetActive(false);
        SetFeedback(""); // clear
    }

    // ---------------- Hosting ----------------

    async Task HostLobby()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            SetFeedback("Nicht angemeldet – bitte zuerst einloggen.");
            return;
        }

        SetUiInteractable(false);

        try
        {
            // Loader AN (Phase 1: Lobby erstellen)
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Show("Lobby wird erstellt…");

            var myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

            var lobby = await LobbyService.Instance.CreateLobbyAsync(
                "SpaceRoom",
                8,
                new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "mode", new DataObject(DataObject.VisibilityOptions.Public, "deathmatch") }
                    },
                    Player = new Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            { "name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,  myName) },
                            { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") }
                        }
                    }
                });

            // Host-Heartbeat starten
            heartbeatRunning = true;
            _ = HostHeartbeat(lobby.Id);

            LobbyCache.Current = lobby;

            // Phase 2: Szene laden (Overlay bleibt sichtbar und zeigt Progress)
            await LoadSceneWhileShown("LobbyScene", "Lobby wird geladen…");
        }
        catch (LobbyServiceException ex)
        {
            SetFeedback($"Host fehlgeschlagen: {ex.Reason} ({ex.Message})");
        }
        finally
        {
            // Loader AUS (falls noch an, z. B. nach Fehler)
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            SetUiInteractable(true);
        }
    }

    // ---------------- Quick Join ----------------

    async Task QuickJoin()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            SetFeedback("Nicht angemeldet – bitte zuerst einloggen.");
            return;
        }

        SetUiInteractable(false);

        try
        {
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Show("Lobby wird gesucht…");

            var myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

            var filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
            };

            var lobby = await LobbyService.Instance.QuickJoinLobbyAsync(
                new QuickJoinLobbyOptions
                {
                    Filter = filters,
                    Player = new Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            { "name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,  myName) },
                            { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") }
                        }
                    }
                });

            LobbyCache.Current = lobby;

            await LoadSceneWhileShown("LobbyScene", "Lobby wird geladen…");
        }
        catch (LobbyServiceException ex)
        {
            SetFeedback($"Quick Join fehlgeschlagen: {ex.Reason} ({ex.Message})");
        }
        finally
        {
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            SetUiInteractable(true);
        }
    }

    // ---------------- Join by ID (TMP) ----------------

    async Task JoinById()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            SetFeedback("Nicht angemeldet – bitte zuerst einloggen.");
            return;
        }

        if (lobbyIdInput == null)
        {
            SetFeedback("Kein TMP_InputField für die Lobby-ID zugewiesen.");
            return;
        }

        var lobbyId = (lobbyIdInput.text ?? "").Trim();
        if (string.IsNullOrEmpty(lobbyId))
        {
            SetFeedback("Bitte eine gültige Lobby-ID eingeben.");
            return;
        }

        SetUiInteractable(false);
        if (joinConfirmBtn) joinConfirmBtn.interactable = false;

        try
        {
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Show("Verbinde mit Lobby…");

            var myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? "Pilot" : UgsBootstrap.DisplayName;

            var lobby = await LobbyService.Instance.JoinLobbyByIdAsync(
                lobbyId,
                new JoinLobbyByIdOptions
                {
                    Player = new Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            { "name",  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,  myName) },
                            { "ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") }
                        }
                    }
                });

            LobbyCache.Current = lobby;
            SetFeedback("");
            ToggleJoinPanel(false);

            await LoadSceneWhileShown("LobbyScene", "Lobby wird geladen…");
        }
        catch (LobbyServiceException ex)
        {
            SetFeedback($"Join fehlgeschlagen: {ex.Reason} ({ex.Message})");
        }
        finally
        {
            if (joinConfirmBtn) joinConfirmBtn.interactable = true;
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            SetUiInteractable(true);
        }
    }

    // ---------------- Quit Game (komplett beenden) ----------------

    void QuitGame()
    {
        // Falls du hier noch etwas speichern/cleanup machen willst, tu es vor dem Quit.

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- Helpers ----------------

    async Task HostHeartbeat(string lobbyId)
    {
        while (heartbeatRunning)
        {
            try { await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
            catch { /* optional loggen */ }
            await Task.Delay(15000);
        }
    }

    void ToggleJoinPanel(bool show)
    {
        if (joinPanel) joinPanel.SetActive(show);
        if (show && lobbyIdInput) lobbyIdInput.text = "";
        if (show) SetFeedback("");
    }

    void SetUiInteractable(bool enabled)
    {
        if (hostBtn) hostBtn.interactable = enabled;
        if (quickBtn) quickBtn.interactable = enabled;
        if (openJoinPanelBtn) openJoinPanelBtn.interactable = enabled;
        if (joinConfirmBtn) joinConfirmBtn.interactable = enabled;
        if (joinCancelBtn) joinCancelBtn.interactable = enabled;
        if (quitGameBtn) quitGameBtn.interactable = enabled;
    }

    void SetFeedback(string msg)
    {
        if (feedbackText) feedbackText.text = msg ?? "";
        if (!string.IsNullOrEmpty(msg))
            Debug.Log(msg);
    }

    void OnDisable()
    {
        heartbeatRunning = false;
    }

    // --- Szene mit bereits sichtbarem Overlay laden (echter Progress) ---
    private async Task LoadSceneWhileShown(string sceneName, string labelWhileLoading = null)
    {
        if (LoadingOverlay.I != null && !string.IsNullOrEmpty(labelWhileLoading))
            LoadingOverlay.I.SetProgress(0f); // reset

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            if (LoadingOverlay.I != null)
                LoadingOverlay.I.SetProgress(Mathf.Clamp01(op.progress / 0.9f));
            await Task.Yield();
        }

        if (LoadingOverlay.I != null) LoadingOverlay.I.SetProgress(1f);
        await Task.Yield();

        op.allowSceneActivation = true;
        await Task.Yield();
    }
}

public static class LobbyCache
{
    public static Lobby Current;
}
