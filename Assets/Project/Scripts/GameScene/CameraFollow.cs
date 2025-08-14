using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem; // nötig

[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Follow")]
    public Vector3 offset = new Vector3(0, 0, -10);
    public float smoothSpeed = 5f;

    [Header("Zoom")]
    public float zoomSpeed = 8f;
    public float minZoom = 3f;
    public float maxZoom = 15f;

    private Transform target;
    private Camera cam;
    private SpaceshipControls controls;
    private float zoomAxis; // -1..+1 (Mausrad liefert kleine Werte, Gamepad Trigger 0..1)

    void Awake()
    {
        cam = GetComponent<Camera>();
        controls = new SpaceshipControls();
    }

    void OnEnable()
    {
        // Zoom als kontinuierlicher Wert lesen
        controls.Gameplay.Zoom.performed += ctx => zoomAxis = ctx.ReadValue<float>();
        controls.Gameplay.Zoom.canceled += _ => zoomAxis = 0f;
        controls.Enable();
    }

    void OnDisable()
    {
        controls?.Disable();
    }

    void LateUpdate()
    {
        // Ziel (eigener Spieler) finden
        if (target == null)
        {
            foreach (var ship in FindObjectsOfType<ShipControllerInputSystem>())
            {
                if (ship.IsOwner) { target = ship.transform; break; }
            }
        }

        // Follow
        if (target != null)
        {
            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        }

        // Zoom (orthographic)
        if (cam.orthographic)
        {
            // Maus-Scroll ist sehr klein → etwas verstärken
            float delta = zoomAxis * zoomSpeed * Time.deltaTime * 10f;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - delta, minZoom, maxZoom);
        }
        else
        {
            // Falls du doch eine Perspektivkamera nutzt (selten bei 2D)
            float delta = zoomAxis * zoomSpeed * Time.deltaTime * 100f;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - delta, 20f, 90f);
        }
    }
}
