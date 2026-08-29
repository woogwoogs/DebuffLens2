using System.Drawing;
using System.Collections.Generic;
using DebuffLens2.Models;
using System.Numerics;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using Newtonsoft.Json;

namespace DebuffLens2;

public sealed class DebuffLens2Settings : ISettings
{
    public DebuffLens2Settings()
    {
        Hud = new HudVisibilitySettings { Parent = this };
        Layout = new LayoutSettings { Parent = this };
        Alerts = new AlertSettings { Parent = this };
    }

    public ToggleNode Enable { get; set; } = new(true);

    // These are the persisted settings from earlier builds.  The grouped menu
    // objects below forward to them, so existing config files keep every value.
    [IgnoreMenu]
    [Menu("Show HUD", "Shows the selected Hardcore HUD only while a tracked harmful effect is active.")]
    public ToggleNode ShowMappedDebuffs { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Display style", "Compact Icons is the smallest combat view. Detailed Icons uses the vertical icon-and-description layout.")]
    public ListNode DisplayStyle { get; set; } = new()
    {
        Value = "Compact Icons",
        Values = new List<string>
        {
            "Compact Icons",
            "Detailed Icons"
        }
    };

    [IgnoreMenu]
    [Menu("New debuff popups", "Briefly enlarges a newly applied tracked effect before it collapses into its normal compact pill. Drowning always shows its emergency warning.")]
    public ToggleNode ShowNewDebuffPopups { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Popup duration")]
    public RangeNode<float> InitialAlertDurationSeconds { get; set; } = new(1.1f, 0.25f, 2.0f);

    [IgnoreMenu]
    [Menu("Critical popup scale")]
    public RangeNode<float> CriticalAlertScale { get; set; } = new(1.25f, 1.0f, 2.0f);

    // Kept so existing settings files can be migrated once on startup. They are never shown again.
    [IgnoreMenu]
    public ToggleNode DetailedMode { get; set; } = new(false);

    [IgnoreMenu]
    public ToggleNode IconOnlyMode { get; set; } = new(false);

    [IgnoreMenu]
    [Menu("Visibility filter", "0 = All tracked; 1 = Critical + Major; 2 = Critical only; 3 = Custom (uses each library entry's Enabled setting).")]
    public RangeNode<int> VisibilityFilter { get; set; } = new(0, 0, 3);

    [IgnoreMenu]
    [Menu("Max visible debuffs", "The highest-priority effects remain visible first. An overflow pill shows how many lower-priority effects were hidden.")]
    public RangeNode<int> MaxVisibleDebuffs { get; set; } = new(6, 1, 16);

    [IgnoreMenu]
    [Menu("Sound alerts", "Plays an alert once when a tracked effect with Sound enabled is first applied. Drowning is enabled by default; other effects are opt-in from the Debuff Library.")]
    public ToggleNode EnableSoundAlerts { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Vertical layout", "Stacks Compact Icons vertically. Descriptions use the vertical icon-and-text layout; Detailed Icons always uses it.")]
    public ToggleNode VerticalLayout { get; set; } = new(false);

    [IgnoreMenu]
    [Menu("Drag overlay", "Drag directly on a displayed debuff pill to move the whole overlay.")]
    public ToggleNode AllowDragging { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Lock position", "Prevents mouse dragging while preserving the current position.")]
    public ToggleNode LockPosition { get; set; } = new(false);

    [IgnoreMenu]
    [Menu("Hide in town")]
    public ToggleNode HideInTown { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Hide in hideout")]
    public ToggleNode HideInHideout { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Overlay position")]
    public RangeNode<Vector2> OverlayPosition { get; set; } = new(new Vector2(900, 390), Vector2.Zero, new Vector2(4000, 4000));

    [IgnoreMenu]
    public RangeNode<int> IconSize { get; set; } = new(28, 16, 56);

    [IgnoreMenu]
    public RangeNode<int> PillHeight { get; set; } = new(38, 26, 80);

    [JsonIgnore]
    [Menu("HUD & visibility", "Choose the HUD style and which tracked effects can appear during combat.")]
    [Submenu(CollapsedByDefault = false)]
    public HudVisibilitySettings Hud { get; set; }

    [JsonIgnore]
    [Menu("Layout & position", "Choose how effects are arranged and where the overlay lives on screen.")]
    [Submenu(CollapsedByDefault = true)]
    public LayoutSettings Layout { get; set; }

    [JsonIgnore]
    [Menu("Alerts", "Configure the short first-application popup and optional sound alert.")]
    [Submenu(CollapsedByDefault = true)]
    public AlertSettings Alerts { get; set; }

    [Menu("Icon display", "Size, names, descriptions, radial dial, timer number, and application effects.")]
    [Submenu(CollapsedByDefault = true)]
    public IconOnlyDisplaySettings IconsOnly { get; set; } = new();

    [Menu("Appearance", "Shared scale, spacing, opacity, and colour tuning. Most players can leave these at their defaults.")]
    [Submenu(CollapsedByDefault = true)]
    public AppearanceSettings Appearance { get; set; } = new();

    [IgnoreMenu]
    public Dictionary<string, DebuffUserOverride> DebuffOverrides { get; set; } = new();

    [JsonIgnore]
    [Menu("Open Debuff Library", "Configure individual tracked effects: enabled state, priority, compact name, icon, timer, alert, and sound preference.")]
    public ButtonNode OpenDebuffLibrary { get; set; } = new();

    [Menu("Debug", "Raw runtime data and unknown-effect discovery tools. Leave these closed during normal play.")]
    [Submenu(CollapsedByDefault = true)]
    public DebugSettings Debug { get; set; } = new();

    [JsonIgnore]
    [Menu("Reload debuff database", "Reloads Data/Debuffs.json and local icon files without restarting ExileCore2.")]
    public ButtonNode ReloadDatabase { get; set; } = new();
}

public sealed class HudVisibilitySettings
{
    [JsonIgnore]
    internal DebuffLens2Settings Parent { get; set; }

    [Menu("Show HUD", "Shows the selected Hardcore HUD only while a tracked harmful effect is active.")]
    public ToggleNode ShowHud { get => Parent.ShowMappedDebuffs; set => Parent.ShowMappedDebuffs = value; }
    [Menu("Display style", "Compact Icons is the smallest combat view. Detailed Icons uses the vertical icon-and-description layout.")]
    public ListNode DisplayStyle { get => Parent.DisplayStyle; set => Parent.DisplayStyle = value; }
    [Menu("Visibility filter", "0 = All tracked; 1 = Critical + Major; 2 = Critical only; 3 = Custom.")]
    public RangeNode<int> VisibilityFilter { get => Parent.VisibilityFilter; set => Parent.VisibilityFilter = value; }
    [Menu("Max visible debuffs", "Critical effects remain visible before lower-priority effects.")]
    public RangeNode<int> MaxVisibleDebuffs { get => Parent.MaxVisibleDebuffs; set => Parent.MaxVisibleDebuffs = value; }
    [Menu("Hide in town")]
    public ToggleNode HideInTown { get => Parent.HideInTown; set => Parent.HideInTown = value; }
    [Menu("Hide in hideout")]
    public ToggleNode HideInHideout { get => Parent.HideInHideout; set => Parent.HideInHideout = value; }
}

public sealed class LayoutSettings
{
    [JsonIgnore]
    internal DebuffLens2Settings Parent { get; set; }

    [Menu("Vertical layout", "Stacks Compact Icons vertically. Detailed Icons is always vertical.")]
    public ToggleNode VerticalLayout { get => Parent.VerticalLayout; set => Parent.VerticalLayout = value; }
    [Menu("Drag overlay", "Drag any displayed effect to move the whole HUD.")]
    public ToggleNode DragOverlay { get => Parent.AllowDragging; set => Parent.AllowDragging = value; }
    [Menu("Lock position")]
    public ToggleNode LockPosition { get => Parent.LockPosition; set => Parent.LockPosition = value; }
    [Menu("Overlay position")]
    public RangeNode<Vector2> OverlayPosition { get => Parent.OverlayPosition; set => Parent.OverlayPosition = value; }
}

public sealed class AlertSettings
{
    [JsonIgnore]
    internal DebuffLens2Settings Parent { get; set; }

    [Menu("New debuff popups")]
    public ToggleNode NewDebuffPopups { get => Parent.ShowNewDebuffPopups; set => Parent.ShowNewDebuffPopups = value; }
    [Menu("Popup duration")]
    public RangeNode<float> PopupDuration { get => Parent.InitialAlertDurationSeconds; set => Parent.InitialAlertDurationSeconds = value; }
    [Menu("Critical popup scale")]
    public RangeNode<float> CriticalPopupScale { get => Parent.CriticalAlertScale; set => Parent.CriticalAlertScale = value; }
    [Menu("Sound alerts")]
    public ToggleNode SoundAlerts { get => Parent.EnableSoundAlerts; set => Parent.EnableSoundAlerts = value; }
}

public sealed class IconOnlyDisplaySettings
{
    public IconOnlyDisplaySettings()
    {
        Content = new IconContentSettings { Parent = this };
        Countdown = new IconCountdownSettings { Parent = this };
        Values = new IconValueSettings { Parent = this };
        TextColours = new IconTextColourSettings { Parent = this };
    }

    // Persisted leaf values; grouped menu sections below forward to them.
    [IgnoreMenu]
    [Menu("Icon size")]
    public RangeNode<int> Size { get; set; } = new(54, 28, 128);

    [IgnoreMenu]
    [Menu("Show names", "Shows the compact debuff name under horizontal icons, or to the right of icons in vertical layout.")]
    public ToggleNode Labels { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Color names by debuff", "When descriptions are off, uses each effect's accent colour for its icon label. Leave off for the neutral label style.")]
    public ToggleNode ColorLabelsByDebuff { get; set; } = new(false);

    [IgnoreMenu]
    [Menu("Name size")]
    public RangeNode<float> LabelScale { get; set; } = new(0.82f, 0.45f, 1.25f);

    [IgnoreMenu]
    [Menu("Show descriptions", "In vertical layout, places the curated combat consequence beside each icon. Long consequences wrap inside a fixed-width text column.")]
    public ToggleNode VerticalDescriptions { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Description column width", "Maximum width of the wrapped text beside vertical icons.")]
    public RangeNode<int> DescriptionWidth { get; set; } = new(185, 110, 420);

    [IgnoreMenu]
    [Menu("Application effects", "On a new application only: Critical briefly pulses with a red border and Major briefly glows with an orange border. Minor effects never animate. When disabled, icons retain their normal black borders.")]
    public ToggleNode PriorityVisualEffects { get; set; } = new(false);

    [IgnoreMenu]
    [Menu("Show radial dial", "Shows a smooth shaded counter-clockwise cooldown mask inside timed icons. Persistent effects never receive a fake timer.")]
    public ToggleNode CountdownDial { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Use observed timer when maximum is missing", "When ExileCore2 provides Timer but no MaxTime, uses the highest live Timer observed during the current application as the dial baseline. It is cleared when the effect ends.")]
    public ToggleNode ObservedDurationFallback { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Show timer number", "Shows the live remaining duration directly inside timed icons, for example 5.0.")]
    public ToggleNode TimerText { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Show verified magnitudes", "Shows values such as Shock 20% or Armour Break 770 only when DebuffLens2 can pair PoE's native value with the correct effect confidently.")]
    public ToggleNode Magnitudes { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Show verified stacks", "Shows a small xN badge for effects whose native secondary value is known to represent stacks, such as Corrupted Blood.")]
    public ToggleNode Stacks { get; set; } = new(true);

    [IgnoreMenu]
    [Menu("Timer number size")]
    public RangeNode<float> TimerTextScale { get; set; } = new(1.15f, 0.55f, 1.8f);

    [IgnoreMenu]
    public ColorNode LabelColor { get; set; } = Color.FromArgb(245, 230, 224, 205);
    [IgnoreMenu]
    public ColorNode DescriptionColor { get; set; } = Color.FromArgb(255, 185, 192, 200);
    [IgnoreMenu]
    public ColorNode TimerColor { get; set; } = Color.FromArgb(255, 244, 244, 244);

    [JsonIgnore]
    [Menu("Content & labels", "Control the icon size, names, and the optional wrapped combat consequence.")]
    [Submenu(CollapsedByDefault = false)]
    public IconContentSettings Content { get; set; }

    [JsonIgnore]
    [Menu("Timer & radial dial", "Choose the countdown dial, timer number, and fallback behaviour for effects with no maximum duration.")]
    [Submenu(CollapsedByDefault = true)]
    public IconCountdownSettings Countdown { get; set; }

    [JsonIgnore]
    [Menu("Values & application effects", "Optional verified magnitude/stack labels and the first-application pulse or glow.")]
    [Submenu(CollapsedByDefault = true)]
    public IconValueSettings Values { get; set; }

    [JsonIgnore]
    [Menu("Text colours", "Fine-tune the neutral label, description, and timer colours.")]
    [Submenu(CollapsedByDefault = true)]
    public IconTextColourSettings TextColours { get; set; }
}

public sealed class IconContentSettings
{
    [JsonIgnore] internal IconOnlyDisplaySettings Parent { get; set; }
    [Menu("Icon size")]
    public RangeNode<int> IconSize { get => Parent.Size; set => Parent.Size = value; }
    [Menu("Show names")]
    public ToggleNode ShowNames { get => Parent.Labels; set => Parent.Labels = value; }
    [Menu("Color names by debuff")]
    public ToggleNode ColorNamesByDebuff { get => Parent.ColorLabelsByDebuff; set => Parent.ColorLabelsByDebuff = value; }
    [Menu("Name size")]
    public RangeNode<float> NameSize { get => Parent.LabelScale; set => Parent.LabelScale = value; }
    [Menu("Show descriptions")]
    public ToggleNode ShowDescriptions { get => Parent.VerticalDescriptions; set => Parent.VerticalDescriptions = value; }
    [Menu("Description column width")]
    public RangeNode<int> DescriptionColumnWidth { get => Parent.DescriptionWidth; set => Parent.DescriptionWidth = value; }
}

public sealed class IconCountdownSettings
{
    [JsonIgnore] internal IconOnlyDisplaySettings Parent { get; set; }
    [Menu("Show radial dial")]
    public ToggleNode ShowRadialDial { get => Parent.CountdownDial; set => Parent.CountdownDial = value; }
    [Menu("Use observed timer when maximum is missing")]
    public ToggleNode UseObservedTimerWhenMaximumMissing { get => Parent.ObservedDurationFallback; set => Parent.ObservedDurationFallback = value; }
    [Menu("Show timer number")]
    public ToggleNode ShowTimerNumber { get => Parent.TimerText; set => Parent.TimerText = value; }
    [Menu("Timer number size")]
    public RangeNode<float> TimerNumberSize { get => Parent.TimerTextScale; set => Parent.TimerTextScale = value; }
}

public sealed class IconValueSettings
{
    [JsonIgnore] internal IconOnlyDisplaySettings Parent { get; set; }
    [Menu("Show verified magnitudes")]
    public ToggleNode ShowVerifiedMagnitudes { get => Parent.Magnitudes; set => Parent.Magnitudes = value; }
    [Menu("Show verified stacks")]
    public ToggleNode ShowVerifiedStacks { get => Parent.Stacks; set => Parent.Stacks = value; }
    [Menu("Application effects")]
    public ToggleNode ApplicationEffects { get => Parent.PriorityVisualEffects; set => Parent.PriorityVisualEffects = value; }
}

public sealed class IconTextColourSettings
{
    [JsonIgnore] internal IconOnlyDisplaySettings Parent { get; set; }
    public ColorNode LabelColor { get => Parent.LabelColor; set => Parent.LabelColor = value; }
    public ColorNode DescriptionColor { get => Parent.DescriptionColor; set => Parent.DescriptionColor = value; }
    public ColorNode TimerColor { get => Parent.TimerColor; set => Parent.TimerColor = value; }
}

public sealed class AppearanceSettings
{
    [Menu("Overall scale")]
    public RangeNode<float> OverallScale { get; set; } = new(1.0f, 0.6f, 2.0f);

    [Menu("Text scale")]
    public RangeNode<float> FontScale { get; set; } = new(1.0f, 0.65f, 1.8f);

    public RangeNode<int> Padding { get; set; } = new(8, 2, 24);
    public RangeNode<int> Spacing { get; set; } = new(6, 0, 30);
    public RangeNode<int> BackgroundOpacity { get; set; } = new(230, 0, 255);

    public ColorNode BackgroundColor { get; set; } = Color.FromArgb(255, 8, 10, 14);
    public ColorNode TextColor { get; set; } = Color.FromArgb(255, 238, 238, 238);
    public ColorNode TimerColor { get; set; } = Color.FromArgb(255, 205, 214, 222);
    public ColorNode MinorColor { get; set; } = Color.FromArgb(255, 154, 169, 184);
    public ColorNode MajorColor { get; set; } = Color.FromArgb(255, 255, 183, 77);
    public ColorNode CriticalColor { get; set; } = Color.FromArgb(255, 255, 82, 82);
}

public sealed class DebugSettings
{
    [Menu("Show raw effect scanner", "Shows all player effects, including beneficial ones. Use only when investigating an unknown effect.")]
    public ToggleNode ShowRawEffects { get; set; } = new(false);

    [Menu("Show live Buff member inspector", "Debug only. Lists public primitive/string fields exposed by ExileCore2's live Buff objects so stack and magnitude sources can be proven before they are shown on the HUD.")]
    public ToggleNode ShowBuffMemberProbe { get; set; } = new(false);

    [Menu("Show native Buff UI hover inspector", "Debug only. Hover PoE's own top-left player-effect icon or number. This inspects the public UI element under the cursor, looking for the exact text PoE renders such as Shock 20% or Armour Break 480.")]
    public ToggleNode ShowNativeBuffUiProbe { get; set; } = new(false);

    [Menu("Show native player-effect panel scanner", "Debug only. Scans every tile in PoE's native top-left player-effect panel and lists its timer and magnitude/stack label beside DebuffLens2's tracked BuffsList effects.")]
    public ToggleNode ShowNativeBuffPanelScanner { get; set; } = new(false);

    [Menu("Show unknown player effects", "Shows only active player effects that are not mapped in DebuffLens2's curated database. This never adds them to the normal HUD.")]
    public ToggleNode ShowUnknownPlayerEffects { get; set; } = new(false);

    [Menu("Log unknown player effects", "Writes new/reapplied unknown runtime effects to UnknownEffects.json for later research. It is deduplicated and never writes every scan.")]
    public ToggleNode LogUnknownPlayerEffects { get; set; } = new(false);

    [Menu("Show raw runtime values", "Adds timer/max timer, charges, and stacks to the unknown-effect panel.")]
    public ToggleNode ShowRawRuntimeValues { get; set; } = new(true);

    public RangeNode<Vector2> RawPosition { get; set; } = new(new Vector2(35, 150), Vector2.Zero, new Vector2(4000, 4000));
    public RangeNode<Vector2> UnknownPosition { get; set; } = new(new Vector2(35, 470), Vector2.Zero, new Vector2(4000, 4000));
    public RangeNode<Vector2> BuffProbePosition { get; set; } = new(new Vector2(700, 150), Vector2.Zero, new Vector2(4000, 4000));
    public RangeNode<Vector2> NativeBuffUiProbePosition { get; set; } = new(new Vector2(700, 560), Vector2.Zero, new Vector2(4000, 4000));
    public RangeNode<Vector2> NativeBuffPanelPosition { get; set; } = new(new Vector2(520, 160), Vector2.Zero, new Vector2(4000, 4000));
    public RangeNode<float> RawTextScale { get; set; } = new(1.0f, 0.7f, 2.0f);
    public RangeNode<int> MaxRawEffects { get; set; } = new(30, 1, 100);
    public RangeNode<int> MaxUnknownEffects { get; set; } = new(12, 1, 100);
    public RangeNode<int> MaxBuffProbeEffects { get; set; } = new(4, 1, 12);
    public RangeNode<int> MaxBuffProbeMembers { get; set; } = new(24, 6, 64);
    public RangeNode<int> MaxNativeBuffUiProbeMembers { get; set; } = new(28, 8, 80);
    public RangeNode<int> MaxNativeBuffPanelEffects { get; set; } = new(30, 4, 80);
    public RangeNode<int> ScanIntervalMilliseconds { get; set; } = new(50, 25, 500);
    public ColorNode RawTextColor { get; set; } = Color.FromArgb(235, 235, 235);
}
