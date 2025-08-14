using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public class UgsBootstrap : MonoBehaviour
{
    public static bool IsReady;
    public static string DisplayName;

    async void Start()
    {
        if (IsReady) return;

        var options = new InitializationOptions();

#if UNITY_EDITOR
        string profile = "EditorMain";
#if PARRELSYNC
        // ParrelSync: jedem Clone ein eigenes Profil geben
        if (ParrelSync.ClonesManager.IsClone())
        {
            // Optional: irgendwas Eindeutiges aus ParrelSync nehmen
            var arg = ParrelSync.ClonesManager.GetArgument(); // z.B. "1", "2", ...
            profile = $"Clone_{arg}";
        }
#endif
        options.SetProfile(profile);
        Debug.Log($"[UGS] Init with profile: {profile}");
#endif

        await UnityServices.InitializeAsync(options);
        IsReady = true;
    }
}
