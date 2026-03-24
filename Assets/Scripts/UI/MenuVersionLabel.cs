using TMPro;
using UnityEngine;

public class MenuVersionLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text targetText;

    public static string BuildVersionText()
    {
        return $"v{Application.version} \u00B7 PbP {TurnManager.PbpProtocolVersion}";
    }

    private void Start()
    {
        if (targetText == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("MenuVersionLabel: targetText is not assigned.", this);
#endif
            return;
        }

        targetText.text = BuildVersionText();
    }
}
