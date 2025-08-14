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

    /// <summary>Nur SERVER: direkt nach Instantiate und VOR Spawn(true) aufrufen.</summary>
    public void Init(Vector2 dir, ulong shooterClient, ulong shooterNoId)
    {
        if (!IsServer) return;

        shooterClientId = shooterClient;
        shooterObjectId = shooterNoId;

        moveDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (Vector2)transform.up;

        rb.linearVelocity = moveDir * speed;

        float angle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
        rb.SetRotation(angle);
        rb.WakeUp();
    }

    // optional: Überladung falls irgendwo noch 2-Param-Aufrufe existieren
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

        // Schiff über Parent finden (deckt Child-Collider ab)
        var ship = other.GetComponentInParent<ShipControllerInputSystem>();
        if (ship)
        {
            // Kein Schaden am Schützen selbst (objektbasiert)
            if (ship.NetworkObjectId != shooterObjectId)
                ship.ApplyDamageFromPlayerServerRpc(damage, shooterClientId);

            Despawn();
            return;
        }

        if (other.CompareTag("Asteroid") || other.GetComponent<AsteroidHazard>() != null)
        {
            Despawn();
        }
    }

    private void Despawn()
    {
        var no = GetComponent<NetworkObject>();
        if (IsServer && no && no.IsSpawned) no.Despawn();
        else Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (speed < 0f) speed = 0f;
    }
#endif
}
