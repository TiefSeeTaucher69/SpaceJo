// Projectile2D.cs
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkRigidbody2D))]
public class Projectile2D : NetworkBehaviour
{
    [Header("Balance")]
    public float speed = 30f;
    public int damage = 20;
    public float lifetime = 3f;

    [Tooltip("Offset in Grad für die Sprite-Ausrichtung: Sprite zeigt nach oben = -90, nach rechts = 0.")]
    public float rotationOffsetDegrees = -90f;

    [Header("VFX")]
    [Tooltip("Particle-Prefab für Explosion (kein NetworkObject nötig).")]
    public GameObject explosionPrefab;

    private Rigidbody2D rb;
    private Collider2D col;

    // Shooter-Identität
    private ulong shooterClientId;
    private ulong shooterObjectId;

    // Serverseitige Bewegung
    private Vector2 moveDir = Vector2.zero;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (col) col.isTrigger = true;

        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.None;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.isKinematic = !IsServer;

        // DEBUG: prüfen, ob Shooter-Daten schon da sind
        Debug.Log($"[Projectile2D.OnNetworkSpawn] IsServer={IsServer} IsSpawned={GetComponent<NetworkObject>()?.IsSpawned} " +
                  $"shooterCid={shooterClientId} shooterNo={shooterObjectId} moveDir={moveDir}");

        if (IsServer)
        {
            if (moveDir.sqrMagnitude < 0.0001f)
                moveDir = (Vector2)transform.up;

            rb.linearVelocity = moveDir * speed;
            Invoke(nameof(Despawn), lifetime);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) CancelInvoke(nameof(Despawn));
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Direkt nach Instantiate und VOR Spawn(true) aufrufen.
    /// WICHTIG: KEIN Early-Return mehr; bei unspawned Objects kann IsServer false sein.
    /// </summary>
    public void Init(Vector2 dir, ulong shooterClient, ulong shooterNoId)
    {
        // Shooter-Infos IMMER setzen (wir rufen das sowieso serverseitig auf)
        shooterClientId = shooterClient;
        shooterObjectId = shooterNoId;

        moveDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (Vector2)transform.up;

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        rb.linearVelocity = moveDir * speed;

        float angle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
        rb.SetRotation(angle);
        rb.WakeUp();

        Debug.Log($"[Projectile2D.Init] assigned shooterCid={shooterClientId} shooterNo={shooterObjectId} " +
                  $"IsServerNow={IsServer} IsSpawned={(GetComponent<NetworkObject>()?.IsSpawned ?? false)} " +
                  $"dir={moveDir}");
    }

    // Optional: 2-Param-Überladung, falls alter Aufruf irgendwo steht
    public void Init(Vector2 dir, ulong shooterClient)
    {
        Init(dir, shooterClient, 0);
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        rb.linearVelocity = moveDir * speed;

        // Rotation stabil halten
        float angle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
        rb.SetRotation(angle);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        // Robuste Trefferdaten (keine aufwendige Distanzabfrage)
        Vector3 hitPos = transform.position;
        float hitAngleZ = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg + rotationOffsetDegrees;

        // Schiff über Parent finden (deckt Child-Collider ab)
        var ship = other.GetComponentInParent<ShipControllerInputSystem>();
        if (ship)
        {
            // Self-Hit verhindern (objektbasiert)
            if (shooterObjectId == 0 || ship.NetworkObjectId != shooterObjectId)
            {
                // Killer-ClientId sicher auflösen: erst Feld, dann per NO-Owner nachschlagen
                ulong attackerCidResolved = shooterClientId;

                if (shooterObjectId != 0 &&
                    NetworkManager != null &&
                    NetworkManager.SpawnManager != null &&
                    NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(shooterObjectId, out var shooterNO) &&
                    shooterNO != null)
                {
                    attackerCidResolved = shooterNO.OwnerClientId;
                }

                Debug.Log($"[Projectile2D] HIT -> victimCid={ship.OwnerClientId} victimNo={ship.NetworkObjectId} " +
                          $"rawShooterCid={shooterClientId} rawShooterNo={shooterObjectId} resolvedShooterCid={attackerCidResolved}");

                // Direkter Server-Call + Angreifer-Objekt-ID mitschicken
                ship.ApplyDamageFromPlayerServer(damage, attackerCidResolved, shooterObjectId);
            }
            else
            {
                Debug.Log($"[Projectile2D] SELF-HIT prevented by NO check. shooterNo={shooterObjectId} victimNo={ship.NetworkObjectId}");
            }

            // Explosion zuerst (vor Despawn), lokal per ClientRpc
            PlayExplosionClientRpc(hitPos, hitAngleZ);
            Despawn();
            return;
        }

        // Asteroid / Umwelt
        if (other.CompareTag("Asteroid") || other.GetComponent<AsteroidHazard>() != null)
        {
            Debug.Log($"[Projectile2D] HIT ENV -> pos={hitPos} tag={other.tag}");
            PlayExplosionClientRpc(hitPos, hitAngleZ);
            Despawn();
        }
    }

    private void Despawn()
    {
        var no = GetComponent<NetworkObject>();
        if (IsServer && no && no.IsSpawned) no.Despawn();
        else Destroy(gameObject);
    }

    // ---------- VFX RPC ----------

    [ClientRpc]
    private void PlayExplosionClientRpc(Vector3 worldPos, float angleZ)
    {
        if (!explosionPrefab) return;
        Instantiate(explosionPrefab, worldPos, Quaternion.Euler(0f, 0f, angleZ));
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (speed < 0f) speed = 0f;
    }
#endif
}
