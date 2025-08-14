using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class AsteroidFieldSpawner : NetworkBehaviour
{
    [Header("Prefabs & Spawnpunkte")]
    public GameObject[] asteroidPrefabs;        // Asteroiden-Prefabs (mit NetworkObject)
    public Transform[] playerSpawnPoints;       // dieselben wie beim GameSpawner

    [Header("Spawn-Area (Weltkoordinaten)")]
    public Vector2 areaCenter = Vector2.zero;                 // Mittelpunkt
    public Vector2 areaHalfExtents = new Vector2(60, 60);     // Breite/2, Höhe/2

    [Header("Mengen & Abstände")]
    public int asteroidCount = 40;
    public float minDistanceBetweenAsteroids = 6f;   // Abstand Asteroid↔Asteroid (Center–Center)
    public float minDistanceToPlayerSpawns = 10f;    // Abstand zu Player-Spawns (Center–Center)
    public int maxAttemptsPerAsteroid = 30;          // max. Versuche pro Asteroid

    [Header("Optional")]
    public bool randomRotation = true;               // nur Drehung, KEINE Skalierung

    private readonly List<Vector2> placed = new();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            SpawnField();
    }

    private void SpawnField()
    {
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0) return;

        int spawned = 0;

        for (int i = 0; i < asteroidCount; i++)
        {
            bool placedOk = false;

            for (int attempt = 0; attempt < maxAttemptsPerAsteroid; attempt++)
            {
                Vector2 pos = new Vector2(
                    Random.Range(areaCenter.x - areaHalfExtents.x, areaCenter.x + areaHalfExtents.x),
                    Random.Range(areaCenter.y - areaHalfExtents.y, areaCenter.y + areaHalfExtents.y)
                );

                if (!IsFarFromPlayers(pos) || !IsFarFromOthers(pos))
                    continue;

                // Prefab wählen & Instanz erstellen (ohne Skalierungsänderung)
                var prefab = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
                var rot = randomRotation ? Quaternion.Euler(0, 0, Random.Range(0f, 360f)) : Quaternion.identity;

                var go = Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), rot);

                // Netzwerk-Spawn
                var no = go.GetComponent<NetworkObject>();
                if (no != null) no.Spawn(true);

                placed.Add(pos);
                spawned++;
                placedOk = true;
                break;
            }

            if (!placedOk)
                continue; // kein Platz gefunden → nächster Asteroid
        }

        Debug.Log($"AsteroidFieldSpawner: {spawned}/{asteroidCount} Asteroiden gespawnt.");
    }

    private bool IsFarFromPlayers(Vector2 p)
    {
        if (playerSpawnPoints == null) return true;

        float minSqr = minDistanceToPlayerSpawns * minDistanceToPlayerSpawns;
        foreach (var t in playerSpawnPoints)
        {
            if (!t) continue;
            if (((Vector2)t.position - p).sqrMagnitude < minSqr)
                return false;
        }
        return true;
    }

    private bool IsFarFromOthers(Vector2 p)
    {
        float minSqr = minDistanceBetweenAsteroids * minDistanceBetweenAsteroids;
        for (int i = 0; i < placed.Count; i++)
        {
            if ((placed[i] - p).sqrMagnitude < minSqr)
                return false;
        }
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        var c = new Vector3(areaCenter.x, areaCenter.y, 0);
        var s = new Vector3(areaHalfExtents.x * 2f, areaHalfExtents.y * 2f, 1f);
        Gizmos.DrawCube(c, s);

        Gizmos.color = Color.yellow;
        if (playerSpawnPoints != null)
        {
            foreach (var t in playerSpawnPoints)
            {
                if (!t) continue;
                Gizmos.DrawWireSphere(t.position, minDistanceToPlayerSpawns);
            }
        }
    }
#endif
}
