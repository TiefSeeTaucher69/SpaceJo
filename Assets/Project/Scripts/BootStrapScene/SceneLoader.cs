using UnityEngine;
using UnityEngine.SceneManagement;

public enum AppScene { BootstrapScene, LoginScene, MainMenuScene, LobbyScene, GameScene }

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader I;
    void Awake() { if (I == null) { I = this; DontDestroyOnLoad(gameObject); } else Destroy(gameObject); }

    public void Load(AppScene s) => SceneManager.LoadScene(s.ToString());
}
