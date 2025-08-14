using System.Collections;
using UnityEngine;
public class BootstrapEntry : MonoBehaviour
{
    IEnumerator Start()
    {
        // Ein Frame warten, damit UGS/Runner stehen
        yield return null;
        SceneLoader.I.Load(AppScene.LoginScene);
    }
}
