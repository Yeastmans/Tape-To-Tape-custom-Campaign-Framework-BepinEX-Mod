using System;

namespace EndlessMode;

internal static class SquadScrollOffset
{
    // All distances are measured downward from the top of the unscrolled grid.
    internal static float Reveal(float offset, float viewportHeight, float contentHeight, float top, float bottom)
    {
        float maxOffset = Math.Max(0f, contentHeight - viewportHeight);
        if (top < offset)
            offset = top;
        else if (bottom > offset + viewportHeight)
            offset = bottom - viewportHeight;

        return Math.Clamp(offset, 0f, maxOffset);
    }
}
