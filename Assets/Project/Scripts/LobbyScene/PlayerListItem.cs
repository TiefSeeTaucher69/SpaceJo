using TMPro;
using UnityEngine;

public class PlayerListItem : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text nameText;
    public TMP_Text readyText;

    /// <summary>
    /// Setzt die Texte für den Eintrag in der Lobby-Liste.
    /// </summary>
    public void Set(string displayName, bool isReady)
    {
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(displayName) ? "(Unbenannt)" : displayName;

        if (readyText != null)
            readyText.text = isReady ? "Ready" : "Not Ready";
    }
}
