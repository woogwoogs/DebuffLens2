using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DebuffLens2.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum DebuffPriority
{
    Minor = 0,
    Major = 1,
    Critical = 2,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum DebuffMergeMode
{
    LongestDurationThenCharges,
    HighestChargesThenDuration,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum NativeValueMode
{
    None,
    PercentageMagnitude,
    IntegerMagnitude,
    Stacks,
}

public sealed class DebuffDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CompactName { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string DetailedDescription { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public DebuffPriority Priority { get; set; } = DebuffPriority.Major;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool ShowTimer { get; set; } = true;
    public bool ShowStacks { get; set; }
    public bool ShowMagnitude { get; set; }
    public NativeValueMode NativeValueMode { get; set; }
    public bool DefaultEnabled { get; set; } = true;
    public bool DefaultSound { get; set; }
    public bool DefaultInitialAlert { get; set; } = true;
    public string InitialAlertText { get; set; } = string.Empty;
    public float InitialAlertScale { get; set; } = 1f;
    public float InitialAlertDurationMultiplier { get; set; } = 1f;
    public bool RuntimeVerified { get; set; }
    public DebuffMergeMode MergeMode { get; set; } = DebuffMergeMode.LongestDurationThenCharges;

    [JsonIgnore]
    public bool GeneratedFromRePoe { get; set; }

    [JsonIgnore]
    public string SourceInternalId { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceVisualId { get; set; } = string.Empty;

    [JsonIgnore]
    public List<string> SourceStats { get; set; } = new();

    [JsonIgnore]
    public int StableOrder { get; set; }
}
