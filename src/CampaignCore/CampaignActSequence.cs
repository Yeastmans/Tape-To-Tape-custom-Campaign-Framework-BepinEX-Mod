using System;
using System.Globalization;

namespace CampaignCore;

/// <summary>Gameplay acts in authored map order; any act may be the final map.</summary>
public static class CampaignActSequence
{
    public const string Default = "1,2,3";
    public const string ValidationMessage = "Act order must be a comma-separated sequence of 1, 2 or 3.";

    public static bool TryParse(string value, out int[] acts)
    {
        acts = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split(',');
        var parsed = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i])
                || parsed[i] < 1 || parsed[i] > 3) return false;
        acts = parsed;
        return true;
    }

    public static int[] Read(CampaignDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (!TryParse(document.Get("Act Sequence", Default), out var acts))
            throw new FormatException(ValidationMessage);
        return acts;
    }
}
