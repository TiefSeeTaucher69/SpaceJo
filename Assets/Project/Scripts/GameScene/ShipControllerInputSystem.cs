// ShipControllerInputSystem.cs
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkRigidbody2D))]
public class ShipControllerInputSystem : NetworkBehaviour
{
    [Header("Movement & Shooting")]
    public float thrust = 10f;
    public float turnSpeed = 180f;
    [Tooltip("Wird automatisch auf ein Child namens 'muzzle' gesetzt, wenn leer.")]
    public Transform muzzle;
    public GameObject projectilePrefab;

    [Header("Speed & Brake")]
    public float maxSpeed = 20f;
    public float brakePower = 40f;

    [Header("Health")]
    public int maxHp = 100;
    public float respawnDelay = 3f;

    [Header("Knockback")]
    public float knockbackDuration = 0.3f;

    [Header("VFX")]
    [Tooltip("Particle-Prefab für Muzzle Flash (kein NetworkObject nötig).")]
    public GameObject muzzleFlashPrefab;
    [Tooltip("Offset des Muzzle-Flash entlang der Schussrichtung (kann negativ sein).")]
    public float muzzleFlashOffset = -0.2f; // näher am Schiff

    private bool inKnockback = false;
    private float knockbackTimer = 0f;

    private bool isDead = false; // sperrt Eingaben/Feuer während Tod

    private Rigidbody2D rb;
    private SpaceshipControls controls;
    private Vector2 moveInput;
    private bool fireInput;

