using UnityEngine;
using UnityEngine.SceneManagement;

// Instructions:
// 1. Create a new empty scene named TutorialLauncher.unity.
// 2. Add an empty GameObject named TutorialBoot.
// 3. Attach this component to that GameObject.
// 4. Add both TutorialLauncher and the gameplay scene to Build Settings.
public class TutorialBoot : MonoBehaviour
{
    [SerializeField]
    private string gameplaySceneName = "SampleScene";

    private void Awake()
    {
        if (string.IsNullOrEmpty(gameplaySceneName))
        {
            Debug.LogError("[TutorialBoot] Gameplay scene name is empty; cannot continue.");
            return;
        }

        Debug.Log("[TutorialBoot] Requesting tutorial to show.");
        TutorialLaunch.RequestShow(resetCompleted: true);

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.name == gameplaySceneName)
        {
            Debug.Log("[TutorialBoot] Already in gameplay scene; skipping scene load.");
            return;
        }

        Debug.Log($"[TutorialBoot] Loading gameplay scene '{gameplaySceneName}'.");
        SceneManager.LoadScene(gameplaySceneName);
    }
}
