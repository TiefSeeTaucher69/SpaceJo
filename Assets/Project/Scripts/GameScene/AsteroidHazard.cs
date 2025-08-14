using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class AsteroidHazard : NetworkBehaviour
{
    [Header("Damage")]
    public int baseDamage = 20;
    [Tooltip("Cooldown pro Spieler (s), damit der gleiche Asteroid nicht in jedem Physikframe trifft.")]
    public float hitCooldown = 0.5f;
    public float minImpactSpeedForDamage = 2f;

    [Header("Bounce")]
    [Tooltip("Wie stark der Abpraller ist (1 = perfekt elastisch, >1 stärker, <1 schwächer).")]
    public float bounceMultiplier = 1.05f;
    [Tooltip("Unter diesem Speed kein Bounce/Knockback (verhindert Mini-Stupser).")]
    public float minImpactSpeedForBounce = 0.2f;

    private readonly Dictionary<ulong, float> lastHitTime = new();

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = false;
        gameObject.tag = "Asteroid";
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (!IsServer) return; // nur Server macht Gameplay-Logik

        if (!c.collider.TryGetComponent<ShipControllerInputSystem>(out var ship))
            return;

        var rb = ship.GetComponent<Rigidbody2D>();
        if (rb == null || c.contactCount == 0) return;

        // aktuelle Velocity und Kollisionsnormale
        Vector2 v = rb.linearVelocity;
        Vector2 n = c.GetContact(0).normal; // zeigt vom Asteroiden weg

        float speed = v.magnitude;
        if (speed < minImpactSpeedForBounce) return; // sehr langsam → ignorieren

        // Bounce-Richtung (Reflexion) berechnen
        Vector2 reflected = Vector2.Reflect(v, n) * bounceMultiplier;

        // Server setzt physikalisch direkt (Server-Authority)
        rb.linearVelocity = reflected;

        // Schiff über Knockback informieren (Clients bekommen Sperre via RPC)
        ship.ApplyKnockbackServer(reflected);

        // Schaden nur wenn schnell genug UND Cooldown vorbei
        if (speed >= minImpactSpeedForDamage)
        {
            ulong id = ship.OwnerClientId;
            float now = Time.time;
            if (!lastHitTime.TryGetValue(id, out var last) || (now - last) >= hitCooldown)
            {
                lastHitTime[id] = now;

                // optional leicht mit Speed skalieren
                int dmg = Mathf.RoundToInt(baseDamage * Mathf.Clamp01((speed - minImpactSpeedForDamage) / 5f));
                if (dmg <= 0) dmg = baseDamage;

                // >>> Umweltschaden mit Cause "Asteroid" für Killfeed/Toast
                ship.ApplyDamageFromEnvironmentServerRpc(dmg, (int)DeathCause.Asteroid);
            }
        }
    }
}
