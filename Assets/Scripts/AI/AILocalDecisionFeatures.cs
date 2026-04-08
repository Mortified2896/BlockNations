using System;

[Flags]
public enum AILocalDecisionFeatures
{
    None = 0,
    OffensiveObviousWin = 1 << 0,
    DefensiveVeto = 1 << 1,
    ExchangeScoring = 1 << 2,
    All = OffensiveObviousWin | DefensiveVeto | ExchangeScoring
}
