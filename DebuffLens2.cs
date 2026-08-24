using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using DebuffLens2.Models;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ImGuiNET;
using Newtonsoft.Json;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace DebuffLens2;

public sealed class DebuffLens2 : BaseSettingsPlugin<DebuffLens2Settings>
{
    private const string IconTexturePrefix = "debufflens2_";

    private readonly List<DebuffDefinition> _definitions = new();
    private readonly Dictionary<string, DebuffDefinition> _definitionByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEffectSnapshot> _rawByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RuntimeEffectSnapshot> _rawEffects = new();
    private readonly List<RuntimeEffectSnapshot> _snapshotPool = new();
    private readonly Dictionary<string, ActiveTrackedEffect> _activeByDefinition = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveTrackedEffect> _activeEffects = new();
    private readonly List<ActiveTrackedEffect> _visibleEffects = new();
    private readonly List<ActiveTrackedEffect> _activeEffectPool = new();
    private readonly List<RenderLine> _rawLines = new();
    private readonly List<RenderLine> _unknownLines = new();
    private readonly List<RenderLine> _buffProbeLines = new();
    private readonly List<RenderLine> _nativeBuffUiProbeLines = new();
    private readonly Dictionary<Type, List<MemberInfo>> _buffProbeMembersByType = new();
    private readonly List<RuntimeEffectSnapshot> _activeUnknownEffects = new();
    private readonly List<PillLayout> _pillLayouts = new();
    private readonly HashSet<string> _loadedIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownCatalogIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _activeSinceByDefinition = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _dialDurationByDefinition = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedDefinitionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _inactiveDefinitionIds = new();
    private readonly Dictionary<string, UnknownEffectRecord> _unknownEffectLog = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownNamesThisScan = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownNamesLastScan = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WrappedTextCacheEntry> _wrappedIconDescriptionCache = new(StringComparer.OrdinalIgnoreCase);

    private long _nextScanAt;
    private long _lastScanErrorAt;
    private int _snapshotPoolIndex;
    private int _activeEffectPoolIndex;
    private string _lastScanError = string.Empty;
    private string _databaseError = string.Empty;
    private string _lastSoundError = string.Empty;
    private byte[] _dangerSoundData;
    private byte[] _attentionSoundData;
    private bool _isDragging;
    private bool _hasHudBounds;
    private bool _showDebuffLibrary;
    private bool _showGeneratedLibraryEntries;
    private int _overflowCount;
    private string _selectedDefinitionId = string.Empty;
    private string _librarySearch = string.Empty;
    private Vector2 _dragOffset;
    private RectangleF _hudBounds;
    private readonly Vector2[] _cooldownMaskPoints = new Vector2[6];

    private string DatabasePath => Path.Combine(DirectoryFullName, "Data", "Debuffs.json");
    private string RePoeCatalogPath => Path.Combine(DirectoryFullName, "Data", "RePoeDebuffs.json");
    private string AssetsDirectory => Path.Combine(DirectoryFullName, "assets");
    private string UnknownEffectsPath => GetUnknownEffectsPath();

    public override void OnLoad()
    {
        LoadDatabase();
    }

    public override bool Initialise()
    {
        MigrateLegacyDisplaySettings();
        Settings.ReloadDatabase.OnPressed += LoadDatabase;
        Settings.OpenDebuffLibrary.OnPressed += () => _showDebuffLibrary = true;
        LoadUnknownEffectLog();
        return true;
    }

    private void MigrateLegacyDisplaySettings()
    {
        // Preserve earlier choices while replacing the retired card HUD with Detailed Icons.
        var wasLegacyDetailed =
            (Settings.DetailedMode.Value && !Settings.IconOnlyMode.Value) ||
            Settings.DisplayStyle.Value == "Detailed Cards";

        if (wasLegacyDetailed)
        {
            Settings.DisplayStyle.Value = "Detailed Icons";
        }
        else if (Settings.DisplayStyle.Value != "Detailed Icons")
        {
            Settings.DisplayStyle.Value = "Compact Icons";
        }

        Settings.DetailedMode.Value = false;
        Settings.IconOnlyMode.Value = false;
    }

    public override void AreaChange(AreaInstance area)
    {
        ClearRuntimeState();
        _nextScanAt = 0;
    }

    public override void Tick()
    {
        if (!Settings.Enable.Value || !GameController.InGame || GameController.IsLoading || ShouldHideForArea())
        {
            ClearRuntimeState();
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextScanAt)
            return;

        _nextScanAt = now + Settings.Debug.ScanIntervalMilliseconds.Value;
        ScanPlayerEffects();
    }

    public override void Render()
    {
        if (!Settings.Enable.Value || !GameController.InGame || GameController.IsLoading || ShouldHideForArea())
            return;

        BuildVisibleEffects();

        if (Settings.ShowMappedDebuffs.Value && _visibleEffects.Count > 0)
        {
            DrawCompactHud(Settings.OverlayPosition.Value);
            DrawInitialAlert(Settings.OverlayPosition.Value);
            HandleOverlayDrag();
        }
        else
        {
            _hasHudBounds = false;
            _isDragging = false;
        }

        if (Settings.Debug.ShowRawEffects.Value && _rawLines.Count > 0)
            DrawRawPanel(_rawLines, Settings.Debug.RawPosition.Value);

        if (Settings.Debug.ShowUnknownPlayerEffects.Value && _unknownLines.Count > 0)
            DrawRawPanel(_unknownLines, Settings.Debug.UnknownPosition.Value);

        if (Settings.Debug.ShowBuffMemberProbe.Value && _buffProbeLines.Count > 0)
            DrawRawPanel(_buffProbeLines, Settings.Debug.BuffProbePosition.Value);

        if (Settings.Debug.ShowNativeBuffUiProbe.Value)
        {
            RebuildNativeBuffUiProbe();
            if (_nativeBuffUiProbeLines.Count > 0)
                DrawRawPanel(_nativeBuffUiProbeLines, Settings.Debug.NativeBuffUiProbePosition.Value);
        }

        if (_showDebuffLibrary)
            DrawDebuffLibrary();
    }

    private void LoadDatabase()
    {
        _definitions.Clear();
        _definitionByAlias.Clear();
        _loadedIcons.Clear();
        _failedIcons.Clear();
        _knownCatalogIds.Clear();
        _wrappedIconDescriptionCache.Clear();
        _databaseError = string.Empty;

        try
        {
            if (!File.Exists(DatabasePath))
                throw new FileNotFoundException("Debuff database was not found.", DatabasePath);

            var json = File.ReadAllText(DatabasePath);
            var loaded = JsonConvert.DeserializeObject<List<DebuffDefinition>>(json) ?? new List<DebuffDefinition>();

            var catalogRecords = LoadRePoeCatalog();
            var catalogByInternalId = new Dictionary<string, RePoeDebuffRecord>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < catalogRecords.Count; i++)
            {
                var record = catalogRecords[i];
                if (string.IsNullOrWhiteSpace(record.InternalId))
                    continue;
                _knownCatalogIds.Add(record.InternalId);
                if (!catalogByInternalId.ContainsKey(record.InternalId))
                    catalogByInternalId.Add(record.InternalId, record);
            }

            for (var i = 0; i < loaded.Count; i++)
            {
                var definition = loaded[i];
                if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DisplayName))
                {
                    DebugWindow.LogError($"DebuffLens2: skipped definition #{i + 1}; Id and DisplayName are required.");
                    continue;
                }

