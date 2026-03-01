using TMPro;
using UnityEngine;

public class MenuVersionLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text targetText;

    private void Start()
    {
        if (targetText == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("MenuVersionLabel: targetText is not assigned.", this);
#endif
            return;
        }

        targetText.text = $"v{Application.version} \u00B7 PbP {TurnManager.PbpProtocolVersion}";
    }
}
