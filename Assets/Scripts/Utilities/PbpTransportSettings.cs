using UnityEngine;

[CreateAssetMenu(fileName = "PbpTransportSettings", menuName = "BlockNations/PBp Transport Settings")]
public class PbpTransportSettings : ScriptableObject
{
    [Tooltip("Shared PBp base URL used by HttpTurnTransport for both menu and gameplay.")]
    public string playByPostBaseUrl = string.Empty;
}