                ApplyCatalogMetadata(definition, catalogByInternalId);
                AddDefinition(definition);
            }

            catalogRecords.Sort(CompareCatalogRecords);
            for (var i = 0; i < catalogRecords.Count; i++)
            {
                var record = catalogRecords[i];
                if (string.IsNullOrWhiteSpace(record.InternalId) ||
                    string.IsNullOrWhiteSpace(record.Name) ||
                    _definitionByAlias.ContainsKey(record.InternalId))
                    continue;

                AddDefinition(CreateGeneratedDefinition(record));
            }

            DebugWindow.LogMsg($"DebuffLens2: loaded {_definitions.Count} display definitions and {_definitionByAlias.Count} runtime aliases from curated overrides plus {_knownCatalogIds.Count} RePoE harmful records. Icons load lazily.");
        }
        catch (Exception exception)
        {
            _databaseError = exception.Message;
            DebugWindow.LogError($"DebuffLens2: could not load Data/Debuffs.json. Raw scanning will still work. {exception}");
        }

        _nextScanAt = 0;
    }

    private List<RePoeDebuffRecord> LoadRePoeCatalog()
    {
        if (!File.Exists(RePoeCatalogPath))
            return new List<RePoeDebuffRecord>();

        try
        {
            return JsonConvert.DeserializeObject<List<RePoeDebuffRecord>>(File.ReadAllText(RePoeCatalogPath)) ??
                   new List<RePoeDebuffRecord>();
        }
        catch (Exception exception)
        {
            DebugWindow.LogError("DebuffLens2: could not load the generated RePoE catalog safely: " + exception.Message);
            return new List<RePoeDebuffRecord>();
        }
    }

    private void AddDefinition(DebuffDefinition definition)
    {
        definition.StableOrder = _definitions.Count;
        _definitions.Add(definition);

        foreach (var aliasValue in definition.Aliases)
        {
            var alias = aliasValue?.Trim();
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (_definitionByAlias.TryGetValue(alias, out var existing))
            {
                DebugWindow.LogError($"DebuffLens2: alias '{alias}' belongs to both '{existing.Id}' and '{definition.Id}'. Keeping the first.");
                continue;
            }

            _definitionByAlias.Add(alias, definition);
        }
    }

    private static void ApplyCatalogMetadata(DebuffDefinition definition, Dictionary<string, RePoeDebuffRecord> catalogByInternalId)
    {
        RePoeDebuffRecord firstRecord = null;
        RePoeDebuffRecord iconRecord = null;
        for (var i = 0; i < definition.Aliases.Count; i++)
        {
            if (!catalogByInternalId.TryGetValue(definition.Aliases[i], out var record))
                continue;

            firstRecord ??= record;
            if (iconRecord == null && !string.IsNullOrWhiteSpace(record.Icon))
                iconRecord = record;
        }

        if (firstRecord == null)
            return;

        definition.SourceInternalId = firstRecord.InternalId;
        definition.SourceVisualId = firstRecord.VisualId;
        definition.SourceStats = firstRecord.Stats;
        if (iconRecord != null)
            definition.Icon = iconRecord.Icon;
    }

    private static DebuffDefinition CreateGeneratedDefinition(RePoeDebuffRecord record)
    {
        var combatDescription = string.IsNullOrWhiteSpace(record.CombatDescription)
            ? record.Description
            : record.CombatDescription;

        return new DebuffDefinition
        {
            Id = "repoe:" + record.InternalId,
            DisplayName = record.Name,
            CompactName = record.Name.ToUpperInvariant(),
            Aliases = new List<string> { record.InternalId },
            Description = record.Description,
            DetailedDescription = combatDescription,
            ShortDescription = combatDescription,
            Priority = DebuffPriority.Minor,
            Category = record.Category,
            Icon = record.Icon,
            ShowTimer = true,
            ShowStacks = record.StackLimit.HasValue && record.StackLimit.Value > 1,
            ShowMagnitude = false,
            DefaultEnabled = true,
            DefaultSound = false,
            DefaultInitialAlert = false,
            RuntimeVerified = false,
            MergeMode = record.StackLimit.HasValue && record.StackLimit.Value > 1
                ? DebuffMergeMode.HighestChargesThenDuration
                : DebuffMergeMode.LongestDurationThenCharges,
            GeneratedFromRePoe = true,
            SourceInternalId = record.InternalId,
            SourceVisualId = record.VisualId,
            SourceStats = record.Stats,
        };
    }

    private bool EnsureIconTextureLoaded(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon) || _failedIcons.Contains(icon))
            return false;
        if (_loadedIcons.Contains(icon))
            return true;

        var path = Path.Combine(AssetsDirectory, icon.Replace('/', Path.DirectorySeparatorChar) + ".png");
        try
        {
            if (!File.Exists(path))
            {
                _failedIcons.Add(icon);
                DebugWindow.LogError($"DebuffLens2: missing icon '{icon}'. Text fallback will be used.");
                return false;
            }

            Graphics.InitImage(IconTextureKey(icon), path);
            _loadedIcons.Add(icon);
            return true;
        }
        catch (Exception exception)
        {
            _failedIcons.Add(icon);
            DebugWindow.LogError($"DebuffLens2: failed to load icon '{icon}': {exception.Message}");
            return false;
        }
    }

    private bool ShouldHideForArea()
    {
        try
        {
            var area = GameController.Area?.CurrentArea;
            if (area == null)
                return true;

            return Settings.HideInTown.Value && area.IsTown ||
                   Settings.HideInHideout.Value && area.IsHideout;
        }
        catch
        {
            return true;
        }
    }

    private void ScanPlayerEffects()
    {
        _rawByName.Clear();
        _rawEffects.Clear();
        _activeByDefinition.Clear();
        _activeEffects.Clear();
        _rawLines.Clear();
        _unknownLines.Clear();
        _buffProbeLines.Clear();
        _activeUnknownEffects.Clear();
        _snapshotPoolIndex = 0;
        _activeEffectPoolIndex = 0;

        try
        {
            var player = GameController.Player;
            if (player == null || !player.TryGetComponent<Buffs>(out var buffs) || buffs?.BuffsList == null)
                return;

            var probeCandidates = Settings.Debug.ShowBuffMemberProbe.Value
                ? new List<BuffProbeCandidate>()
                : null;

            foreach (var buff in buffs.BuffsList)
            {
                if (buff == null || string.IsNullOrWhiteSpace(buff.Name))
                    continue;

                var snapshot = RentSnapshot();
                snapshot.InternalName = buff.Name;
                snapshot.DisplayName = buff.DisplayName ?? string.Empty;
                snapshot.TimeLeft = buff.Timer;
                snapshot.MaxTime = buff.MaxTime;
                snapshot.Charges = buff.BuffCharges;
                snapshot.Stacks = buff.BuffStacks;

                if (probeCandidates != null)
                    probeCandidates.Add(new BuffProbeCandidate(
                        buff,
                        snapshot,
                        _definitionByAlias.ContainsKey(snapshot.InternalName)));

                AddOrMergeRaw(snapshot);

                if (_definitionByAlias.TryGetValue(snapshot.InternalName, out var definition) && IsEffectEnabled(definition))
                    AddOrMergeTracked(definition, snapshot);
            }

            foreach (var pair in _rawByName)
                _rawEffects.Add(pair.Value);

            foreach (var pair in _activeByDefinition)
                _activeEffects.Add(pair.Value);

            _rawEffects.Sort(CompareRawEffects);
            _activeEffects.Sort(CompareTrackedEffects);
            UpdateActiveEffectLifetimes(Environment.TickCount64);
            UpdateUnknownEffects();

            if (probeCandidates != null)
                RebuildBuffMemberProbe(probeCandidates);

            if (Settings.Debug.ShowRawEffects.Value)
                RebuildRawRenderLines();
            if (Settings.Debug.ShowUnknownPlayerEffects.Value)
                RebuildUnknownRenderLines();
        }
        catch (Exception exception)
        {
            ClearRuntimeState();
            ReportScanError(exception.Message);
        }
    }

    private void RebuildBuffMemberProbe(List<BuffProbeCandidate> candidates)
    {
        candidates.Sort(CompareBuffProbeCandidates);
        var maxEffects = Settings.Debug.MaxBuffProbeEffects.Value;
        var count = Math.Min(maxEffects, candidates.Count);
        for (var i = 0; i < count; i++)
            CaptureBuffMemberProbe(candidates[i].Buff, candidates[i].Snapshot);
    }

    private void CaptureBuffMemberProbe(object buff, RuntimeEffectSnapshot snapshot)
    {
        var type = buff.GetType();
        var displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? snapshot.InternalName
            : snapshot.DisplayName;
        _buffProbeLines.Add(new RenderLine(
            $"BUFF INSPECTOR | {displayName} | {snapshot.InternalName} | {type.Name}",
            Color.FromArgb(255, 110, 220, 255)));

        var members = GetBuffProbeMembers(type);
        var maxMembers = Settings.Debug.MaxBuffProbeMembers.Value;
        var shown = 0;
        for (var i = 0; i < members.Count && shown < maxMembers; i++)
        {
            var member = members[i];
            if (!TryGetProbeValue(member, buff, out var value))
                continue;

            _buffProbeLines.Add(new RenderLine(
                $"  {member.Name} = {FormatProbeValue(value)}",
                Settings.Debug.RawTextColor.Value));
            shown++;
        }

        if (shown == 0)
            _buffProbeLines.Add(new RenderLine("  (No supported public primitive/string members.)", Color.FromArgb(255, 170, 170, 170)));
    }

    private static int CompareBuffProbeCandidates(BuffProbeCandidate left, BuffProbeCandidate right)
    {
        var mapped = right.IsMapped.CompareTo(left.IsMapped);
        if (mapped != 0)
            return mapped;

        var named = (!string.IsNullOrWhiteSpace(right.Snapshot.DisplayName))
            .CompareTo(!string.IsNullOrWhiteSpace(left.Snapshot.DisplayName));
        if (named != 0)
            return named;

        var timed = IsMeaningfulTime(right.Snapshot.TimeLeft).CompareTo(IsMeaningfulTime(left.Snapshot.TimeLeft));
        if (timed != 0)
            return timed;

        return StringComparer.OrdinalIgnoreCase.Compare(left.Snapshot.InternalName, right.Snapshot.InternalName);
    }

    private List<MemberInfo> GetBuffProbeMembers(Type type)
    {
        if (_buffProbeMembersByType.TryGetValue(type, out var cached))
            return cached;

        var members = new List<MemberInfo>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 && IsSupportedProbeType(property.PropertyType))
                members.Add(property);
        }

        foreach (var field in type.GetFields(flags))
        {
            if (IsSupportedProbeType(field.FieldType))
                members.Add(field);
        }

        members.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        _buffProbeMembersByType[type] = members;
        return members;
    }

    private static bool IsSupportedProbeType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive ||
               underlying.IsEnum ||
               underlying == typeof(string) ||
               underlying == typeof(decimal) ||
               underlying == typeof(DateTime) ||
               underlying == typeof(TimeSpan);
    }

    private static bool TryGetProbeValue(MemberInfo member, object instance, out object value)
    {
        try
        {
            value = member switch
            {
                PropertyInfo property => property.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => null,
            };
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static string FormatProbeValue(object value)
    {
        if (value == null)
            return "<null>";

        var text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
        const int maximumLength = 96;
        return text.Length <= maximumLength ? text : text.Substring(0, maximumLength - 3) + "...";
    }

    private void AddOrMergeRaw(RuntimeEffectSnapshot candidate)
    {
        if (!_rawByName.TryGetValue(candidate.InternalName, out var current))
        {
            _rawByName.Add(candidate.InternalName, candidate);
            return;
        }

        if (candidate.Charges > current.Charges ||
            candidate.Charges == current.Charges && candidate.Stacks > current.Stacks ||
            candidate.Charges == current.Charges && candidate.Stacks == current.Stacks && candidate.TimeLeft > current.TimeLeft)
        {
            _rawByName[candidate.InternalName] = candidate;
        }
    }

    private void UpdateActiveEffectLifetimes(long now)
    {
        _observedDefinitionIds.Clear();

        for (var i = 0; i < _activeEffects.Count; i++)
        {
            var effect = _activeEffects[i];
            var id = effect.Definition.Id;
            _observedDefinitionIds.Add(id);

            var isNewApplication = !_activeSinceByDefinition.TryGetValue(id, out var appliedAt);
            if (isNewApplication)
            {
                appliedAt = now;
                _activeSinceByDefinition.Add(id, appliedAt);
                TryPlayEffectSound(effect);
            }

            effect.AppliedAt = appliedAt;
            UpdateDialDuration(effect, isNewApplication);
        }

        _inactiveDefinitionIds.Clear();
        foreach (var pair in _activeSinceByDefinition)
        {
            if (!_observedDefinitionIds.Contains(pair.Key))
                _inactiveDefinitionIds.Add(pair.Key);
        }

        for (var i = 0; i < _inactiveDefinitionIds.Count; i++)
        {
            var id = _inactiveDefinitionIds[i];
            _activeSinceByDefinition.Remove(id);
            _dialDurationByDefinition.Remove(id);
        }
    }

    private void UpdateDialDuration(ActiveTrackedEffect effect, bool isNewApplication)
    {
        var id = effect.Definition.Id;
        if (!IsMeaningfulTime(effect.TimeLeft))
            return;

        if (IsMeaningfulTime(effect.MaxTime) && effect.MaxTime > 0d)
        {
            _dialDurationByDefinition[id] = effect.MaxTime;
            return;
        }

        if (!Settings.IconsOnly.ObservedDurationFallback.Value)
            return;

        if (isNewApplication ||
            !_dialDurationByDefinition.TryGetValue(id, out var observedDuration) ||
            effect.TimeLeft > observedDuration)
        {
            // Some ExileCore2 effects expose Timer but no MaxTime. The highest live
            // Timer observed during this application is a runtime-derived baseline,
            // not a guessed duration, and is discarded when the effect disappears.
            _dialDurationByDefinition[id] = effect.TimeLeft;
        }
    }

    private void AddOrMergeTracked(DebuffDefinition definition, RuntimeEffectSnapshot candidate)
    {
        if (!_activeByDefinition.TryGetValue(definition.Id, out var active))
        {
            active = RentActiveEffect();
            active.Reset(definition, candidate);
            _activeByDefinition.Add(definition.Id, active);
            return;
        }

        if (!ContainsIgnoreCase(active.MatchedAliases, candidate.InternalName))
            active.MatchedAliases.Add(candidate.InternalName);

        var replace = definition.MergeMode switch
        {
            DebuffMergeMode.HighestChargesThenDuration =>
                candidate.Charges > active.Charges ||
                candidate.Charges == active.Charges && candidate.Stacks > active.Stacks ||
                candidate.Charges == active.Charges && candidate.Stacks == active.Stacks && candidate.TimeLeft > active.TimeLeft,
            _ =>
                candidate.TimeLeft > active.TimeLeft ||
                Math.Abs(candidate.TimeLeft - active.TimeLeft) < 0.001 && candidate.Stacks > active.Stacks ||
                Math.Abs(candidate.TimeLeft - active.TimeLeft) < 0.001 && candidate.Stacks == active.Stacks && candidate.Charges > active.Charges,
        };

        if (!replace)
            return;

        active.RuntimeDisplayName = candidate.DisplayName;
        active.TimeLeft = candidate.TimeLeft;
        active.MaxTime = candidate.MaxTime;
        active.Charges = candidate.Charges;
        active.Stacks = candidate.Stacks;
    }

    private void BuildVisibleEffects()
    {
        _visibleEffects.Clear();
        _overflowCount = 0;

        if (!Settings.ShowMappedDebuffs.Value || _activeEffects.Count == 0)
            return;

        var maximum = Settings.MaxVisibleDebuffs.Value;
        for (var i = 0; i < _activeEffects.Count; i++)
        {
            var effect = _activeEffects[i];
            if (!PassesVisibilityFilter(effect.Definition))
                continue;

            if (_visibleEffects.Count < maximum)
                _visibleEffects.Add(effect);
            else
                _overflowCount++;
        }
    }

    private bool PassesVisibilityFilter(DebuffDefinition definition)
    {
        return Settings.VisibilityFilter.Value switch
        {
            1 => GetEffectivePriority(definition) >= DebuffPriority.Major,
            2 => GetEffectivePriority(definition) == DebuffPriority.Critical,
            _ => true,
        };
    }

    private bool IsEffectEnabled(DebuffDefinition definition)
    {
        if (Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.Enabled.HasValue)
            return userOverride.Enabled.Value;

        return definition.DefaultEnabled;
    }

    private DebuffPriority GetEffectivePriority(DebuffDefinition definition)
    {
        return Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.Priority.HasValue
            ? userOverride.Priority.Value
            : definition.Priority;
    }

    private bool GetEffectiveShowTimer(DebuffDefinition definition)
    {
        return Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.ShowTimer.HasValue
            ? userOverride.ShowTimer.Value
            : definition.ShowTimer;
    }

    private bool GetEffectiveShowIcon(DebuffDefinition definition)
    {
        return Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.ShowIcon.HasValue
            ? userOverride.ShowIcon.Value
            : true;
    }

    private bool GetEffectiveInitialAlert(DebuffDefinition definition)
    {
        return Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.InitialAlert.HasValue
            ? userOverride.InitialAlert.Value
            : definition.DefaultInitialAlert;
    }

    private bool GetEffectiveSound(DebuffDefinition definition)
    {
        return Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) && userOverride.Sound.HasValue
            ? userOverride.Sound.Value
            : definition.DefaultSound;
    }

    private void TryPlayEffectSound(ActiveTrackedEffect effect)
    {
        if (!Settings.EnableSoundAlerts.Value || !GetEffectiveSound(effect.Definition))
            return;

        var soundName = GetEffectivePriority(effect.Definition) == DebuffPriority.Critical ? "danger.wav" : "attention.wav";
        var soundData = GetEmbeddedSoundData(soundName);
        if (soundData == null || soundData.Length == 0)
            return;

        try
        {
            if (!PlayMemorySound(soundData, IntPtr.Zero, SoundAsync | SoundMemory | SoundNoDefault))
                ReportSoundError("Windows could not play the embedded DebuffLens2 sound: " + soundName);
        }
        catch (Exception exception)
        {
            ReportSoundError("Sound alert failed safely: " + exception.GetBaseException().Message);
        }
    }

    private void ReportSoundError(string message)
    {
        if (string.Equals(message, _lastSoundError, StringComparison.Ordinal))
            return;

        _lastSoundError = message;
        DebugWindow.LogError("DebuffLens2: " + message);
    }

    private byte[] GetEmbeddedSoundData(string soundName)
    {
        if (string.Equals(soundName, "danger.wav", StringComparison.OrdinalIgnoreCase) && _dangerSoundData != null)
            return _dangerSoundData;
        if (string.Equals(soundName, "attention.wav", StringComparison.OrdinalIgnoreCase) && _attentionSoundData != null)
            return _attentionSoundData;

        try
        {
            var resourceName = "DebuffLens2.assets." + soundName;
            using var source = GetType().Assembly.GetManifestResourceStream(resourceName);
            if (source == null)
            {
                ReportSoundError("Embedded DebuffLens2 sound resource was not found: " + soundName);
                return null;
            }

            using var destination = new MemoryStream();
            source.CopyTo(destination);
            var soundData = destination.ToArray();

            if (string.Equals(soundName, "danger.wav", StringComparison.OrdinalIgnoreCase))
                _dangerSoundData = soundData;
            else if (string.Equals(soundName, "attention.wav", StringComparison.OrdinalIgnoreCase))
                _attentionSoundData = soundData;

            return soundData;
        }
        catch (Exception exception)
        {
            ReportSoundError("Could not load embedded sound '" + soundName + "': " + exception.GetBaseException().Message);
            return null;
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundA", SetLastError = true)]
    private static extern bool PlayMemorySound(byte[] sound, IntPtr module, int flags);

    private const int SoundAsync = 0x0001;
    private const int SoundNoDefault = 0x0002;
    private const int SoundMemory = 0x0004;

    private string GetEffectiveCompactName(DebuffDefinition definition)
    {
        if (Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride) &&
            !string.IsNullOrWhiteSpace(userOverride.CompactName))
            return userOverride.CompactName;

        return string.IsNullOrWhiteSpace(definition.CompactName) ? definition.DisplayName : definition.CompactName;
    }

    private void HandleOverlayDrag()
    {
        if (!Settings.AllowDragging.Value || Settings.LockPosition.Value || !_hasHudBounds)
        {
            _isDragging = false;
            return;
        }

        var size = new Vector2(_hudBounds.Width, _hudBounds.Height);
        if (size.X <= 0 || size.Y <= 0)
        {
            _isDragging = false;
            return;
        }

        ImGui.SetNextWindowPos(new Vector2(_hudBounds.X, _hudBounds.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var flags = ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoBringToFrontOnFocus |
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav;

        ImGui.Begin("##DebuffLens2DragSurface", flags);
        ImGui.InvisibleButton("##DebuffLens2DragTarget", size);

        var mousePosition = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _isDragging = true;
            _dragOffset = mousePosition - Settings.OverlayPosition.Value;
        }

        if (_isDragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var movedPosition = mousePosition - _dragOffset;
            movedPosition.X = Math.Max(0, movedPosition.X);
            movedPosition.Y = Math.Max(0, movedPosition.Y);
            Settings.OverlayPosition.Value = movedPosition;
        }
        else if (_isDragging)
        {
            _isDragging = false;
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawCompactHud(Vector2 position)
    {
        var detailedIcons = Settings.DisplayStyle.Value == "Detailed Icons";
        DrawIconOnlyHud(position, detailedIcons);
    }

    // Legacy pill renderer retained only for source compatibility. The public HUD
    // now exposes Compact Icons and Detailed Icons exclusively.
    private void DrawLegacyCompactHud(Vector2 position)
    {
        _pillLayouts.Clear();

        var overallScale = Settings.Appearance.OverallScale.Value;
        var iconSize = Settings.IconSize.Value * overallScale;
        var padding = Settings.Appearance.Padding.Value * overallScale;
        var spacing = Settings.Appearance.Spacing.Value * overallScale;
        var accentWidth = Math.Max(2f, 3f * overallScale);
        var textGap = 7f * overallScale;
        var cursor = position;
        var maxRight = position.X;
        var maxBottom = position.Y;
        var verticalLayout = Settings.VerticalLayout.Value;

        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * overallScale))
        {
            foreach (var effect in _visibleEffects)
            {
                var definition = effect.Definition;
                var label = GetEffectiveCompactName(definition).ToUpperInvariant();
                var detail = GetCompactDetail(effect);
                var labelSize = Graphics.MeasureText(label);
                var detailWidth = GetReservedDetailWidth(effect, detail);
                var subtext = string.Empty;
                var height = Math.Max(Settings.PillHeight.Value * overallScale, Math.Max(iconSize, labelSize.Y) + padding * 2f);
                var showIcon = GetEffectiveShowIcon(definition);
                var width = accentWidth + padding * 2f + labelSize.X;
                if (showIcon)
                    width += iconSize + textGap;

                if (detailWidth > 0)
                    width += textGap + detailWidth;

                var bounds = new RectangleF(cursor.X, cursor.Y, width, height);
                var layout = new PillLayout(effect, bounds, label, detail, subtext, GetAccentColor(definition));
                _pillLayouts.Add(layout);

                DrawPill(layout, iconSize, padding, accentWidth, textGap);
                maxRight = Math.Max(maxRight, bounds.Right);
                maxBottom = Math.Max(maxBottom, bounds.Bottom);

                if (verticalLayout)
                    cursor.Y += height + spacing;
                else
                    cursor.X += width + spacing;
            }

            if (_overflowCount > 0)
            {
                var overflowLabel = "+" + _overflowCount.ToString(CultureInfo.InvariantCulture);
                var labelSize = Graphics.MeasureText(overflowLabel);
                var height = Math.Max(Settings.PillHeight.Value * overallScale, labelSize.Y + padding * 2f);
                var width = accentWidth + padding * 2f + labelSize.X;
                var bounds = new RectangleF(cursor.X, cursor.Y, width, height);
                DrawOverflowPill(bounds, overflowLabel, GetAccentColorForPriority(DebuffPriority.Minor), padding, accentWidth);
                maxRight = Math.Max(maxRight, bounds.Right);
                maxBottom = Math.Max(maxBottom, bounds.Bottom);
            }
        }

        _hasHudBounds = _pillLayouts.Count > 0;
        if (_hasHudBounds)
            _hudBounds = new RectangleF(position.X, position.Y, maxRight - position.X, maxBottom - position.Y);
    }

    private void DrawPill(PillLayout layout, float iconSize, float padding, float accentWidth, float textGap)
    {
        var bounds = layout.Bounds;
        var background = WithOpacity(Settings.Appearance.BackgroundColor.Value, Settings.Appearance.BackgroundOpacity.Value);
        var topLeft = new Vector2(bounds.X, bounds.Y);
        var bottomRight = new Vector2(bounds.Right, bounds.Bottom);

        Graphics.DrawBox(topLeft, bottomRight, background);
        Graphics.DrawBox(topLeft, new Vector2(bounds.X + accentWidth, bounds.Bottom), layout.Accent);

        var drawIcon = GetEffectiveShowIcon(layout.Effect.Definition);
        var iconRect = new RectangleF(
            bounds.X + accentWidth + padding,
            bounds.Y + (bounds.Height - iconSize) / 2f,
            iconSize,
            iconSize);

        if (drawIcon && EnsureIconTextureLoaded(layout.Effect.Definition.Icon))
            Graphics.DrawImage(IconTextureKey(layout.Effect.Definition.Icon), iconRect);
        else if (drawIcon)
            Graphics.DrawBox(iconRect.TopLeft, iconRect.BottomRight, layout.Accent);

        var labelSize = Graphics.MeasureText(layout.Label);
        var textPosition = new Vector2(
            drawIcon ? iconRect.Right + textGap : bounds.X + accentWidth + padding,
            bounds.Y + (bounds.Height - labelSize.Y) / 2f);
        Graphics.DrawText(layout.Label, textPosition, Settings.Appearance.TextColor.Value);

        if (!string.IsNullOrWhiteSpace(layout.Subtext))
        {
            using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * Settings.Appearance.OverallScale.Value * 0.72f))
            {
                var subtextY = textPosition.Y + labelSize.Y + 2f * Settings.Appearance.OverallScale.Value;
                Graphics.DrawText(layout.Subtext, new Vector2(textPosition.X, subtextY), layout.Accent);
            }
        }

        if (!string.IsNullOrWhiteSpace(layout.Detail))
        {
            var detailSize = Graphics.MeasureText(layout.Detail);
            var detailPosition = new Vector2(
                bounds.Right - padding - detailSize.X,
                bounds.Y + (bounds.Height - detailSize.Y) / 2f);
            Graphics.DrawText(layout.Detail, detailPosition, Settings.Appearance.TimerColor.Value);
        }
    }

    private void DrawIconOnlyHud(Vector2 position, bool detailedIcons)
    {
        _pillLayouts.Clear();

        var overallScale = Settings.Appearance.OverallScale.Value;
        var iconSize = Settings.IconsOnly.Size.Value * overallScale;
        var spacing = Math.Max(3f, Settings.Appearance.Spacing.Value) * overallScale;
        var labelGap = Math.Max(2f, 4f * overallScale);
        var labelScale = Settings.Appearance.FontScale.Value * overallScale * Settings.IconsOnly.LabelScale.Value;
        var cursor = position;
        var maxRight = position.X;
        var maxBottom = position.Y;
        var isVertical = detailedIcons || Settings.VerticalLayout.Value;
        var showIconNames = Settings.IconsOnly.Labels.Value;
        var verticalCellWidth = iconSize;
        var verticalLabelHeight = 0f;

        if (isVertical && Settings.IconsOnly.VerticalDescriptions.Value)
        {
            DrawVerticalIconDescriptionHud(position, iconSize, spacing, labelScale);
            return;
        }

        // Vertical icon-only mode uses a common cell width based on the longest
        // active full label. This keeps every icon and label on one exact axis.
        if (isVertical && showIconNames)
        {
            for (var i = 0; i < _visibleEffects.Count; i++)
            {
                var fullLabel = GetEffectiveCompactName(_visibleEffects[i].Definition).ToUpperInvariant();
                var fullLabelSize = MeasureTextAtScale(fullLabel, labelScale);
                verticalCellWidth = Math.Max(verticalCellWidth, fullLabelSize.X);
                verticalLabelHeight = Math.Max(verticalLabelHeight, fullLabelSize.Y);
            }

            if (_overflowCount > 0)
            {
                var overflowLabelSize = MeasureTextAtScale("MORE", labelScale);
                verticalCellWidth = Math.Max(verticalCellWidth, overflowLabelSize.X);
                verticalLabelHeight = Math.Max(verticalLabelHeight, overflowLabelSize.Y);
            }
        }

        foreach (var effect in _visibleEffects)
        {
            var label = showIconNames
                ? GetEffectiveCompactName(effect.Definition).ToUpperInvariant()
                : string.Empty;
            if (!isVertical)
            {
                var maxLabelWidth = iconSize * 1.65f;
                label = TruncateTextToWidth(label, maxLabelWidth, labelScale);
            }

            var labelSize = string.IsNullOrWhiteSpace(label)
                ? Vector2.Zero
                : MeasureTextAtScale(label, labelScale);
            var cellWidth = isVertical ? verticalCellWidth : Math.Max(iconSize, labelSize.X);
            var visibleLabelHeight = isVertical ? verticalLabelHeight : labelSize.Y;
            var cellHeight = iconSize + (visibleLabelHeight > 0f ? labelGap + visibleLabelHeight : 0f);
            var bounds = new RectangleF(cursor.X, cursor.Y, cellWidth, cellHeight);
            var layout = new PillLayout(effect, bounds, label, string.Empty, string.Empty, GetAccentColor(effect.Definition));
            _pillLayouts.Add(layout);
            DrawIconOnlyCard(layout, iconSize, labelScale, labelGap);
            maxRight = Math.Max(maxRight, bounds.Right);
            maxBottom = Math.Max(maxBottom, bounds.Bottom);

            if (isVertical)
                cursor.Y += cellHeight + spacing;
            else
                cursor.X += cellWidth + spacing;
        }

        if (_overflowCount > 0)
        {
            var label = showIconNames ? "MORE" : string.Empty;
            var labelSize = string.IsNullOrWhiteSpace(label)
                ? Vector2.Zero
                : MeasureTextAtScale(label, labelScale);
            var cellWidth = isVertical ? verticalCellWidth : Math.Max(iconSize, labelSize.X);
            var visibleLabelHeight = isVertical ? verticalLabelHeight : labelSize.Y;
            var cellHeight = iconSize + (visibleLabelHeight > 0f ? labelGap + visibleLabelHeight : 0f);
            var bounds = new RectangleF(cursor.X, cursor.Y, cellWidth, cellHeight);
            DrawIconOnlyOverflow(bounds, _overflowCount, iconSize, label, labelScale, labelGap);
            maxRight = Math.Max(maxRight, bounds.Right);
            maxBottom = Math.Max(maxBottom, bounds.Bottom);
        }

        _hasHudBounds = _pillLayouts.Count > 0;
        if (_hasHudBounds)
            _hudBounds = new RectangleF(position.X, position.Y, maxRight - position.X, maxBottom - position.Y);
    }

    private void DrawVerticalIconDescriptionHud(Vector2 position, float iconSize, float spacing, float labelScale)
    {
        var overallScale = Settings.Appearance.OverallScale.Value;
        var textGap = Math.Max(8f, 12f * overallScale);
        var textWidth = Settings.IconsOnly.DescriptionWidth.Value * overallScale;
        var descriptionScale = Settings.Appearance.FontScale.Value * overallScale * 0.72f;
        var lineGap = Math.Max(1f, 2f * overallScale);
        var blockGap = Math.Max(2f, 5f * overallScale);
        var cursor = position;
        var rowWidth = iconSize + textGap + textWidth;
        var maxBottom = position.Y;

        foreach (var effect in _visibleEffects)
        {
            var definition = effect.Definition;
            var label = Settings.IconsOnly.Labels.Value
                ? GetEffectiveCompactName(definition).ToUpperInvariant()
                : string.Empty;
            var description = GetIconOnlyDescription(definition).ToUpperInvariant();
            var labelLines = GetWrappedIconText(definition.Id + ":label", label, textWidth, labelScale);
            var descriptionLines = GetWrappedIconText(definition.Id + ":description", description, textWidth, descriptionScale);
            var labelLineHeight = labelLines.Length > 0 ? MeasureTextAtScale("Ag", labelScale).Y : 0f;
            var descriptionLineHeight = descriptionLines.Length > 0 ? MeasureTextAtScale("Ag", descriptionScale).Y : 0f;
            var labelBlockHeight = labelLines.Length > 0
                ? labelLines.Length * labelLineHeight + Math.Max(0, labelLines.Length - 1) * lineGap
                : 0f;
            var descriptionBlockHeight = descriptionLines.Length > 0
                ? descriptionLines.Length * descriptionLineHeight + Math.Max(0, descriptionLines.Length - 1) * lineGap
                : 0f;
            var textBlockHeight = labelBlockHeight + descriptionBlockHeight;
            if (labelBlockHeight > 0f && descriptionBlockHeight > 0f)
                textBlockHeight += blockGap;

            var rowHeight = Math.Max(iconSize, textBlockHeight);
            var bounds = new RectangleF(cursor.X, cursor.Y, rowWidth, rowHeight);
            var frameRect = new RectangleF(bounds.X, bounds.Y + (bounds.Height - iconSize) / 2f, iconSize, iconSize);
            var layout = new PillLayout(effect, bounds, label, string.Empty, description, GetAccentColor(definition));
            _pillLayouts.Add(layout);
            DrawIconTile(frameRect, effect);

            var textX = frameRect.Right + textGap;
            var textY = bounds.Y + (bounds.Height - textBlockHeight) / 2f;
            if (labelLines.Length > 0)
            {
                using (Graphics.SetTextScale(labelScale))
                {
                    for (var i = 0; i < labelLines.Length; i++)
                    {
                        Graphics.DrawText(labelLines[i], new Vector2(textX, textY), layout.Accent);
                        textY += labelLineHeight + lineGap;
                    }
                }

                if (descriptionLines.Length > 0)
                    textY += blockGap - lineGap;
            }

            if (descriptionLines.Length > 0)
            {
                using (Graphics.SetTextScale(descriptionScale))
                {
                    for (var i = 0; i < descriptionLines.Length; i++)
                    {
                        Graphics.DrawText(descriptionLines[i], new Vector2(textX, textY), Settings.IconsOnly.DescriptionColor.Value);
                        textY += descriptionLineHeight + lineGap;
                    }
                }
            }

            cursor.Y += rowHeight + spacing;
            maxBottom = Math.Max(maxBottom, bounds.Bottom);
        }

        if (_overflowCount > 0)
        {
            var bounds = new RectangleF(cursor.X, cursor.Y, rowWidth, iconSize);
            var iconBounds = new RectangleF(bounds.X, bounds.Y, iconSize, iconSize);
            DrawIconOnlyOverflow(iconBounds, _overflowCount, iconSize, string.Empty, labelScale, 0f);
            var moreSize = MeasureTextAtScale("MORE", labelScale);
            using (Graphics.SetTextScale(labelScale))
                Graphics.DrawText("MORE", new Vector2(iconBounds.Right + textGap, bounds.Y + (bounds.Height - moreSize.Y) / 2f), Settings.IconsOnly.LabelColor.Value);
            maxBottom = Math.Max(maxBottom, bounds.Bottom);
        }

        _hasHudBounds = _pillLayouts.Count > 0;
        if (_hasHudBounds)
            _hudBounds = new RectangleF(position.X, position.Y, rowWidth, maxBottom - position.Y);
    }

    private void DrawIconOnlyCard(PillLayout layout, float iconSize, float labelScale, float labelGap)
    {
        var bounds = layout.Bounds;
        var frameRect = new RectangleF(bounds.X + (bounds.Width - iconSize) / 2f, bounds.Y, iconSize, iconSize);
        DrawIconTile(frameRect, layout.Effect);

        if (!string.IsNullOrWhiteSpace(layout.Label))
        {
            using (Graphics.SetTextScale(labelScale))
            {
                var labelSize = Graphics.MeasureText(layout.Label);
                Graphics.DrawText(
                    layout.Label,
                    new Vector2(bounds.X + (bounds.Width - labelSize.X) / 2f, frameRect.Bottom + labelGap),
                    WithOpacity(Settings.IconsOnly.LabelColor.Value, 255));
            }
        }
    }

    private void DrawIconTile(RectangleF frameRect, ActiveTrackedEffect effect)
    {
        var inset = Math.Max(2f, 3f * Settings.Appearance.OverallScale.Value);
        var iconRect = new RectangleF(frameRect.X + inset, frameRect.Y + inset, frameRect.Width - inset * 2f, frameRect.Height - inset * 2f);
        var frameThickness = Math.Max(2, (int)Math.Round(Settings.Appearance.OverallScale.Value * 1.5f));
        var priority = GetEffectivePriority(effect.Definition);
        var priorityColor = GetIconPriorityColor(priority);

        Graphics.DrawBox(frameRect.TopLeft, frameRect.BottomRight, WithOpacity(Settings.Appearance.BackgroundColor.Value, Settings.Appearance.BackgroundOpacity.Value));
        if (GetEffectiveShowIcon(effect.Definition) && EnsureIconTextureLoaded(effect.Definition.Icon))
            Graphics.DrawImage(IconTextureKey(effect.Definition.Icon), iconRect);
        else if (GetEffectiveShowIcon(effect.Definition))
            Graphics.DrawBox(iconRect.TopLeft, iconRect.BottomRight, WithOpacity(GetAccentColor(effect.Definition), 165));

        DrawIconCountdownMask(iconRect, effect);
        DrawIconTimerText(iconRect, effect);

        Graphics.DrawFrame(frameRect, Color.FromArgb(245, 0, 0, 0), frameThickness);

        if (Settings.IconsOnly.PriorityVisualEffects.Value)
            DrawPriorityApplicationEffect(frameRect, effect, priority, priorityColor);
        else if (priority != DebuffPriority.Minor)
            DrawIconPriorityCornerMarker(frameRect, priorityColor);
    }

    private void DrawIconPriorityCornerMarker(RectangleF frameRect, Color color)
    {
        var inset = Math.Max(2f, Settings.Appearance.OverallScale.Value * 2f);
        var size = Math.Max(6f, frameRect.Width * 0.18f);
        var drawList = ImGui.GetForegroundDrawList();
        var topLeft = new Vector2(frameRect.X + inset, frameRect.Y + inset);
        var topRight = new Vector2(topLeft.X + size, topLeft.Y);
        var bottomLeft = new Vector2(topLeft.X, topLeft.Y + size);
        drawList.AddTriangleFilled(topLeft, topRight, bottomLeft, ToImGuiColor(color));
    }

    private void DrawPriorityApplicationEffect(RectangleF frameRect, ActiveTrackedEffect effect, DebuffPriority priority, Color color)
    {
        if (priority == DebuffPriority.Minor || effect.AppliedAt <= 0)
            return;

        var elapsed = (Environment.TickCount64 - effect.AppliedAt) / 1000f;
        const float duration = 0.9f;
        if (elapsed < 0f || elapsed > duration)
            return;

        var overallScale = Settings.Appearance.OverallScale.Value;
        var fade = 1f - elapsed / duration;
        var frameThickness = Math.Max(2, (int)Math.Round(overallScale * 1.5f));
        if (priority == DebuffPriority.Critical)
        {
            Graphics.DrawFrame(frameRect, color, frameThickness);
            var pulse = 0.5f + 0.5f * MathF.Sin(elapsed * MathF.PI * 7f);
            var expansion = (2f + pulse * 5f) * overallScale;
            var pulseBounds = ExpandRectangle(frameRect, expansion);
            var alpha = (int)Math.Round((90f + pulse * 130f) * fade);
            Graphics.DrawFrame(pulseBounds, WithOpacity(color, alpha), Math.Max(2, (int)Math.Round(2f * overallScale)));
            return;
        }

        // Major effects receive a short, steadily fading halo rather than a pulse.
        Graphics.DrawFrame(frameRect, color, frameThickness);
        for (var layer = 1; layer <= 4; layer++)
        {
            var expansion = layer * 2.1f * overallScale;
            var alpha = (int)Math.Round(fade * (115f - layer * 18f));
            Graphics.DrawFrame(ExpandRectangle(frameRect, expansion), WithOpacity(color, alpha), 1);
        }
    }

    private void DrawIconTimerText(RectangleF iconRect, ActiveTrackedEffect effect)
    {
        if (!Settings.IconsOnly.TimerText.Value ||
            !GetEffectiveShowTimer(effect.Definition) ||
            !IsMeaningfulTime(effect.TimeLeft))
            return;

        var text = FormatIconTimer(effect.TimeLeft);
        var textScale = Settings.Appearance.FontScale.Value * Settings.Appearance.OverallScale.Value * Settings.IconsOnly.TimerTextScale.Value * 1.1f;
        using (Graphics.SetTextScale(textScale))
        {
            var textSize = Graphics.MeasureText(text);
            var position = new Vector2(
                iconRect.X + (iconRect.Width - textSize.X) / 2f,
                iconRect.Y + (iconRect.Height - textSize.Y) / 2f);
            var shadow = Color.FromArgb(245, 0, 0, 0);
            var shadowOffset = Math.Max(1.25f, Settings.Appearance.OverallScale.Value * 1.25f);
            Graphics.DrawText(text, new Vector2(position.X - shadowOffset, position.Y), shadow);
            Graphics.DrawText(text, new Vector2(position.X + shadowOffset, position.Y), shadow);
            Graphics.DrawText(text, new Vector2(position.X, position.Y - shadowOffset), shadow);
            Graphics.DrawText(text, new Vector2(position.X, position.Y + shadowOffset), shadow);
            Graphics.DrawText(text, new Vector2(position.X - shadowOffset, position.Y - shadowOffset), shadow);
            Graphics.DrawText(text, new Vector2(position.X + shadowOffset, position.Y - shadowOffset), shadow);
            Graphics.DrawText(text, new Vector2(position.X - shadowOffset, position.Y + shadowOffset), shadow);
            Graphics.DrawText(text, new Vector2(position.X + shadowOffset, position.Y + shadowOffset), shadow);
            Graphics.DrawText(text, position, Settings.IconsOnly.TimerColor.Value);
        }
    }

    private void DrawIconOnlyOverflow(RectangleF bounds, int overflowCount, float iconSize, string label, float labelScale, float labelGap)
    {
        var accent = GetAccentColorForPriority(DebuffPriority.Minor);
        var frameRect = new RectangleF(bounds.X + (bounds.Width - iconSize) / 2f, bounds.Y, iconSize, iconSize);
        Graphics.DrawBox(frameRect.TopLeft, frameRect.BottomRight, WithOpacity(Settings.Appearance.BackgroundColor.Value, Settings.Appearance.BackgroundOpacity.Value));
        Graphics.DrawFrame(frameRect, WithOpacity(accent, 150), Math.Max(1, (int)Math.Round(Settings.Appearance.OverallScale.Value)));
        var text = "+" + overflowCount.ToString(CultureInfo.InvariantCulture);
        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * Settings.Appearance.OverallScale.Value * 0.92f))
        {
            var textSize = Graphics.MeasureText(text);
            Graphics.DrawText(text, new Vector2(frameRect.X + (frameRect.Width - textSize.X) / 2f, frameRect.Y + (frameRect.Height - textSize.Y) / 2f), Settings.Appearance.TextColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            using (Graphics.SetTextScale(labelScale))
            {
                var labelSize = Graphics.MeasureText(label);
                Graphics.DrawText(
                    label,
                    new Vector2(bounds.X + (bounds.Width - labelSize.X) / 2f, frameRect.Bottom + labelGap),
                    WithOpacity(Settings.IconsOnly.LabelColor.Value, 255));
            }
        }
    }

    private void DrawIconCountdownMask(RectangleF iconRect, ActiveTrackedEffect effect)
    {
        if (!Settings.IconsOnly.CountdownDial.Value ||
            !GetEffectiveShowTimer(effect.Definition) ||
            !TryGetDurationFraction(effect, out var fraction))
            return;

        const float startAngle = -MathF.PI / 2f;
        const float fullCircle = MathF.PI * 2f;
        var center = new Vector2(iconRect.X + iconRect.Width / 2f, iconRect.Y + iconRect.Height / 2f);

        // This is a single WoW-style cooldown mask: no ring, hand, or triangle-fan
        // tick lines. Each section is a solid convex fill; at most four are needed
        // for a full sweep, and the live leading edge remains exact and smooth.
        var halfWidth = Math.Max(3f, iconRect.Width / 2f - 2f);
        var halfHeight = Math.Max(3f, iconRect.Height / 2f - 2f);
        var elapsedColor = Color.FromArgb(178, 0, 0, 0);
        var drawList = ImGui.GetForegroundDrawList();
        var elapsedColorU32 = ToImGuiColor(elapsedColor);
        var sectionStart = fraction;
        while (sectionStart < 0.9999f)
        {
            var nextQuarterBoundary = (MathF.Floor(sectionStart * 4f) + 1f) / 4f;
            var sectionEnd = Math.Min(1f, nextQuarterBoundary);
            DrawCooldownMaskSection(
                drawList,
                center,
                halfWidth,
                halfHeight,
                startAngle,
                fullCircle,
                sectionStart,
                sectionEnd,
                elapsedColorU32);
            sectionStart = sectionEnd;
        }
    }

    private void DrawCooldownMaskSection(
        ImDrawListPtr drawList,
        Vector2 center,
        float halfWidth,
        float halfHeight,
        float startAngle,
        float fullCircle,
        float startProgress,
        float endProgress,
        uint color)
    {
        var pointCount = 0;
        _cooldownMaskPoints[pointCount++] = center;
        _cooldownMaskPoints[pointCount++] = GetIconEdgePoint(
            center,
            halfWidth,
            halfHeight,
            startAngle - fullCircle * startProgress);

        // A square icon has corners at 12.5%, 37.5%, 62.5%, and 87.5% around
        // its sweep. Including a crossed corner preserves a complete square mask.
        for (var cornerProgress = 0.125f; cornerProgress < 1f; cornerProgress += 0.25f)
        {
            if (cornerProgress > startProgress + 0.0001f && cornerProgress < endProgress - 0.0001f)
            {
                _cooldownMaskPoints[pointCount++] = GetIconEdgePoint(
                    center,
                    halfWidth,
                    halfHeight,
                    startAngle - fullCircle * cornerProgress);
            }
        }

        _cooldownMaskPoints[pointCount++] = GetIconEdgePoint(
            center,
            halfWidth,
            halfHeight,
            startAngle - fullCircle * endProgress);

        drawList.AddConvexPolyFilled(ref _cooldownMaskPoints[0], pointCount, color);
    }

    private bool TryGetDurationFraction(ActiveTrackedEffect effect, out float fraction)
    {
        fraction = 0f;
        if (!IsMeaningfulTime(effect.TimeLeft))
            return false;

        var duration = IsMeaningfulTime(effect.MaxTime) && effect.MaxTime > 0d
            ? effect.MaxTime
            : _dialDurationByDefinition.TryGetValue(effect.Definition.Id, out var observedDuration)
                ? observedDuration
                : 0d;

        if (!IsMeaningfulTime(duration) || duration <= 0d)
            return false;

        fraction = Math.Clamp((float)(effect.TimeLeft / duration), 0f, 1f);
        return true;
    }

    private static Vector2 GetIconEdgePoint(Vector2 center, float halfWidth, float halfHeight, float angle)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var distanceToEdge = Math.Min(
            halfWidth / Math.Max(0.001f, Math.Abs(direction.X)),
            halfHeight / Math.Max(0.001f, Math.Abs(direction.Y)));
        return center + direction * distanceToEdge;
    }

    private static uint ToImGuiColor(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16) | (color.A << 24));
    }

    private string GetDetailedDescription(DebuffDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.DetailedDescription))
            return definition.DetailedDescription;

        if (!string.IsNullOrWhiteSpace(definition.Description))
            return definition.Description;

        return definition.ShortDescription;
    }

    private static string GetIconOnlyDescription(DebuffDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.ShortDescription))
            return definition.ShortDescription.Trim().TrimEnd('.');

        if (!string.IsNullOrWhiteSpace(definition.DetailedDescription))
            return definition.DetailedDescription.Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(definition.Description)
            ? string.Empty
            : definition.Description.Trim().TrimEnd('.');
    }

    private string[] GetWrappedIconText(string cacheKey, string text, float maxWidth, float textScale)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        if (_wrappedIconDescriptionCache.TryGetValue(cacheKey, out var cached) &&
            string.Equals(cached.Source, text, StringComparison.Ordinal) &&
            Math.Abs(cached.MaxWidth - maxWidth) < 0.1f &&
            Math.Abs(cached.TextScale - textScale) < 0.001f)
        {
            return cached.Lines;
        }

        var lines = new List<string>();
        var words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        using (Graphics.SetTextScale(textScale))
        {
            var currentLine = string.Empty;
            for (var i = 0; i < words.Length; i++)
            {
                var candidate = string.IsNullOrEmpty(currentLine)
                    ? words[i]
                    : currentLine + " " + words[i];
                if (string.IsNullOrEmpty(currentLine) || Graphics.MeasureText(candidate).X <= maxWidth)
                {
                    currentLine = candidate;
                    continue;
                }

                lines.Add(currentLine);
                currentLine = words[i];
            }

            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);
        }

        var wrapped = lines.ToArray();
        _wrappedIconDescriptionCache[cacheKey] = new WrappedTextCacheEntry(text, maxWidth, textScale, wrapped);
        return wrapped;
    }

    private string TruncateTextToWidth(string text, float maxWidth, float textScale)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        using (Graphics.SetTextScale(textScale))
        {
            if (Graphics.MeasureText(text).X <= maxWidth)
                return text;

            const string ellipsis = "...";
            var end = text.Length;
            while (end > 1)
            {
                var candidate = text.Substring(0, end).TrimEnd() + ellipsis;
                if (Graphics.MeasureText(candidate).X <= maxWidth)
                    return candidate;
                end--;
            }
        }

        return "...";
    }

    private string GetCompactDetail(ActiveTrackedEffect effect)
    {
        var definition = effect.Definition;
        var timer = GetEffectiveShowTimer(definition) && IsMeaningfulTime(effect.TimeLeft)
            ? FormatSeconds(effect.TimeLeft)
            : string.Empty;
        var stacks = definition.ShowStacks && effect.Stacks > 1
            ? "x" + effect.Stacks.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(timer))
            return stacks;
        if (string.IsNullOrWhiteSpace(stacks))
            return timer;
        return timer + " " + stacks;
    }

    private float GetReservedDetailWidth(ActiveTrackedEffect effect, string detail)
    {
        var width = string.IsNullOrWhiteSpace(detail) ? 0f : Graphics.MeasureText(detail).X;
        if (GetEffectiveShowTimer(effect.Definition) && IsMeaningfulTime(effect.TimeLeft))
            width = Math.Max(width, Graphics.MeasureText("88.8s").X);

        return width;
    }

    private void DrawInitialAlert(Vector2 hudPosition)
    {
        var now = Environment.TickCount64;
        ActiveTrackedEffect alert = null;
        for (var i = 0; i < _activeEffects.Count; i++)
        {
            var candidate = _activeEffects[i];
            var isDrowning = string.Equals(candidate.Definition.Id, "drowning", StringComparison.Ordinal);
            var elapsedSeconds = (now - candidate.AppliedAt) / 1000f;
            if ((!isDrowning && !Settings.ShowNewDebuffPopups.Value) ||
                !GetEffectiveInitialAlert(candidate.Definition) ||
                (!isDrowning && (candidate.AppliedAt <= 0 ||
                                 elapsedSeconds > Settings.InitialAlertDurationSeconds.Value * candidate.Definition.InitialAlertDurationMultiplier)))
                continue;

            if (alert == null ||
                GetEffectivePriority(candidate.Definition) > GetEffectivePriority(alert.Definition) ||
                GetEffectivePriority(candidate.Definition) == GetEffectivePriority(alert.Definition) && candidate.Definition.InitialAlertScale > alert.Definition.InitialAlertScale)
                alert = candidate;
        }

        if (alert == null)
            return;

        var definition = alert.Definition;
        var elapsed = (now - alert.AppliedAt) / 1000f;
        var pulse = 1f + 0.05f * (float)Math.Sin(elapsed * 22f);
        var criticalScale = GetEffectivePriority(definition) == DebuffPriority.Critical ? Settings.CriticalAlertScale.Value : 1f;
        var scale = Settings.Appearance.OverallScale.Value * definition.InitialAlertScale * criticalScale * pulse;
        var title = definition.DisplayName.ToUpperInvariant();
        var message = string.IsNullOrWhiteSpace(definition.InitialAlertText)
            ? definition.ShortDescription.ToUpperInvariant()
            : definition.InitialAlertText.ToUpperInvariant();
        var padding = 13f * scale;
        Vector2 titleSize;
        Vector2 messageSize;

        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * scale))
            titleSize = Graphics.MeasureText(title);
        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * scale * 0.72f))
            messageSize = Graphics.MeasureText(message);

        var width = Math.Max(titleSize.X, messageSize.X) + padding * 2f;
        var height = titleSize.Y + messageSize.Y + padding * 2.4f;
        var centeredX = _hasHudBounds ? _hudBounds.X + (_hudBounds.Width - width) / 2f : hudPosition.X;
        var position = new Vector2(Math.Max(0, centeredX), Math.Max(0, hudPosition.Y - height - 10f * scale));
        var bounds = new RectangleF(position.X, position.Y, width, height);
        var accent = GetAccentColor(definition);
        var background = Color.FromArgb(242, 24, 8, 10);

        Graphics.DrawBox(bounds.TopLeft, bounds.BottomRight, background);
        Graphics.DrawFrame(bounds, accent, Math.Max(2, (int)(2 * scale)));
        Graphics.DrawBox(bounds.TopLeft, new Vector2(bounds.X + Math.Max(4f, 6f * scale), bounds.Bottom), accent);

        var textLeft = bounds.X + padding + Math.Max(4f, 6f * scale);
        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * scale))
            Graphics.DrawText(title, new Vector2(textLeft, bounds.Y + padding), Settings.Appearance.TextColor.Value);
        using (Graphics.SetTextScale(Settings.Appearance.FontScale.Value * scale * 0.72f))
            Graphics.DrawText(message, new Vector2(textLeft, bounds.Bottom - padding - messageSize.Y), accent);
    }

    private void RebuildRawRenderLines()
    {
        _rawLines.Clear();

        if (_rawEffects.Count == 0)
            return;

        _rawLines.Add(new RenderLine($"RAW PLAYER EFFECTS | {_rawEffects.Count} UNIQUE", Color.FromArgb(255, 127, 219, 255)));
        var count = Math.Min(Settings.Debug.MaxRawEffects.Value, _rawEffects.Count);

        for (var i = 0; i < count; i++)
        {
            var effect = _rawEffects[i];
            var display = string.IsNullOrWhiteSpace(effect.DisplayName) ? "(no display name)" : effect.DisplayName;
            var duration = FormatRawDuration(effect.TimeLeft, effect.MaxTime);
            var charges = effect.Charges > 0 ? $" charges={effect.Charges}" : string.Empty;
            var stacks = effect.Stacks > 0 ? $" stacks={effect.Stacks}" : string.Empty;
            _rawLines.Add(new RenderLine($"{display} | {effect.InternalName} | {duration}{charges}{stacks}", Settings.Debug.RawTextColor.Value));
        }

        if (_rawEffects.Count > count)
            _rawLines.Add(new RenderLine($"+{_rawEffects.Count - count} more (raise Max Raw Effects)", Color.FromArgb(255, 170, 170, 170)));

        if (!string.IsNullOrWhiteSpace(_databaseError))
            _rawLines.Add(new RenderLine($"DATABASE ERROR: {_databaseError}", Settings.Appearance.CriticalColor.Value));
    }

    private void UpdateUnknownEffects()
    {
        _activeUnknownEffects.Clear();
        _unknownNamesThisScan.Clear();

        for (var i = 0; i < _rawEffects.Count; i++)
        {
            var effect = _rawEffects[i];
            if (_definitionByAlias.ContainsKey(effect.InternalName) || _knownCatalogIds.Contains(effect.InternalName))
                continue;

            _activeUnknownEffects.Add(effect);
            _unknownNamesThisScan.Add(effect.InternalName);

            if (Settings.Debug.LogUnknownPlayerEffects.Value &&
                (!_unknownNamesLastScan.Contains(effect.InternalName) || !_unknownEffectLog.ContainsKey(effect.InternalName)))
                RecordUnknownEffect(effect);
        }

        _unknownNamesLastScan.Clear();
        foreach (var name in _unknownNamesThisScan)
            _unknownNamesLastScan.Add(name);
    }

    private void RebuildUnknownRenderLines()
    {
        _unknownLines.Clear();
        if (_activeUnknownEffects.Count == 0)
            return;

        _unknownLines.Add(new RenderLine($"UNKNOWN PLAYER EFFECTS | {_activeUnknownEffects.Count} ACTIVE", Color.FromArgb(255, 255, 126, 126)));
        var count = Math.Min(Settings.Debug.MaxUnknownEffects.Value, _activeUnknownEffects.Count);

        for (var i = 0; i < count; i++)
        {
            var effect = _activeUnknownEffects[i];
            var display = string.IsNullOrWhiteSpace(effect.DisplayName) ? "(no display name)" : effect.DisplayName;
            var details = Settings.Debug.ShowRawRuntimeValues.Value
                ? " | " + FormatRawDuration(effect.TimeLeft, effect.MaxTime) +
                  (effect.Charges > 0 ? " charges=" + effect.Charges.ToString(CultureInfo.InvariantCulture) : string.Empty) +
                  (effect.Stacks > 0 ? " stacks=" + effect.Stacks.ToString(CultureInfo.InvariantCulture) : string.Empty)
                : string.Empty;
            _unknownLines.Add(new RenderLine($"{display} | {effect.InternalName}{details}", Settings.Debug.RawTextColor.Value));
        }

        if (_activeUnknownEffects.Count > count)
            _unknownLines.Add(new RenderLine($"+{_activeUnknownEffects.Count - count} more (raise Max Unknown Effects)", Color.FromArgb(255, 170, 170, 170)));
    }

    private void RecordUnknownEffect(RuntimeEffectSnapshot effect)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (!_unknownEffectLog.TryGetValue(effect.InternalName, out var record))
            {
                record = new UnknownEffectRecord
                {
                    InternalName = effect.InternalName,
                    DisplayName = effect.DisplayName,
                    FirstObservedArea = GetCurrentAreaName(),
                    FirstObservedUtc = now,
                };
                _unknownEffectLog.Add(effect.InternalName, record);
            }

            record.DisplayName = effect.DisplayName;
            record.LastTimer = effect.TimeLeft;
            record.LastMaxTimer = effect.MaxTime;
            record.LastCharges = effect.Charges;
            record.LastStacks = effect.Stacks;
            record.LastObservedUtc = now;
            record.ObservedApplications++;
            SaveUnknownEffectLog();
        }
        catch (Exception exception)
        {
            ReportScanError("unknown-effect log failed safely: " + exception.Message);
        }
    }

    private void LoadUnknownEffectLog()
    {
        _unknownEffectLog.Clear();
        try
        {
            var path = UnknownEffectsPath;
            if (!File.Exists(path))
                return;

            var loaded = JsonConvert.DeserializeObject<List<UnknownEffectRecord>>(File.ReadAllText(path));
            if (loaded == null)
                return;

            for (var i = 0; i < loaded.Count; i++)
            {
                var record = loaded[i];
                if (record == null || string.IsNullOrWhiteSpace(record.InternalName) || _unknownEffectLog.ContainsKey(record.InternalName))
                    continue;
                _unknownEffectLog.Add(record.InternalName, record);
            }
        }
        catch (Exception exception)
        {
            DebugWindow.LogError("DebuffLens2: could not load UnknownEffects.json safely: " + exception.Message);
        }
    }

    private void SaveUnknownEffectLog()
    {
        var path = UnknownEffectsPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var values = new List<UnknownEffectRecord>(_unknownEffectLog.Values);
        values.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.InternalName, right.InternalName));
        File.WriteAllText(path, JsonConvert.SerializeObject(values, Formatting.Indented));
    }

    private string GetUnknownEffectsPath()
    {
        try
        {
            var pluginDirectory = new DirectoryInfo(DirectoryFullName);
            var tempDirectory = pluginDirectory.Parent;
            var pluginsDirectory = tempDirectory?.Parent;
            if (tempDirectory != null && pluginsDirectory != null &&
                string.Equals(tempDirectory.Name, "Temp", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(pluginsDirectory.FullName, "Source", pluginDirectory.Name, "UnknownEffects.json");
        }
        catch
        {
            // Fall back to the current plugin folder if the runtime is not a source-plugin build.
        }

        return Path.Combine(DirectoryFullName, "UnknownEffects.json");
    }

    private string GetCurrentAreaName()
    {
        try
        {
            return GameController.Area?.CurrentArea?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void RebuildNativeBuffUiProbe()
    {
        _nativeBuffUiProbeLines.Clear();
        try
        {
            var ingameState = (object)GameController.IngameState;
            if (ingameState == null)
                return;

            if (!TryGetPublicMemberValue(ingameState, "UIHoverElement", out var hoveredElement) || hoveredElement == null)
            {
                if (TryGetPublicMemberValue(ingameState, "IngameUi", out var ingameUi) && ingameUi != null)
                    TryGetPublicMemberValue(ingameUi, "UIHoverElement", out hoveredElement);
            }

            if (hoveredElement == null)
            {
                _nativeBuffUiProbeLines.Add(new RenderLine(
                    "NATIVE BUFF UI INSPECTOR | No hover element. Hover PoE's own top-left debuff icon or number.",
                    Color.FromArgb(255, 255, 196, 88)));
                return;
            }

            _nativeBuffUiProbeLines.Add(new RenderLine(
                "NATIVE BUFF UI INSPECTOR | Hovering public UI element chain",
                Color.FromArgb(255, 110, 220, 255)));

            AddNativeUiTextSweep(hoveredElement);

            var current = hoveredElement;
            var maxMembers = Settings.Debug.MaxNativeBuffUiProbeMembers.Value;
            var remaining = maxMembers;
            for (var depth = 0; depth < 4 && current != null && remaining > 0; depth++)
            {
                remaining -= AddNativeUiElementDetails(current, depth, remaining);
                if (!TryGetPublicMemberValue(current, "Parent", out var parent) || parent == null || ReferenceEquals(parent, current))
                    break;

                current = parent;
            }
        }
        catch (Exception exception)
        {
            _nativeBuffUiProbeLines.Clear();
            _nativeBuffUiProbeLines.Add(new RenderLine(
                "NATIVE BUFF UI INSPECTOR | unavailable: " + exception.Message,
                Color.FromArgb(255, 255, 130, 130)));
        }
    }

    private void AddNativeUiTextSweep(object hoveredElement)
    {
        var related = new List<NativeUiRelatedElement>();
        AddNativeUiRelatedElement(related, "HOVER", hoveredElement);
        AddNativeUiDescendants(related, hoveredElement, "HOVER", 3, 8, 48);

        _nativeBuffUiProbeLines.Add(new RenderLine(
            "NATIVE UI TEXT SWEEP | Non-empty text below hovered effect icon",
            Color.FromArgb(255, 255, 196, 88)));

        var textCount = 0;
        for (var i = 0; i < related.Count; i++)
        {
            var item = related[i];
            var text = GetNativeUiText(item.Element, "Text");
            var textNoTags = GetNativeUiText(item.Element, "TextNoTags");
            if (text == "<null>" && textNoTags == "<null>")
                continue;

            var index = GetNativeUiText(item.Element, "IndexInParent");
            _nativeBuffUiProbeLines.Add(new RenderLine(
                $"  {item.Label} | {item.Element.GetType().Name} | Text={text} | TextNoTags={textNoTags} | Index={index}",
                Settings.Debug.RawTextColor.Value));
            textCount++;
        }

        if (textCount == 0)
            _nativeBuffUiProbeLines.Add(new RenderLine(
                "  No public Text/TextNoTags found in the first three descendant levels.",
                Settings.Debug.RawTextColor.Value));
    }

    private static string GetNativeUiText(object element, string memberName)
    {
        return TryGetPublicMemberValue(element, memberName, out var value)
            ? FormatProbeValue(value)
            : "<missing>";
    }

    private static void AddNativeUiRelatedElement(List<NativeUiRelatedElement> destination, string label, object element)
    {
        for (var i = 0; i < destination.Count; i++)
        {
            if (ReferenceEquals(destination[i].Element, element))
                return;
        }

        destination.Add(new NativeUiRelatedElement(label, element));
    }

    private static void AddNativeUiDescendants(
        List<NativeUiRelatedElement> destination,
        object root,
        string labelPrefix,
        int maximumDepth,
        int maximumChildrenPerElement,
        int maximumTotal)
    {
        var frontier = new List<NativeUiRelatedElement>
        {
            new(labelPrefix, root)
        };

        for (var depth = 0; depth < maximumDepth && frontier.Count > 0 && destination.Count < maximumTotal; depth++)
        {
            var next = new List<NativeUiRelatedElement>();
            for (var i = 0; i < frontier.Count && destination.Count < maximumTotal; i++)
            {
                var beforeCount = destination.Count;
                AddNativeUiChildren(destination, frontier[i].Element, frontier[i].Label + ".CHILD", maximumChildrenPerElement);
                for (var childIndex = beforeCount; childIndex < destination.Count; childIndex++)
                    next.Add(destination[childIndex]);
            }

            frontier = next;
        }
    }

    private static void AddNativeUiChildren(List<NativeUiRelatedElement> destination, object element, string labelPrefix, int maximum)
    {
        var beforeCount = destination.Count;
        if (TryGetPublicMemberValue(element, "Children", out var children) && children is System.Collections.IEnumerable enumerable && children is not string)
        {
            var index = 0;
            foreach (var child in enumerable)
            {
                if (child != null)
                    AddNativeUiRelatedElement(destination, labelPrefix + "[" + index + "]", child);
                index++;
                if (index >= maximum)
                    return;
            }
        }

        if (destination.Count > beforeCount ||
            !TryGetPublicMemberValue(element, "ChildCount", out var countValue) ||
            countValue is not int childCount || childCount <= 0)
            return;

        var methods = element.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
        MethodInfo childAccessor = null;
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if ((!string.Equals(method.Name, "GetChildAtIndex", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(method.Name, "GetChild", StringComparison.OrdinalIgnoreCase)) ||
                method.GetParameters().Length != 1 ||
                method.GetParameters()[0].ParameterType != typeof(int))
                continue;

            childAccessor = method;
            break;
        }

        if (childAccessor == null)
            return;

        for (var index = 0; index < childCount && index < maximum; index++)
        {
            try
            {
                var child = childAccessor.Invoke(element, new object[] { index });
                if (child != null)
                    AddNativeUiRelatedElement(destination, labelPrefix + "[" + index + "]", child);
            }
            catch
            {
                return;
            }
        }
    }

    private int AddNativeUiElementDetails(object element, int depth, int remaining)
    {
        var type = element.GetType();
        _nativeBuffUiProbeLines.Add(new RenderLine(
            $"  [{depth}] {type.Name}",
            Color.FromArgb(255, 190, 218, 230)));

        var added = 1;
        var members = GetBuffProbeMembers(type);
        for (var i = 0; i < members.Count && added < remaining; i++)
        {
            var member = members[i];
            if (!TryGetProbeValue(member, element, out var value))
                continue;

            _nativeBuffUiProbeLines.Add(new RenderLine(
                $"    {member.Name} = {FormatProbeValue(value)}",
                Settings.Debug.RawTextColor.Value));
            added++;
        }

        return added;
    }

    private static bool TryGetPublicMemberValue(object instance, string name, out object value)
    {
        var type = instance.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        var property = type.GetProperty(name, flags);
        if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            return TryGetProbeValue(property, instance, out value);

        var field = type.GetField(name, flags);
        if (field != null)
            return TryGetProbeValue(field, instance, out value);

        value = null;
        return false;
    }

    private void DrawRawPanel(List<RenderLine> lines, Vector2 position)
    {
        const float padding = 8f;
        using (Graphics.SetTextScale(Settings.Debug.RawTextScale.Value))
        {
            var lineHeight = Graphics.MeasureText("Ag").Y + 3f;
            var width = 0f;

            for (var i = 0; i < lines.Count; i++)
                width = Math.Max(width, Graphics.MeasureText(lines[i].Text).X);

            var bottomRight = position + new Vector2(width + padding * 2f, lineHeight * lines.Count + padding * 2f);
            Graphics.DrawBox(position, bottomRight, WithOpacity(Settings.Appearance.BackgroundColor.Value, Settings.Appearance.BackgroundOpacity.Value));

            var cursor = position + new Vector2(padding, padding);
            for (var i = 0; i < lines.Count; i++)
            {
                Graphics.DrawText(lines[i].Text, cursor, lines[i].Color);
                cursor.Y += lineHeight;
            }
        }
    }

    private void DrawDebuffLibrary()
    {
        var visible = _showDebuffLibrary;
        ImGui.SetNextWindowSize(new Vector2(820, 560), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("DebuffLens2 - Debuff Library", ref visible, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            _showDebuffLibrary = visible;
            return;
        }

        _showDebuffLibrary = visible;
        ImGui.TextUnformatted("These overrides are stored separately from Data/Debuffs.json, so database updates keep your HC choices.");
        ImGui.TextDisabled("Sound plays once on application; it never repeats while the effect remains active.");
        ImGui.SetNextItemWidth(270f);
        ImGui.InputText("Search", ref _librarySearch, 80);
        ImGui.SameLine();
        ImGui.Checkbox("Show full RePoE catalog", ref _showGeneratedLibraryEntries);
        ImGui.Separator();

        ImGui.Columns(2, "DebuffLens2LibraryColumns", true);
        ImGui.SetColumnWidth(0, 285);
        ImGui.BeginChild("##DebuffLens2DefinitionList", new Vector2(0, 0), (ImGuiChildFlags)0);
        for (var i = 0; i < _definitions.Count; i++)
        {
            var definition = _definitions[i];
            if ((definition.GeneratedFromRePoe && !_showGeneratedLibraryEntries) || !MatchesLibraryFilter(definition))
                continue;
            var selected = string.Equals(_selectedDefinitionId, definition.Id, StringComparison.OrdinalIgnoreCase);
            var prefix = IsEffectEnabled(definition) ? "[on] " : "[off] ";
            if (ImGui.Selectable(prefix + definition.DisplayName, selected))
                _selectedDefinitionId = definition.Id;
        }

        ImGui.EndChild();
        ImGui.NextColumn();

        var selectedDefinition = FindSelectedDefinition();
        if (selectedDefinition == null)
        {
            ImGui.TextDisabled("Select a tracked effect to edit its combat presentation.");
        }
        else
        {
            DrawDefinitionEditor(selectedDefinition);
        }

        ImGui.Columns(1);
        ImGui.End();
    }

    private DebuffDefinition FindSelectedDefinition()
    {
        if (string.IsNullOrWhiteSpace(_selectedDefinitionId) && _definitions.Count > 0)
            _selectedDefinitionId = _definitions[0].Id;

        for (var i = 0; i < _definitions.Count; i++)
        {
            if (string.Equals(_definitions[i].Id, _selectedDefinitionId, StringComparison.OrdinalIgnoreCase))
                return _definitions[i];
        }

        return null;
    }

    private bool MatchesLibraryFilter(DebuffDefinition definition)
    {
        var search = _librarySearch?.Trim();
        if (string.IsNullOrWhiteSpace(search))
            return true;
        if (definition.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
            return true;

        for (var i = 0; i < definition.Aliases.Count; i++)
        {
            if (definition.Aliases[i].Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void DrawDefinitionEditor(DebuffDefinition definition)
    {
        var userOverride = GetOrCreateOverride(definition);
        ImGui.TextUnformatted(definition.DisplayName.ToUpperInvariant());
        var sourceLabel = definition.GeneratedFromRePoe ? "RePoE fallback" : "HC curated";
        ImGui.TextDisabled(sourceLabel + "  |  " + definition.Category + "  |  Runtime aliases: " + string.Join(", ", definition.Aliases));
        ImGui.Separator();
        ImGui.TextDisabled("Combat display");
        ImGui.TextWrapped(GetDetailedDescription(definition));
        ImGui.Spacing();
        ImGui.TextDisabled("Full source description");
        ImGui.TextWrapped(definition.Description);
        ImGui.Spacing();

        var enabled = IsEffectEnabled(definition);
        if (ImGui.Checkbox("Enabled", ref enabled))
            userOverride.Enabled = enabled;

        var showIcon = GetEffectiveShowIcon(definition);
        if (ImGui.Checkbox("Show icon", ref showIcon))
            userOverride.ShowIcon = showIcon;

        var showTimer = GetEffectiveShowTimer(definition);
        if (ImGui.Checkbox("Show timer", ref showTimer))
            userOverride.ShowTimer = showTimer;

        var alert = GetEffectiveInitialAlert(definition);
        if (ImGui.Checkbox("Initial popup", ref alert))
            userOverride.InitialAlert = alert;

        var sound = GetEffectiveSound(definition);
        if (ImGui.Checkbox("Play sound on application", ref sound))
            userOverride.Sound = sound;

        ImGui.Spacing();
        ImGui.TextUnformatted("Priority");
        var priority = GetEffectivePriority(definition);
        if (ImGui.BeginCombo("##priority", priority.ToString()))
        {
            foreach (DebuffPriority option in Enum.GetValues(typeof(DebuffPriority)))
            {
                var isSelected = option == priority;
                if (ImGui.Selectable(option.ToString(), isSelected))
                    userOverride.Priority = option;
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.TextUnformatted("Compact name");
        var compactName = GetEffectiveCompactName(definition);
        if (ImGui.InputText("##compactName", ref compactName, 48))
            userOverride.CompactName = compactName.Trim().ToUpperInvariant();

        ImGui.Spacing();
        if (ImGui.Button("Reset this effect to database defaults"))
            Settings.DebuffOverrides.Remove(definition.Id);

        ImGui.SameLine();
        ImGui.TextDisabled(definition.GeneratedFromRePoe
            ? "Exact RePoE source ID; generic Minor presentation"
            : definition.RuntimeVerified
                ? "Runtime verified"
                : "Curated candidate - confirm in live BuffsList");
    }

    private DebuffUserOverride GetOrCreateOverride(DebuffDefinition definition)
    {
        if (Settings.DebuffOverrides.TryGetValue(definition.Id, out var userOverride))
            return userOverride;

        userOverride = new DebuffUserOverride();
        Settings.DebuffOverrides.Add(definition.Id, userOverride);
        return userOverride;
    }

    private Color GetAccentColor(DebuffDefinition definition)
    {
        return definition.Id switch
        {
            "bleeding" => Color.FromArgb(225, 68, 72),
            "maim" => Color.FromArgb(225, 68, 72),
            "ignite" => Color.FromArgb(239, 113, 47),
            "poison" => Color.FromArgb(151, 87, 190),
            "shock" => Color.FromArgb(239, 205, 63),
            "chill" => Color.FromArgb(93, 181, 224),
            "freeze" => Color.FromArgb(145, 213, 244),
            "drought" => Color.FromArgb(245, 140, 55),
            "time_freeze" => Color.FromArgb(120, 197, 255),
            "elemental_exposure" => Color.FromArgb(201, 106, 211),
            "flame_wall" => Color.FromArgb(239, 113, 47),
            "armour_break" => Color.FromArgb(211, 151, 74),
            "fully_broken_armour" => Color.FromArgb(255, 82, 82),
            "drowning" => Color.FromArgb(255, 56, 56),
            "verdant_spores" => Color.FromArgb(128, 201, 89),
            "ground_spores" => Color.FromArgb(159, 183, 90),
            _ => GetAccentColorForPriority(GetEffectivePriority(definition)),
        };
    }

    private Color GetAccentColorForPriority(DebuffPriority priority)
    {
        return priority switch
        {
            DebuffPriority.Critical => Settings.Appearance.CriticalColor.Value,
            DebuffPriority.Major => Settings.Appearance.MajorColor.Value,
            _ => Settings.Appearance.MinorColor.Value,
        };
    }

    private Vector2 MeasureTextAtScale(string text, float scale)
    {
        using (Graphics.SetTextScale(scale))
            return Graphics.MeasureText(text);
    }

    private void DrawOverflowPill(RectangleF bounds, string label, Color accent, float padding, float accentWidth)
    {
        Graphics.DrawBox(bounds.TopLeft, bounds.BottomRight, WithOpacity(Settings.Appearance.BackgroundColor.Value, Settings.Appearance.BackgroundOpacity.Value));
        Graphics.DrawBox(bounds.TopLeft, new Vector2(bounds.X + accentWidth, bounds.Bottom), accent);
        var textSize = Graphics.MeasureText(label);
        var textPosition = new Vector2(bounds.X + accentWidth + padding, bounds.Y + (bounds.Height - textSize.Y) / 2f);
        Graphics.DrawText(label, textPosition, Settings.Appearance.TimerColor.Value);
    }

    private void ClearRuntimeState()
    {
        _rawByName.Clear();
        _rawEffects.Clear();
        _activeByDefinition.Clear();
        _activeEffects.Clear();
        _activeUnknownEffects.Clear();
        _activeSinceByDefinition.Clear();
        _dialDurationByDefinition.Clear();
        _observedDefinitionIds.Clear();
        _inactiveDefinitionIds.Clear();
        _unknownNamesThisScan.Clear();
        _unknownNamesLastScan.Clear();
        _rawLines.Clear();
        _unknownLines.Clear();
        _buffProbeLines.Clear();
        _nativeBuffUiProbeLines.Clear();
        _pillLayouts.Clear();
        _snapshotPoolIndex = 0;
        _activeEffectPoolIndex = 0;
        _hasHudBounds = false;
        _isDragging = false;
    }

    private RuntimeEffectSnapshot RentSnapshot()
    {
        if (_snapshotPoolIndex == _snapshotPool.Count)
            _snapshotPool.Add(new RuntimeEffectSnapshot());

        return _snapshotPool[_snapshotPoolIndex++];
    }

    private ActiveTrackedEffect RentActiveEffect()
    {
        if (_activeEffectPoolIndex == _activeEffectPool.Count)
            _activeEffectPool.Add(new ActiveTrackedEffect());

        return _activeEffectPool[_activeEffectPoolIndex++];
    }

    private void ReportScanError(string message)
    {
        var now = Environment.TickCount64;
        if (string.Equals(message, _lastScanError, StringComparison.Ordinal) && now - _lastScanErrorAt < 5000)
            return;

        _lastScanError = message;
        _lastScanErrorAt = now;
        DebugWindow.LogError($"DebuffLens2: player buff scan failed safely: {message}");
    }

    private static Color WithOpacity(Color color, int opacity)
    {
        return Color.FromArgb(Math.Clamp(opacity, 0, 255), color.R, color.G, color.B);
    }

    private static RectangleF ExpandRectangle(RectangleF rectangle, float amount)
    {
        return new RectangleF(
            rectangle.X - amount,
            rectangle.Y - amount,
            rectangle.Width + amount * 2f,
            rectangle.Height + amount * 2f);
    }

    private static Color GetIconPriorityColor(DebuffPriority priority)
    {
        return priority switch
        {
            DebuffPriority.Critical => Color.FromArgb(255, 255, 48, 72),
            DebuffPriority.Major => Color.FromArgb(255, 255, 145, 40),
            _ => Color.FromArgb(255, 244, 204, 64),
        };
    }

    private static string IconTextureKey(string icon) =>
        IconTexturePrefix + icon.Replace('/', '_').Replace('\\', '_');

    private static int CompareRawEffects(RuntimeEffectSnapshot left, RuntimeEffectSnapshot right)
    {
        var leftName = string.IsNullOrWhiteSpace(left.DisplayName) ? left.InternalName : left.DisplayName;
        var rightName = string.IsNullOrWhiteSpace(right.DisplayName) ? right.InternalName : right.DisplayName;
        return StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
    }

    private static int CompareCatalogRecords(RePoeDebuffRecord left, RePoeDebuffRecord right)
    {
        var leftName = string.IsNullOrWhiteSpace(left.Name) ? left.InternalId : left.Name;
        var rightName = string.IsNullOrWhiteSpace(right.Name) ? right.InternalId : right.Name;
        var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
        return nameComparison != 0
            ? nameComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left.InternalId, right.InternalId);
    }

    private int CompareTrackedEffects(ActiveTrackedEffect left, ActiveTrackedEffect right)
    {
        var priority = GetEffectivePriority(right.Definition).CompareTo(GetEffectivePriority(left.Definition));
        return priority != 0 ? priority : left.Definition.StableOrder.CompareTo(right.Definition.StableOrder);
    }

    private static bool ContainsIgnoreCase(List<string> values, string candidate)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsMeaningfulTime(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) && value < 9999;
    }

    private static string FormatSeconds(double value)
    {
        return value < 10
            ? value.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : value.ToString("0", CultureInfo.InvariantCulture) + "s";
    }

    private static string FormatIconTimer(double value)
    {
        return value < 10
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatRawDuration(double timeLeft, double maxTime)
    {
        if (!IsMeaningfulTime(timeLeft))
            return "persistent/no timer";

        var remaining = timeLeft.ToString("0.00", CultureInfo.InvariantCulture) + "s";
        return IsMeaningfulTime(maxTime)
            ? remaining + "/" + maxTime.ToString("0.00", CultureInfo.InvariantCulture) + "s"
            : remaining;
    }

    private readonly record struct RenderLine(string Text, Color Color);
    private readonly record struct BuffProbeCandidate(object Buff, RuntimeEffectSnapshot Snapshot, bool IsMapped);
    private readonly record struct NativeUiRelatedElement(string Label, object Element);
    private readonly record struct PillLayout(ActiveTrackedEffect Effect, RectangleF Bounds, string Label, string Detail, string Subtext, Color Accent);
    private sealed record WrappedTextCacheEntry(string Source, float MaxWidth, float TextScale, string[] Lines);
}
