// Assets/Project/Scripts/GameScene/GameEventRelay.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GameEventRelay : NetworkBehaviour
{
    public static GameEventRelay Instance;

    private readonly Dictionary<ulong, string> _nameByCid = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
        }
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        base.OnNetworkDespawn();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RegisterNameServerRpc(string displayName, ServerRpcParams rpc = default)
    {
        var cid = rpc.Receive.SenderClientId;
        var clean = Sanitize(displayName);
        _nameByCid[cid] = clean;
        Debug.Log($"[GameEventRelay] RegisterName cid={cid} name='{clean}'");
    }

    // --- Host-Leaving-Broadcast (graceful) ---
    public void ServerBroadcastHostLeaving()
    {
        if (!IsServer) return;
        Debug.Log("[GameEventRelay] ServerBroadcastHostLeaving -> HostLeavingClientRpc()");
        HostLeavingClientRpc();
    }

    [ClientRpc]
    private void HostLeavingClientRpc()
    {
        Debug.Log("[GameEventRelay] HostLeavingClientRpc received.");
        HostLeaveManager.I?.OnHostLeavingRpcReceived();
    }

    // --- Status/Killfeed ---
    public void ServerAnnounceKill(ulong attackerCid, ulong victimCid, DeathCause cause)
    {
        if (!IsServer) return;
        string killer = GetName(attackerCid);
        string victim = GetName(victimCid);
        Debug.Log($"[GameEventRelay] KILL -> killerCid={attackerCid}('{killer}') victimCid={victimCid}('{victim}') cause={cause}");
        BroadcastToastClientRpc($"{killer} killed {victim}");
    }

    public void ServerAnnounceSuicide(ulong victimCid, DeathCause cause)
    {
        if (!IsServer) return;
        string victim = GetName(victimCid);
        Debug.Log($"[GameEventRelay] SUICIDE -> victimCid={victimCid}('{victim}') cause={cause}");
        BroadcastToastClientRpc($"{victim} killed himself");
    }

    public void ServerAnnounceLeft(ulong cid)
    {
        if (!IsServer) return;
        Debug.Log($"[GameEventRelay] LEFT -> cid={cid}('{GetName(cid)}')");
        BroadcastToastClientRpc($"{GetName(cid)} left the game");
    }

    [ClientRpc]
    private void BroadcastToastClientRpc(string line)
    {
        if (StatusToastUI.I != null) StatusToastUI.I.Show(line);
        else Debug.Log($"[Toast] {line}");
    }

    private void OnClientDisconnect(ulong cid)
    {
        if (_nameByCid.ContainsKey(cid))
            BroadcastToastClientRpc($"{_nameByCid[cid]} left the game");

        _nameByCid.Remove(cid);
    }

    private string GetName(ulong cid)
    {
        if (_nameByCid.TryGetValue(cid, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;
        return cid == 0 ? "Host" : $"Player {cid}";
    }

    private static string Sanitize(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
}