    private readonly NetworkVariable<int> hp = new(writePerm: NetworkVariableWritePermission.Server);
    private Vector3 spawnPos;
    private Quaternion spawnRot;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        AutoAssignMuzzle();
        spawnPos = transform.position;
        spawnRot = transform.rotation;
    }

    void AutoAssignMuzzle()
    {
        if (muzzle != null) return;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(t.name, "muzzle", System.StringComparison.OrdinalIgnoreCase))
            { muzzle = t; break; }
        }
        if (muzzle == null)
        {
            var go = new GameObject("muzzle");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.8f;
            muzzle = go.transform;
#if UNITY_EDITOR
            Debug.LogWarning($"{name}: Kein 'muzzle' gefunden – automatisch erstellt.", this);
#endif
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) hp.Value = maxHp;

        rb.simulated = IsServer;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.isKinematic = !IsServer;

        // Name für Killfeed registrieren
        if (IsOwner && GameEventRelay.Instance != null)
        {
            var myName = string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName) ? $"Player {OwnerClientId}" : UgsBootstrap.DisplayName;
            GameEventRelay.Instance.RegisterNameServerRpc(myName);
        }

        if (IsOwner)
        {
            if (controls == null) controls = new SpaceshipControls();
            controls.Gameplay.Move.performed += OnMovePerformed;
            controls.Gameplay.Move.canceled += OnMoveCanceled;
            controls.Gameplay.Fire.performed += OnFirePerformed;
            controls.Gameplay.Fire.canceled += OnFireCanceled;
            controls.Enable();
        }
    }

    public override void OnNetworkDespawn() { TeardownInput(); base.OnNetworkDespawn(); }
    public override void OnDestroy() { TeardownInput(); base.OnDestroy(); }
    private void TeardownInput()
    {
        if (controls != null)
        {
            controls.Gameplay.Move.performed -= OnMovePerformed;
            controls.Gameplay.Move.canceled -= OnMoveCanceled;
            controls.Gameplay.Fire.performed -= OnFirePerformed;
            controls.Gameplay.Fire.canceled -= OnFireCanceled;
            if (IsOwner) controls.Disable();
        }
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;
    private void OnFirePerformed(InputAction.CallbackContext ctx) => fireInput = true;
    private void OnFireCanceled(InputAction.CallbackContext ctx) => fireInput = false;

    void Update()
    {
        if (inKnockback)
        {
            knockbackTimer -= Time.deltaTime;
            if (knockbackTimer <= 0f) inKnockback = false;
        }
    }

    void FixedUpdate()
    {
        if (IsOwner)
        {
            if (!GameLoadSync.InputsAllowed || PauseMenuUI.IsOpen)
            {
                fireInput = false;
                MoveServerRpc(Vector2.zero, false);
                return;
            }

            MoveServerRpc(moveInput, fireInput);
            fireInput = false;
        }
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void MoveServerRpc(Vector2 input, bool fire)
    {
        if (!GameLoadSync.InputsAllowed) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; return; }
        if (inKnockback || isDead) return;

        // Turn
        float turn = -input.x;
        rb.MoveRotation(rb.rotation + turn * turnSpeed * Time.fixedDeltaTime);

        // Thrust / Brake
        if (input.y < 0f)
        {
            if (rb.linearVelocity.sqrMagnitude > 0.0001f)
            {
                float decel = brakePower * Time.fixedDeltaTime;
                rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, decel);
            }
        }
        else if (input.y > 0f)
        {
            rb.AddForce(transform.up * input.y * thrust, ForceMode2D.Force);
        }

        if (rb.linearVelocity.magnitude > maxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;

        if (fire) FireServer();
    }

    // --- Schießen (nur Server) ---
    private void FireServer()
    {
        if (!GameLoadSync.InputsAllowed || isDead) return;
        if (projectilePrefab == null) return;

        Vector2 dirWorld = muzzle ? (Vector2)muzzle.up : (Vector2)(Quaternion.Euler(0, 0, rb.rotation) * Vector3.up);
        if (dirWorld.sqrMagnitude < 0.0001f) dirWorld = Vector2.up;
        dirWorld.Normalize();

        float offDeg = 0f;
        if (projectilePrefab.TryGetComponent<Projectile2D>(out var projCfg))
            offDeg = projCfg.rotationOffsetDegrees;

        float angle = Mathf.Atan2(dirWorld.y, dirWorld.x) * Mathf.Rad2Deg + offDeg;

        // Projektil-Spawn
        const float spawnOffset = 0.6f;
        Vector3 spawnPos = (muzzle ? muzzle.position : transform.position) + (Vector3)(dirWorld * spawnOffset);
        Quaternion spawnRot = Quaternion.Euler(0f, 0f, angle);

        var go = Instantiate(projectilePrefab, spawnPos, spawnRot);

        if (go.TryGetComponent<Rigidbody2D>(out var prb))
            prb.SetRotation(angle);

        // IDs des Schützen
        var shooterNO = GetComponent<NetworkObject>();
        ulong shooterCid = shooterNO ? shooterNO.OwnerClientId : OwnerClientId;
        ulong shooterNoId = shooterNO ? shooterNO.NetworkObjectId : 0;

        Debug.Log($"[Ship.FireServer] spawn projectile by shooterCid={shooterCid} shooterNo={shooterNoId}");

        if (go.TryGetComponent<Projectile2D>(out var proj))
            proj.Init(dirWorld, shooterCid, shooterNoId);

        // Eigene Collider ignorieren
        if (go.TryGetComponent<Collider2D>(out var projCol))
        {
            var ownerCols = GetComponentsInChildren<Collider2D>(true);
            foreach (var c in ownerCols) if (c) Physics2D.IgnoreCollision(projCol, c, true);
        }

        var no = go.GetComponent<NetworkObject>();
        if (no != null) no.Spawn(true);
        else Debug.LogError("Projectile Prefab hat kein NetworkObject!", projectilePrefab);

        // Muzzle-Flash (frei in der Welt, kein Parenting)
        Vector3 muzzleFxPos = (muzzle ? muzzle.position : transform.position) + (Vector3)(dirWorld * muzzleFlashOffset);
        PlayMuzzleFlashClientRpc(shooterNoId, muzzleFxPos, angle);
    }

    // --------- Schaden / Tod ---------

    // Direkter Server-Entry inkl. Angreifer-Objekt-ID
    public void ApplyDamageFromPlayerServer(int dmg, ulong attackerClientId, ulong attackerObjectId)
    {
        if (!IsServer) return;

        Debug.Log($"[Ship.ApplyDamageFromPlayerServer] victimCid={OwnerClientId} victimNo={GetComponent<NetworkObject>()?.NetworkObjectId} dmg={dmg} attackerCid={attackerClientId} attackerNo={attackerObjectId}");

        ApplyDamageServer(dmg, attackerClientId, DeathCause.Projectile, attackerObjectId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ApplyDamageServerRpc(int dmg)
    {
        Debug.Log($"[Ship.ApplyDamageServerRpc] victimCid={OwnerClientId} dmg={dmg} cause=Other");
        ApplyDamageServer(dmg, null, DeathCause.Other, null);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ApplyDamageFromPlayerServerRpc(int dmg, ulong attackerClientId)
    {
        Debug.Log($"[Ship.ApplyDamageFromPlayerServerRpc] victimCid={OwnerClientId} dmg={dmg} attackerCid={attackerClientId} (no attackerNoId via this RPC)");
        ApplyDamageServer(dmg, attackerClientId, DeathCause.Projectile, null);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ApplyDamageFromEnvironmentServerRpc(int dmg, int causeCode)
    {
        Debug.Log($"[Ship.ApplyDamageFromEnvironmentServerRpc] victimCid={OwnerClientId} dmg={dmg} cause={(DeathCause)causeCode}");
        ApplyDamageServer(dmg, null, (DeathCause)causeCode, null);
    }

    private void ApplyDamageServer(int dmg, ulong? attackerCid, DeathCause cause, ulong? attackerNoId)
    {
        if (!IsServer) return;
        if (hp.Value <= 0) return;

        int oldHp = hp.Value;
        hp.Value = Mathf.Max(0, hp.Value - dmg);

        Debug.Log($"[Ship.ApplyDamageServer] victimCid={OwnerClientId} victimNo={GetComponent<NetworkObject>()?.NetworkObjectId} oldHp={oldHp} newHp={hp.Value} " +
                  $"attCid={(attackerCid.HasValue ? attackerCid.Value.ToString() : "null")} attNo={(attackerNoId.HasValue ? attackerNoId.Value.ToString() : "null")} cause={cause}");

        if (hp.Value == 0)
        {
            isDead = true;

            // Nur dem betroffenen Client das Death-Overlay zeigen
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };
            ShowDeathOverlayClientRpc(respawnDelay, target);

            if (GameEventRelay.Instance != null)
            {
                var myNO = GetComponent<NetworkObject>();
                ulong victimNoId = myNO ? myNO.NetworkObjectId : 0;

                bool hasAttackerNo = attackerNoId.HasValue && attackerNoId.Value != 0;
                bool isSuicide;

                // 1) Projektil: Objekt-ID entscheidet
                if (cause == DeathCause.Projectile && hasAttackerNo)
                {
                    isSuicide = (attackerNoId.Value == victimNoId);
                }
                else
                {
                    // 2) Fallback: ClientId-Vergleich
                    isSuicide = !(attackerCid.HasValue && attackerCid.Value != OwnerClientId);
                }

                Debug.Log($"[Ship.DeathDecision] victimCid={OwnerClientId} victimNo={victimNoId} " +
                          $"attCid={(attackerCid.HasValue ? attackerCid.Value.ToString() : "null")} " +
                          $"attNo={(attackerNoId.HasValue ? attackerNoId.Value.ToString() : "null")} " +
                          $"cause={cause} -> isSuicide={isSuicide}");

                if (!isSuicide && attackerCid.HasValue)
                    GameEventRelay.Instance.ServerAnnounceKill(attackerCid.Value, OwnerClientId, cause);
                else
                    GameEventRelay.Instance.ServerAnnounceSuicide(OwnerClientId, cause);
            }

            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        // Sicht/Hitbox aus
        SetActiveClientRpc(false);
        yield return new WaitForSeconds(respawnDelay);

        // Server setzt Zustand zurück
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.WakeUp();

        hp.Value = maxHp;
        isDead = false;

        // Owner wieder aktivieren und Overlay sicherheitshalber schließen
        SetActiveClientRpc(true);

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
        HideDeathOverlayClientRpc(target);
    }

    [ClientRpc]
    private void SetActiveClientRpc(bool active)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = active;
        foreach (var c in GetComponentsInChildren<Collider2D>(true)) c.enabled = active;
    }

    public void ApplyKnockbackServer(Vector2 velocity)
    {
        if (!IsServer) return;
        if (!GameLoadSync.InputsAllowed || isDead) return;
        rb.linearVelocity = velocity;
        rb.WakeUp();
        KnockbackClientRpc();
    }

    [ClientRpc]
    private void KnockbackClientRpc()
    {
        inKnockback = true;
        knockbackTimer = knockbackDuration;
    }

    // ---------- VFX RPCs ----------

    [ClientRpc]
    private void PlayMuzzleFlashClientRpc(ulong shooterNoId, Vector3 worldPos, float angleZ)
    {
        if (!muzzleFlashPrefab) return;
        var rot = Quaternion.Euler(0f, 0f, angleZ);
        Instantiate(muzzleFlashPrefab, worldPos, rot);
    }

    // ---------- Death Overlay RPCs (nur Ziel-Client) ----------

    [ClientRpc]
    private void ShowDeathOverlayClientRpc(float duration, ClientRpcParams target = default)
    {
        if (DeathOverlay.I != null)
            DeathOverlay.I.ShowFor(duration);
    }

    [ClientRpc]
    private void HideDeathOverlayClientRpc(ClientRpcParams target = default)
    {
        if (DeathOverlay.I != null)
            DeathOverlay.I.HideImmediate();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (muzzle == null)
            Debug.LogWarning($"{name}: Feld 'muzzle' ist leer. Child 'muzzle' wird zur Laufzeit gesucht/erstellt.", this);
    }
#endif
}
