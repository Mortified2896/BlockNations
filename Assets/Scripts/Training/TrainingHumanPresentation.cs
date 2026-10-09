using UnityEngine;

// References are authored by the training scene builder, never searched at runtime.
public sealed class TrainingHumanPresentation : MonoBehaviour
{
    [SerializeField] private GameObject[] humanUIRoots;
    [SerializeField] private MonoBehaviour[] humanInput;

    public void SetHumanMode(bool human)
    {
        foreach (GameObject root in humanUIRoots) if (root != null) root.SetActive(human);
        foreach (MonoBehaviour input in humanInput) if (input != null) input.enabled = human;
    }
}
