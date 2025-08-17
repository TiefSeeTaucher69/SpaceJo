using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIControllerSelectedHandlerGameSettings : MonoBehaviour
{
    [Header("Fokussteuerung")]
    public GameObject firstSelected;

    private GameObject lastSelected;

    [Header("Feste Navigationsreihenfolge")]
    public Selectable[] navigationOrder;

    void OnEnable()
    {
        SetupFixedNavigation();

        if (firstSelected != null && firstSelected.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(firstSelected);
            lastSelected = firstSelected;
        }
    }

    void Update()
    {
        GameObject current = EventSystem.current.currentSelectedGameObject;

        if (current != null)
        {
            lastSelected = current;
        }

        if (current == null && IsUsingController())
        {
            if (lastSelected != null && lastSelected.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(lastSelected);
            }
            else if (firstSelected != null && firstSelected.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(firstSelected);
                lastSelected = firstSelected;
            }
        }
    }

    private bool IsUsingController()
    {
        return Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f ||
               Mathf.Abs(Input.GetAxis("Vertical")) > 0.1f ||
               Input.GetButtonDown("Submit") || Input.GetButtonDown("Cancel");
    }

    private void SetupFixedNavigation()
    {
        for (int i = 0; i < navigationOrder.Length; i++)
        {
            if (navigationOrder[i] == null) continue;

            Navigation nav = navigationOrder[i].navigation;
            nav.mode = Navigation.Mode.Explicit;

            nav.selectOnDown = (i + 1 < navigationOrder.Length) ? navigationOrder[i + 1] : null;
            nav.selectOnUp = (i - 1 >= 0) ? navigationOrder[i - 1] : null;

            nav.selectOnLeft = null;
            nav.selectOnRight = null;

            navigationOrder[i].navigation = nav;
        }
    }
}
