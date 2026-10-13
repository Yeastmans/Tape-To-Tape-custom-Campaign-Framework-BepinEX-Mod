using System;

namespace CampaignCore;

/// <summary>Keep the outer quarters once; repeat the middle half with a feathered overlap.</summary>
public readonly struct MapSceneryTiling
{
    public readonly float Width, Left, Right, Overlap, Step;
    public MapSceneryTiling(float width, bool generatedTransitions = false)
    {
        if (!float.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Left = width * .25f; Right = width * .75f;
        Overlap = generatedTransitions ? 0 : width * .08f; Step = Right - Left - Overlap;
    }
    public int AdditionalRepeats(float requiredExtension, float worldStep)
    {
        if (!float.IsFinite(requiredExtension) || !float.IsFinite(worldStep) || worldStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldStep));
        return requiredExtension <= 0 ? 0 : checked((int)Math.Ceiling(requiredExtension / worldStep));
    }
    public float RepeatStart(int index) => Right - Overlap + index * Step;
    public float EndingStart(int repeats) => Right - Overlap + repeats * Step;
    public float Extension(int repeats) => repeats * Step;
    public float TransitionStart(int index) => RepeatStart(index) - Width * .125f;
}

/// <summary>Choose the shortest whole scenery length that never compresses the authored route.</summary>
public readonly struct MapRouteSpacing
{
    public readonly int Repeats;
    public readonly float Start, AuthoredBoss, Boss, Scale;
    public MapRouteSpacing(float start, float authoredBoss, float originalBoss, float worldStep, int minimumRepeats = 0)
    {
        if (!float.IsFinite(start) || !float.IsFinite(authoredBoss) || !float.IsFinite(originalBoss)
            || !float.IsFinite(worldStep) || worldStep <= 0 || authoredBoss <= start + .001f)
            throw new ArgumentOutOfRangeException(nameof(authoredBoss));
        Start = start; AuthoredBoss = authoredBoss;
        // Rounding to the nearest section could shrink every gap (M05 was
        // projected to .845x), making individually valid stadium columns overlap.
        int fitting = checked((int)Math.Ceiling((authoredBoss - originalBoss) / worldStep));
        int minimum = checked((int)Math.Ceiling((start + .01f - originalBoss) / worldStep));
        Repeats = Math.Max(minimumRepeats, Math.Max(fitting, minimum));
        Boss = originalBoss + Repeats * worldStep;
        Scale = (Boss - Start) / (AuthoredBoss - Start);
    }
    public float Position(float authoredX) => Start + (authoredX - Start) * Scale;
}

/// <summary>Start + alternating A/B quarters + original final quarter.</summary>
public readonly struct MapHalfTiles
{
    public readonly float Width, TileWidth;
    public readonly int Parts;
    public MapHalfTiles(float width, int parts)
    {
        if (!float.IsFinite(width) || width <= 0 || parts < 1 || parts > 258) throw new ArgumentOutOfRangeException(nameof(parts));
        Width = width; TileWidth = width / 4; Parts = parts;
    }
    public float SourceStart(int index) => TileWidth * (1 + index % 2);
    public float Destination(int index) => TileWidth * (1 + index);
    public float EndingStart => TileWidth * (Parts + 1);
    public float Shift => TileWidth * (Parts - 2);
    public bool EndsWithA => Parts % 2 == 1;
    public bool NeedsLoopSeam(int index) => index >= 2 && index % 2 == 0;
    public float SeamStart(float boundary) => boundary - TileWidth / 2;
}
