using System;

public partial class TurnManager
{
    // Private training-player matches only. Never serialized into a save or PBp.
    public bool DiagnosticFullVision { get; private set; }
    public void ConfigureDiagnosticHumanVision(bool fullVision)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!IsExternallyDrivenMatch || ExternalMatchReady || externalHumanSeatIndex < 0)
            throw new InvalidOperationException("Diagnostic vision requires an isolated human training match before reset.");
        DiagnosticFullVision = fullVision;
#else
        if (fullVision) throw new InvalidOperationException("Diagnostic vision is unavailable in this player.");
#endif
    }
}
