using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameSpawner : NetworkBehaviour
{
    [Header("Settings")]
    public GameObject playerShipPrefab;
    public Transform[] spawnPoints;

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
    }

    // Wird aufgerufen, wenn ein Ladeschritt für eine Szene abgeschlossen ist (alle Clients, die es geschafft haben)
    private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;
        if (sceneName != SceneManager.GetActiveScene().name) return; // optional: auf "GameScene" prüfen

        foreach (var clientId in clientsCompleted)
        {
            TrySpawnFor(clientId);
        }
    }

    // Für Late Joiner, die nach dem Spawnen noch beitreten
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        // nur spawnen, wenn wir uns bereits in der GameScene befinden
        // (sonst übernimmt OnLoadEventCompleted das Spawnen beim Szenenwechsel)
        TrySpawnFor(clientId);
    }

    private void TrySpawnFor(ulong clientId)
    {
        // Doppel-Spawn vermeiden: hat der Client schon ein PlayerObject?
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var nc))
        {
            if (nc.PlayerObject != null && nc.PlayerObject.IsSpawned)
                return;
        }

        // Spawnpunkt wählen (einfach rundrobin/random)
        var spawn = GetSpawnPointFor(clientId);

        var go = Instantiate(playerShipPrefab, spawn.position, spawn.rotation);
        var no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("PlayerShipPrefab hat kein NetworkObject!");
            Destroy(go);
            return;
        }

        // WICHTIG: registriertes Prefab in NetworkPrefabs nötig
        no.SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    private Transform GetSpawnPointFor(ulong clientId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        // deterministische Auswahl
        int idx = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[idx];
    }
}
