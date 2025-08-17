using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // Neues Input System
using Unity.Netcode;

public class PauseMenuUI : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Panel, das als Pause-Menü angezeigt wird (im Inspector deaktiviert lassen!).")]
    public GameObject panel;
    [Tooltip("Optional: Standard-Button der beim Öffnen fokussiert wird (z.B. Quit).")]
    public Selectable firstSelected;
    [Tooltip("Button, der ins Hauptmenü zurückkehrt.")]
    public Button quitButton;
    [Tooltip("Optionaler Button zum Fortsetzen (falls vorhanden).")]
    public Button resumeButton;

    [Header("Cursor")]
    public bool showCursorWhileOpen = true;

    public static bool IsOpen { get; private set; }

    void Awake()
    {
        if (panel) panel.SetActive(false);
        IsOpen = false;

        if (quitButton) quitButton.onClick.AddListener(OnQuit);
        if (resumeButton) resumeButton.onClick.AddListener(Close);
    }

    void Update()
    {
        if (PressedToggle())
        {
            if (IsOpen) Close();
            else Open();
        }
    }

    bool PressedToggle()
    {
        // Keyboard ESC
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;

        // Controller "Start"
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
            return true;

        return false;
    }

    public void Open()
    {
        if (panel) panel.SetActive(true);
        IsOpen = true;

        if (showCursorWhileOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        // Fokus setzen
        if (firstSelected)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            firstSelected.Select();
        }
    }

    public void Close()
    {
        if (panel) panel.SetActive(false);
        IsOpen = false;

        // Optional: Fokus zurücksetzen
        EventSystem.current?.SetSelectedGameObject(null);
    }

    public void OnQuit()
    {
        // Sauber Netzwerksession beenden, egal ob Host/Client
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            try { nm.Shutdown(); } catch { /* ignore */ }
        }

        // Panel schließen und ins Hauptmenü zurück
        Close();
        SceneLoader.I.Load(AppScene.MainMenuScene);
    }
}
