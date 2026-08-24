using System;

namespace DebuffLens2.Models;

public sealed class UnknownEffectRecord
{
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double LastTimer { get; set; }
    public double LastMaxTimer { get; set; }
    public int LastCharges { get; set; }
    public int LastStacks { get; set; }
    public string FirstObservedArea { get; set; } = string.Empty;
    public DateTime FirstObservedUtc { get; set; }
    public DateTime LastObservedUtc { get; set; }
    public int ObservedApplications { get; set; }
}
