// UIRaycastDebugger.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class UIRaycastDebugger : MonoBehaviour
{
    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
#else
        if (Input.GetMouseButtonDown(0))
#endif
        {
            if (EventSystem.current == null) return;
            var data = new PointerEventData(EventSystem.current)
            {
                position =
#if ENABLE_INPUT_SYSTEM
                Mouse.current != null ? (Vector2)Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition
#else
                Input.mousePosition
#endif
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);

            var msg = results.Count == 0 ? "—" : string.Join(" > ", results.ConvertAll(r => r.gameObject.name));
            Debug.Log($"[UIRaycast] under cursor: {msg}");
        }
    }
}
