using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum DeathCause : byte { Projectile = 0, Asteroid = 1, Quit = 2, Other = 3 }

/// <summary>
/// Serverseitiger Event-Hub: sammelt Spielernamen, broadcastet Statusmeldungen.
/// </summary>
public class GameEventRelay : NetworkBehaviour
{
    public static GameEventRelay Instance { get; private set; }

    private readonly Dictionary<ulong, string> _names = new();

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;

        if (Instance == this) Instance = null;
    }

    // ---------- Names ----------

    [ServerRpc(RequireOwnership = false)]
    public void RegisterNameServerRpc(string displayName, ServerRpcParams rp = default)
    {
        var cid = rp.Receive.SenderClientId;
        _names[cid] = San(displayName);
    }

    private string GetName(ulong clientId)
    {
        if (_names.TryGetValue(clientId, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;
        return $"Player {clientId}";
    }

    private static string San(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

    // ---------- Public Server helpers ----------

    // Kill (A tötet B)
    public void ServerAnnounceKill(ulong killerCid, ulong victimCid, DeathCause cause = DeathCause.Projectile)
    {
        if (!IsServer) return;
        var k = GetName(killerCid);
        var v = GetName(victimCid);
        string msg = cause == DeathCause.Projectile ? $"{k} killed {v}" : $"{k} killed {v} ({cause})";
        AnnounceClientRpc(msg);
    }

    // Suizid/Environment (B tötet sich selbst)
    public void ServerAnnounceSuicide(ulong victimCid, DeathCause cause)
    {
        if (!IsServer) return;
        var v = GetName(victimCid);
        string why = cause == DeathCause.Asteroid ? " (asteroid)" : "";
        AnnounceClientRpc($"{v} killed himself{why}");
    }

    // Spieler hat das Spiel verlassen
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        var n = GetName(clientId);
        AnnounceClientRpc($"{n} left the game");
    }

    // ---------- Broadcast ----------

    [ClientRpc]
    private void AnnounceClientRpc(string message)
    {
        if (StatusToastUI.I != null)
            StatusToastUI.I.Show(message);
        else
            Debug.Log($"[Toast] {message}");
    }
}
