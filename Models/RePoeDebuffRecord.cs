using System.Collections.Generic;

namespace DebuffLens2.Models;

public sealed class RePoeDebuffRecord
{
    public string InternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CombatDescription { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Invisible { get; set; }
    public List<string> Stats { get; set; } = new();
    public string VisualId { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int? StackLimit { get; set; }
}
