/// <summary>Capabilities of the isolated, credential-free public browser builds.</summary>
public static class PublicWebBuild
{
#if BLOCKNATIONS_SINGLE_PLAYER_WEB
    public const bool IsSinglePlayer = true;
#else
    public const bool IsSinglePlayer = false;
#endif
#if BLOCKNATIONS_ACCOUNT_WEB
    public const bool UsesGoogleAccounts = true;
#else
    public const bool UsesGoogleAccounts = false;
#endif
    public const bool IsPublicWeb = IsSinglePlayer || UsesGoogleAccounts;
#if BLOCKNATIONS_LEARNED_AI_WEB
    public const bool UsesFrozenOpponent = IsPublicWeb;
#else
    public const bool UsesFrozenOpponent = false;
#endif
}
