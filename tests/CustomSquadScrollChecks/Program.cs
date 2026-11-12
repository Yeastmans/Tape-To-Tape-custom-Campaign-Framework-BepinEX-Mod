using EndlessMode;

void Check(string label, float expected, float current, float viewport, float content, float top, float bottom)
{
    float actual = SquadScrollOffset.Reveal(current, viewport, content, top, bottom);
    if (Math.Abs(actual - expected) > .001f)
        throw new InvalidOperationException($"{label}: expected offset {expected}, got {actual}.");
    Console.WriteLine($"PASS {label}");
}

// Current Steam prefab uses 148-high cells with -16 vertical spacing.
Check("partly hidden row clears the footer", 44, 0, 500, 1204, 396, 544);
Check("next row fully below view", 176, 44, 500, 1204, 528, 676);
Check("same row preserves the scroll position", 176, 176, 500, 1204, 528, 676);
Check("jump to last partial row", 704, 0, 500, 1204, 1056, 1204);
Check("previous row clears the title", 660, 704, 500, 1204, 660, 808);
Check("jump back to first row", 0, 704, 500, 1204, 0, 148);
Check("short list remains in native position", 0, 0, 768, 412, 264, 412);
Check("widening viewport clamps obsolete offset", 0, 88, 1300, 1204, 1056, 1204);
Check("fractional canvas coordinates", 42.75f, 0, 501.5f, 1204, 396.25f, 544.25f);
