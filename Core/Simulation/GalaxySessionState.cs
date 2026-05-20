using System.Collections.Generic;

namespace GalaxySim.Core.Simulation;

public sealed record GalaxySessionState
{
    public int Version { get; init; } = 1;
    public int GalaxySeed { get; init; }
    public int ParticleCount { get; init; }
    public GalaxyPresetType Preset { get; init; } = GalaxyPresetType.MilkyWay;
    public Scenario Scenario { get; init; } = Scenario.Single;
    public CameraState? Camera { get; init; }
    public GalaxyObjectId? SelectedObjectId { get; init; }
    public List<GalaxyObjectId> BookmarkedObjectIds { get; init; } = [];
    public SearchSettingsState SearchSettings { get; init; } = new();
    public DiscoveryJournalState DiscoveryJournal { get; init; } = new();
    public UiState UiState { get; init; } = new();
}

public sealed record SearchSettingsState
{
    public string? Query { get; init; }
    public string? SpectralClass { get; init; }
    public string? BinaryFilter { get; init; }
    public string? InterestTrait { get; init; }
    public bool HabitableSystemsOnly { get; init; }
    public bool RingedGiantsOnly { get; init; }
    public int MinimumPlanetCount { get; init; }
    public string? SortMode { get; init; }
    public int ResultLimit { get; init; } = 80;
}

public sealed record UiState
{
    public bool SystemViewerOpen { get; init; }
    public bool SelectedStarCardPinned { get; init; }
    public string? DiscoveryFilter { get; init; }
}
