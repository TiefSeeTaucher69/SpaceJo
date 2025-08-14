using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class UINavigator : MonoBehaviour
{
    [Tooltip("Reihenfolge der UI-Elemente (TMP_InputField, Button, etc.). " +
             "Wenn leer, werden alle Selectables automatisch gesucht (Reihenfolge unbestimmt).")]
    public List<Selectable> order = new List<Selectable>();

    private InputAction tabNext;
    private InputAction tabPrev;

    void Awake()
    {
        // Falls keine Reihenfolge gesetzt: alle Selectables einsammeln (optional)
        if (order.Count == 0)
            order.AddRange(FindObjectsOfType<Selectable>());

        // Actions (New Input System)
        tabNext = new InputAction("TabNext", InputActionType.Button, "<Keyboard>/tab");

        tabPrev = new InputAction("TabPrev", InputActionType.Button);
        // Shift + Tab über OneModifier-Composite
        tabPrev.AddCompositeBinding("OneModifier")
               .With("Modifier", "<Keyboard>/shift")
               .With("Binding", "<Keyboard>/tab");
    }

    void OnEnable()
    {
        tabNext.performed += _ => Move(+1);
        tabPrev.performed += _ => Move(-1);
        tabNext.Enable();
        tabPrev.Enable();
    }

    void OnDisable()
    {
        tabNext.Disable();
        tabPrev.Disable();
    }

    void Move(int dir)
    {
        if (order.Count == 0) return;

        var es = EventSystem.current;
        if (es == null) return;

        Selectable current = null;
        if (es.currentSelectedGameObject)
            current = es.currentSelectedGameObject.GetComponent<Selectable>();

        // Wenn noch nichts selektiert, nimm das erste
        if (current == null)
        {
            order[0]?.Select();
            return;
        }

        int i = order.IndexOf(current);
        if (i < 0) { order[0]?.Select(); return; }

        i = (i + dir + order.Count) % order.Count;
        order[i]?.Select();
    }
}
