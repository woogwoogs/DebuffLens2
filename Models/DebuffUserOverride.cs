using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DebuffLens2.Models;

// Kept separate from the shipped definition database so database updates never
// overwrite a player's HC presentation choices.
public sealed class DebuffUserOverride
{
    public bool? Enabled { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public DebuffPriority? Priority { get; set; }

    public bool? ShowIcon { get; set; }
    public bool? ShowTimer { get; set; }
    public bool? InitialAlert { get; set; }
    public bool? Sound { get; set; }
    public string CompactName { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasAnyValue => Enabled.HasValue || Priority.HasValue || ShowIcon.HasValue ||
                               ShowTimer.HasValue || InitialAlert.HasValue || Sound.HasValue ||
                               !string.IsNullOrWhiteSpace(CompactName);
}
