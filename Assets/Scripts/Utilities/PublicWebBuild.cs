/// <summary>Capabilities of the dedicated, offline single-player browser release.</summary>
public static class PublicWebBuild
{
#if BLOCKNATIONS_SINGLE_PLAYER_WEB
    public const bool IsSinglePlayer = true;
#else
    public const bool IsSinglePlayer = false;
#endif
}
