using System;
using System.Collections.Generic;

namespace GalaxySim.Core.Simulation;

public sealed record DiscoveryJournalState
{
    public List<DiscoveryRecord> Records { get; init; } = [];
    public int Sequence { get; init; }
}

public sealed record DiscoveryRecord
{
    public string Type { get; init; } = string.Empty;
    public GalaxyObjectId ObjectId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ObjectName { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public float Score { get; init; }
    public int Order { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.Now;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Value) ? Title : $"{Title} · {Value}";

    public string DisplaySubtitle => string.IsNullOrWhiteSpace(ObjectName)
        ? Description
        : $"{ObjectName} — {Description}";
}
