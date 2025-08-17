using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;

public class LoginWithPasswordUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject loginPanel;   // im Inspector deaktiviert lassen
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;
    public Button signUpBtn;
    public Button signInBtn;
    public TMP_Text feedback;

    const string DisplayNameKey = "display_name";
    const string LOG = "[LoginWithPasswordUI]";

    // Schutz gegen parallele Restores (über Szenenobjekte hinweg)
    private static Task<bool> s_RestoreTask;
    private static bool s_RestoreResult;

    async void Awake()
    {
        var id = GetInstanceID();
        if (loginPanel) loginPanel.SetActive(false);

        if (passwordField != null)
        {
            passwordField.contentType = TMP_InputField.ContentType.Password;
            passwordField.ForceLabelUpdate();
        }

        // evtl. Anzeigename vorfüllen
        var saved = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            UgsBootstrap.DisplayName = saved;
            if (usernameField && string.IsNullOrWhiteSpace(usernameField.text))
                usernameField.text = saved;
        }

        if (LoadingOverlay.I != null)
            await LoadingOverlay.I.Show("Prüfe Anmeldung …");

        bool restoredOk = false;

        try
        {
            await UnityServices.InitializeAsync();

            // Wenn "IsSignedIn=true" aber kein Token -> harte Bereinigung
            if (!AuthenticationService.Instance.SessionTokenExists && AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"{LOG}#{id} IsSignedIn=true aber kein SessionToken -> SignOut()");
                AuthenticationService.Instance.SignOut();
            }

            // Deduplicate: nur **ein** Restore-Task gleichzeitig
            if (s_RestoreTask == null)
            {
                s_RestoreTask = TryRestoreSessionInternal(id);
                s_RestoreResult = await s_RestoreTask;
                s_RestoreTask = null;
            }
            else
            {
                // auf laufenden Restore warten
                s_RestoreResult = await s_RestoreTask;
            }

            restoredOk = s_RestoreResult;
            Debug.Log($"{LOG}#{id} After Restore: restoredOk={restoredOk}, IsSignedIn={AuthenticationService.Instance.IsSignedIn}, HasToken={AuthenticationService.Instance.SessionTokenExists}");
        }
        finally
        {
            if (LoadingOverlay.I != null)
                await LoadingOverlay.I.Hide();
        }

        if (restoredOk && AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log($"{LOG}#{id} Auto-Login OK -> lade MainMenu.");
            SceneLoader.I.Load(AppScene.MainMenuScene);
            return;
        }

        // Sicherstellen, dass wir in einem klaren "nicht eingeloggt"-Zustand sind
        if (AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log($"{LOG}#{id} Restore scheiterte, aber IsSignedIn==true -> SignOut() (Konsistenz).");
            AuthenticationService.Instance.SignOut();
        }

        // manueller Login
        if (loginPanel) loginPanel.SetActive(true);
        SetBusy(false);
        FocusUserField();

        Debug.Log($"{LOG}#{id} Zeige Login-Panel. IsSignedIn={AuthenticationService.Instance.IsSignedIn}, HasToken={AuthenticationService.Instance.SessionTokenExists}");
    }

    void Start()
    {
        if (signUpBtn) signUpBtn.onClick.AddListener(() => { _ = DoSignUp(); });
        if (signInBtn) signInBtn.onClick.AddListener(() => { _ = DoSignIn(); });
    }

    // ---------- Restore intern, idempotent ----------
    private async Task<bool> TryRestoreSessionInternal(int id)
    {
        // Bereits eingeloggt? -> Erfolg (idempotent)
        if (AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log($"{LOG}#{id} Restore: schon eingeloggt -> OK");
            EnsureDisplayNameFallback();
            return true;
        }

        if (!AuthenticationService.Instance.SessionTokenExists)
        {
            Debug.Log($"{LOG}#{id} Kein SessionToken -> kein Restore.");
            return false;
        }

        try
        {
            // Unity Auth: ruft bei vorhandenem Token die Session wieder her
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            EnsureDisplayNameFallback();
            Debug.Log($"{LOG}#{id} Silent restore OK (anonymous reactivated).");
            return true;
        }
        catch (RequestFailedException e)
        {
            // Wenn wir trotz Exception am Ende eingeloggt sind (race/parallel): Erfolg
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"{LOG}#{id} Restore warf '{e.Message}', aber IsSignedIn==true -> OK");
                EnsureDisplayNameFallback();
                return true;
            }

            SetFeedback("Sitzung abgelaufen – bitte erneut einloggen.");
            Debug.Log($"{LOG}#{id} Silent restore FAILED -> SignOut()");
            AuthenticationService.Instance.SignOut();
            return false;
        }
        catch (System.Exception e)
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"{LOG}#{id} Restore Exception '{e.Message}', aber IsSignedIn==true -> OK");
                EnsureDisplayNameFallback();
                return true;
            }

            SetFeedback("Sitzung abgelaufen – bitte erneut einloggen.");
            Debug.Log($"{LOG}#{id} Silent restore FAILED (Exception) -> SignOut()");
            AuthenticationService.Instance.SignOut();
            return false;
        }
    }

    // ---------- Sign Up ----------
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

            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
            OnSignedIn(u);
        }
        catch (RequestFailedException e) { SetFeedback($"Registrierung fehlgeschlagen: {e.Message}"); }
        catch (System.Exception e) { SetFeedback($"Fehler: {e.Message}"); }
        finally
        {
            SetBusy(false);
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
        }
    }

    // ---------- Sign In ----------
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
            if (LoadingOverlay.I != null) await LoadingOverlay.I.Hide();
        }
    }

    // -------- Helpers --------

    void OnSignedIn(string enteredUsername)
    {
        var dn = string.IsNullOrWhiteSpace(enteredUsername) ? "Pilot" : enteredUsername;
        UgsBootstrap.DisplayName = dn;
        PlayerPrefs.SetString(DisplayNameKey, dn);
        Debug.Log($"{LOG} SignIn erfolgreich -> lade MainMenu.");
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
