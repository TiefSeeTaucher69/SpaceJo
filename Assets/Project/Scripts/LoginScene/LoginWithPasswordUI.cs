using UnityEngine;
using UnityEngine.UI;
using TMPro; // TMP_InputField, TMP_Text
using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;

public class LoginWithPasswordUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;
    public Button signUpBtn;
    public Button signInBtn;
    public TMP_Text feedback;
    [Tooltip("Optional: Spinner/Overlay während Auto-Login/Requests.")]
    public GameObject busyOverlay;

    const string DisplayNameKey = "display_name";

    async void Awake()
    {
        if (passwordField != null)
        {
            passwordField.contentType = TMP_InputField.ContentType.Password;
            passwordField.ForceLabelUpdate();
        }

        // evtl. gespeicherten Anzeigenamen vorfüllen
        var saved = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            UgsBootstrap.DisplayName = saved;
            if (usernameField && string.IsNullOrWhiteSpace(usernameField.text))
                usernameField.text = saved;
        }

        await UnityServices.InitializeAsync();

        // ---- Silent Sign-In mit gecachter Session ----
        if (AuthenticationService.Instance.SessionTokenExists)
        {
            SetBusy(true);
            try
            {
                // NEU: cached player via Anonymous-SignIn reaktivieren
                await AuthenticationService.Instance.SignInAnonymouslyAsync(); // nutzt das gespeicherte Session-Token
                EnsureDisplayNameFallback();
                SceneLoader.I.Load(AppScene.MainMenuScene);
                return;
            }
            catch
            {
                feedback.text = "Sitzung abgelaufen – bitte erneut einloggen.";
            }
            finally
            {
                SetBusy(false);
            }
        }

        // Falls aus irgendeinem Grund schon eingeloggt, direkt weiter
        if (AuthenticationService.Instance.IsSignedIn)
        {
            EnsureDisplayNameFallback();
            SceneLoader.I.Load(AppScene.MainMenuScene);
        }
    }

    void Start()
    {
        signUpBtn.onClick.AddListener(() => { _ = DoSignUp(); });
        signInBtn.onClick.AddListener(() => { _ = DoSignIn(); });
    }

    async Task DoSignUp()
    {
        try
        {
            string u = usernameField.text.Trim();
            string p = passwordField.text;

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                feedback.text = "Bitte Benutzername und Passwort eingeben.";
                return;
            }

            SetBusy(true);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            await AuthenticationService.Instance.AddUsernamePasswordAsync(u, p);

            AuthenticationService.Instance.SignOut();
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(u, p);

            OnSignedIn(u);
        }
        catch (RequestFailedException e) { feedback.text = $"Registrierung fehlgeschlagen: {e.Message}"; }
        catch (System.Exception e) { feedback.text = $"Fehler: {e.Message}"; }
        finally { SetBusy(false); }
    }

    async Task DoSignIn()
    {
        try
        {
            string u = usernameField.text.Trim();
            string p = passwordField.text;

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                feedback.text = "Bitte Benutzername und Passwort eingeben.";
                return;
            }

            SetBusy(true);
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(u, p);

            OnSignedIn(u);
        }
        catch (RequestFailedException e) { feedback.text = $"Login fehlgeschlagen: {e.Message}"; }
        catch (System.Exception e) { feedback.text = $"Fehler: {e.Message}"; }
        finally { SetBusy(false); }
    }

    // -------- Helpers --------

    void OnSignedIn(string enteredUsername)
    {
        var dn = string.IsNullOrWhiteSpace(enteredUsername) ? "Pilot" : enteredUsername;
        UgsBootstrap.DisplayName = dn;
        PlayerPrefs.SetString(DisplayNameKey, dn);

        // Session-Token wird vom SDK automatisch persistiert -> nächster Start autologin.
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
        if (busyOverlay) busyOverlay.SetActive(busy);
    }
}
