using System;

namespace CampaignCore;

/// <summary>Convert source sprite pixels to the renderer's actual local geometry.</summary>
public readonly struct MapSpriteGeometry
{
    public readonly float XPerPixel, YPerPixel;

    public MapSpriteGeometry(float pixelWidth, float pixelHeight, float pixelsPerUnit,
        bool usesRendererSize, float rendererWidth, float rendererHeight)
    {
        if (!float.IsFinite(pixelWidth) || pixelWidth <= 0 || !float.IsFinite(pixelHeight) || pixelHeight <= 0
            || !float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0
            || (usesRendererSize && (!float.IsFinite(rendererWidth) || rendererWidth <= 0
                || !float.IsFinite(rendererHeight) || rendererHeight <= 0)))
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        XPerPixel = usesRendererSize ? rendererWidth / pixelWidth : 1 / pixelsPerUnit;
        YPerPixel = usesRendererSize ? rendererHeight / pixelHeight : 1 / pixelsPerUnit;
    }

    public float LocalX(float pixels) => pixels * XPerPixel;
    public float LocalY(float pixels) => pixels * YPerPixel;
}
