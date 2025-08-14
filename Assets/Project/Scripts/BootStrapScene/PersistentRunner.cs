using UnityEngine;
using Unity.Netcode;

public class PersistentRunner : MonoBehaviour
{
    private void Awake()
    {
        // Nur ein Exemplar erlauben
        var runners = FindObjectsOfType<PersistentRunner>();
        if (runners.Length > 1) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
    }
}
