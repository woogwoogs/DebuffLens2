using System.Collections.Generic;

namespace DebuffLens2.Models;

internal sealed class RuntimeEffectSnapshot
{
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double TimeLeft { get; set; }
    public double MaxTime { get; set; }
    public int Charges { get; set; }
    public int Stacks { get; set; }
}

internal sealed class ActiveTrackedEffect
{
    public DebuffDefinition Definition { get; set; }
    public string RuntimeDisplayName { get; set; } = string.Empty;
    public double TimeLeft { get; set; }
    public double MaxTime { get; set; }
    public int Charges { get; set; }
    public int Stacks { get; set; }
    public long AppliedAt { get; set; }
    public List<string> MatchedAliases { get; } = new();

    public void Reset(DebuffDefinition definition, RuntimeEffectSnapshot snapshot)
    {
        Definition = definition;
        RuntimeDisplayName = snapshot.DisplayName;
        TimeLeft = snapshot.TimeLeft;
        MaxTime = snapshot.MaxTime;
        Charges = snapshot.Charges;
        Stacks = snapshot.Stacks;
        AppliedAt = 0;
        MatchedAliases.Clear();
        MatchedAliases.Add(snapshot.InternalName);
    }
}
