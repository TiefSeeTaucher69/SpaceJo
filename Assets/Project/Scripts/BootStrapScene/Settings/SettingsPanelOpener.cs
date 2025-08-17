// Assets/Project/Scripts/BootStrapScene/Settings/SettingsPanelOpener.cs
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SettingsPanelOpener : MonoBehaviour
{
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private bool closeWithShortcut = true;

#if ENABLE_INPUT_SYSTEM
    [Header("Input (New Input System)")]
    // ESC auf Keyboard
    [SerializeField] private string keyboardCloseBinding = "<Keyboard>/escape";
    // B (Xbox) / Kreis (PlayStation) = buttonEast (generisch)
    [SerializeField] private string gamepadCloseBindingGeneric = "<Gamepad>/buttonEast";
    // Fallback (einige Mappings benutzen noch '/b')
    [SerializeField] private string gamepadCloseBindingLegacy = "<Gamepad>/b";
    // Start/Options (optional auch zum Schlieﬂen)
    [SerializeField] private string gamepadMenuBinding = "<Gamepad>/start";

    private InputAction closeAction;
#endif

    public void OpenSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
    }

#if ENABLE_INPUT_SYSTEM
    private void OnEnable()
    {
        if (!closeWithShortcut) return;

        closeAction = new InputAction("CloseSettings", InputActionType.Button);

        if (!string.IsNullOrEmpty(keyboardCloseBinding))
            closeAction.AddBinding(keyboardCloseBinding);

        // B / Kreis (generisch + legacy)
        if (!string.IsNullOrEmpty(gamepadCloseBindingGeneric))
            closeAction.AddBinding(gamepadCloseBindingGeneric);
        if (!string.IsNullOrEmpty(gamepadCloseBindingLegacy))
            closeAction.AddBinding(gamepadCloseBindingLegacy);

        // Start/Options (optional auch schlieﬂen)
        if (!string.IsNullOrEmpty(gamepadMenuBinding))
            closeAction.AddBinding(gamepadMenuBinding);

        closeAction.performed += OnClosePerformed;
        closeAction.Enable();
    }

    private void OnDisable()
    {
        if (closeAction != null)
        {
            closeAction.performed -= OnClosePerformed;
            closeAction.Disable();
            closeAction.Dispose();
            closeAction = null;
        }
    }

    private void OnClosePerformed(InputAction.CallbackContext ctx)
    {
        // nur schlieﬂen, wenn Panel offen ist
        if (settingsPanel && settingsPanel.activeSelf)
            CloseSettings();
    }
#else
    // Falls du "Both" im Player Setting nutzt, kˆnntest du hier optional
    // ein Legacy-GetKeyDown lassen. Bei reinem neuen System bleibt das leer.
#endif
}
