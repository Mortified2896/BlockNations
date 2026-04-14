using UnityEngine;

[CreateAssetMenu(fileName = "PbpTransportSettings", menuName = "BlockNations/PBp Transport Settings")]
public class PbpTransportSettings : ScriptableObject
{
    [Tooltip("Shared PBp base URL used by HttpTurnTransport for both menu and gameplay.")]
    public string playByPostBaseUrl = string.Empty;

    [Tooltip("Bundled PBp API key for non-development iOS/Android release builds. MVP workaround only: values shipped in a public app are not secret.")]
    public string releaseMobileApiKey = string.Empty;
}
