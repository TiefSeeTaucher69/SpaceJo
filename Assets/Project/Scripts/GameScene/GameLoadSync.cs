using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class GameLoadSync : NetworkBehaviour
{
    // <<< NEU: globale Gate-Flag
    public static bool InputsAllowed { get; private set; } = false;

    private readonly HashSet<ulong> readyClients = new();
    private int expectedPlayers = -1;
    private bool allReadySent = false;

    public override void OnNetworkSpawn()
    {
        InputsAllowed = false; // Start: gesperrt

        // Overlay bei allen zeigen
        if (LoadingOverlay.I != null)
        {
            _ = LoadingOverlay.I.Show("Spiel wird geladen…");
            LoadingOverlay.I.SetProgress(0f);
        }

        if (IsServer)
        {
            expectedPlayers = ComputeExpectedPlayersFromLobby();
            if (expectedPlayers < 1)
                expectedPlayers = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

            MarkReady(NetworkManager.ServerClientId);

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            BroadcastProgress();
            TryComplete();
        }

        if (IsClient)
        {
            _ = NotifyReadyWhenStable();
        }
    }

    public override void OnNetworkDespawn()
    {
        InputsAllowed = false; // Szene verlässt -> wieder sperren
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    // -------- Server-seitig --------
    private int ComputeExpectedPlayersFromLobby()
    {
        int exp = -1;
        var lob = LobbyCache.Current;
        if (lob != null && lob.Players != null && lob.Players.Count > 0)
        {
            int readyCount = 0;
            foreach (var p in lob.Players)
            {
                if (p.Data != null && p.Data.TryGetValue("ready", out var d) && d.Value == "1")
                    readyCount++;
            }
            exp = readyCount > 0 ? readyCount : lob.Players.Count;
        }
        return exp;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (LobbyCache.Current == null || LobbyCache.Current.Players == null || LobbyCache.Current.Players.Count == 0)
            expectedPlayers = Mathf.Max(expectedPlayers, NetworkManager.ConnectedClientsIds.Count);

        BroadcastProgress();
        TryComplete();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        readyClients.Remove(clientId);
        if (LobbyCache.Current == null || LobbyCache.Current.Players == null || LobbyCache.Current.Players.Count == 0)
            expectedPlayers = Mathf.Min(expectedPlayers, NetworkManager.ConnectedClientsIds.Count);

        BroadcastProgress();
        TryComplete();
    }

    private void MarkReady(ulong clientId)
    {
        if (!readyClients.Contains(clientId))
            readyClients.Add(clientId);
    }

    private void BroadcastProgress()
    {
        int total = Mathf.Max(1, expectedPlayers);
        float p = Mathf.Clamp01(readyClients.Count / (float)total);
        ProgressClientRpc(p, (ushort)readyClients.Count, (ushort)total);
    }

    private void TryComplete()
    {
        if (allReadySent) return;

        if (NetworkManager.ConnectedClientsIds.Count < expectedPlayers)
            return;

        if (readyClients.Count >= expectedPlayers)
        {
            allReadySent = true;
            InputsAllowed = true;    // <<< Server gibt frei
            AllReadyClientRpc();
        }
    }

    // -------- RPCs --------
    [ClientRpc]
    private void ProgressClientRpc(float progress01, ushort ready, ushort total)
    {
        if (LoadingOverlay.I != null)
        {
            LoadingOverlay.I.SetProgress(progress01);
            LoadingOverlay.I.SetStatus($"Warten auf Spieler {ready}/{total}…");
        }
    }

    [ClientRpc]
    private void AllReadyClientRpc()
    {
        InputsAllowed = true;        // <<< Client gibt frei
        _ = HideOverlaySoon();
    }

    // -------- Client-seitig --------
    private async Task NotifyReadyWhenStable()
    {
        await Task.Yield();
        await Task.Yield();
        NotifyClientReadyServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyClientReadyServerRpc(ServerRpcParams rp = default)
    {
        MarkReady(rp.Receive.SenderClientId);
        BroadcastProgress();
        TryComplete();
    }

    private async Task HideOverlaySoon()
    {
        if (LoadingOverlay.I != null)
        {
            LoadingOverlay.I.SetProgress(1f);
            await LoadingOverlay.I.Hide();
        }
    }
}
