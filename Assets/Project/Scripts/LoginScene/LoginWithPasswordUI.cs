using UnityEngine;
using UnityEngine.UI;
using TMPro; // TMP_InputField, TMP_Text
using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;

public class LoginWithPasswordUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Panel mit allen manuellen Loginfeldern (im Inspector DEAKTIVIERT lassen!).")]
    public GameObject loginPanel;   // Deaktiviert im Inspector, nur zeigen wenn nötig
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;
    public Button signUpBtn;
    public Button signInBtn;
    public TMP_Text feedback;

    const string DisplayNameKey = "display_name";

    async void Awake()
    {
        // Panel sicherheitshalber aus
        if (loginPanel) loginPanel.SetActive(false);

        // Passwortfeld maskieren
        if (passwordField != null)
        {
            passwordField.contentType = TMP_InputField.ContentType.Password;
            passwordField.ForceLabelUpdate();
        }

        // ggf. gespeicherten Anzeigenamen vorfüllen
        var saved = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            UgsBootstrap.DisplayName = saved;
            if (usernameField && string.IsNullOrWhiteSpace(usernameField.text))
                usernameField.text = saved;
        }

        // Globalen Loading Screen zeigen
        if (LoadingOverlay.I != null)
            await LoadingOverlay.I.Show("Prüfe Anmeldung …");

        bool goToMenu = false; // -> nach finally wird Szene geladen

        try
        {
            await UnityServices.InitializeAsync();

            // Silent Sign-In mit gecachter Session
            if (AuthenticationService.Instance.SessionTokenExists)
            {
                try
                {
                    // Anonymous reaktiviert ggf. bestehende anonyme Session
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    EnsureDisplayNameFallback();
                    goToMenu = true;
                }
                catch
                {
                    SetFeedback("Sitzung abgelaufen – bitte erneut einloggen.");
                }
            }

            // Falls bereits eingeloggt (z. B. aus Editor)
            if (!goToMenu && AuthenticationService.Instance.IsSignedIn)
            {
                EnsureDisplayNameFallback();
                goToMenu = true;
            }
        }
        finally
        {
            // Overlay IMMER schließen bevor wir irgendwas anzeigen/wechseln
            if (LoadingOverlay.I != null)
                await LoadingOverlay.I.Hide();
        }

        if (goToMenu)
        {
            // Jetzt ist das Overlay sicher weg -> Menü laden
            SceneLoader.I.Load(AppScene.MainMenuScene);
            return;
        }

        // Manueller Login nötig
        if (loginPanel) loginPanel.SetActive(true);
        SetBusy(false);
        FocusUserField();
    }

    void Start()
    {
        if (signUpBtn) signUpBtn.onClick.AddListener(() => { _ = DoSignUp(); });
        if (signInBtn) signInBtn.onClick.AddListener(() => { _ = DoSignIn(); });
    }

    async Task DoSignUp()
    {
        try
        {
            string u = usernameField ? usernameField.text.Trim() : "";
            string p = passwordField ? passwordField.text : "";

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                SetFeedback("Bitte Benutzername und Passwort eingeben.");
                return;
            }

            SetBusy(true);
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Show("Erstelle Account …");

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            await AuthenticationService.Instance.AddUsernamePasswordAsync(u, p);

            AuthenticationService.Instance.SignOut();
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(u, p);

            // Overlay erst schließen, dann Szene wechseln (damit es nicht „hängen“ bleibt)
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            OnSignedIn(u);
        }
        catch (RequestFailedException e) { SetFeedback($"Registrierung fehlgeschlagen: {e.Message}"); }
        catch (System.Exception e) { SetFeedback($"Fehler: {e.Message}"); }
        finally
        {
            SetBusy(false);
            // Falls oben kein Erfolg war, Overlay schließen
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
        }
    }

    async Task DoSignIn()
    {
        try
        {
            string u = usernameField ? usernameField.text.Trim() : "";
            string p = passwordField ? passwordField.text : "";

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                SetFeedback("Bitte Benutzername und Passwort eingeben.");
                return;
            }

            SetBusy(true);
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Show("Anmeldung …");

            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(u, p);

            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            OnSignedIn(u);
        }
        catch (RequestFailedException e) { SetFeedback($"Login fehlgeschlagen: {e.Message}"); }
        catch (System.Exception e) { SetFeedback($"Fehler: {e.Message}"); }
        finally
        {
            SetBusy(false);
            // Falls oben kein Erfolg -> Overlay schließen
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
        }
    }

    // -------- Helpers --------

    void OnSignedIn(string enteredUsername)
    {
        var dn = string.IsNullOrWhiteSpace(enteredUsername) ? "Pilot" : enteredUsername;
        UgsBootstrap.DisplayName = dn;
        PlayerPrefs.SetString(DisplayNameKey, dn);

        // KEIN erneutes Show() hier – damit bleibt nichts hängen
        SceneLoader.I.Load(AppScene.MainMenuScene);
    }

    void EnsureDisplayNameFallback()
    {
        if (!string.IsNullOrWhiteSpace(UgsBootstrap.DisplayName)) return;

        var dn = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (string.IsNullOrWhiteSpace(dn) && usernameField) dn = usernameField.text.Trim();
        if (string.IsNullOrWhiteSpace(dn)) dn = "Pilot";

        UgsBootstrap.DisplayName = dn;
        PlayerPrefs.SetString(DisplayNameKey, dn);
    }

    void SetBusy(bool busy)
    {
        if (signUpBtn) signUpBtn.interactable = !busy;
        if (signInBtn) signInBtn.interactable = !busy;
        if (usernameField) usernameField.interactable = !busy;
        if (passwordField) passwordField.interactable = !busy;
    }

    void SetFeedback(string msg)
    {
        if (feedback) feedback.text = msg ?? "";
        if (!string.IsNullOrEmpty(msg))
            Debug.Log(msg);
    }

    void FocusUserField()
    {
        if (usernameField)
        {
            usernameField.Select();
            usernameField.ActivateInputField();
        }
    }
}
