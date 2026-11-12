using UnityEngine;
using UnityEngine.UI;

namespace EndlessMode;

internal sealed class CustomSquadScroll
{
    internal RectTransform Content { get; }
    internal GridLayoutGroup Grid { get; }

    private readonly RectTransform viewport;
    private readonly ScrollRect scroll;
    private readonly RectTransform nativeRoot;
    private readonly RectTransform column;
    private float lastViewportHeight;

    internal CustomSquadScroll(RectTransform viewport, GridLayoutGroup nativeGrid)
    {
        this.viewport = viewport;
        nativeRoot = nativeGrid.GetComponent<RectTransform>();
        column = nativeRoot.parent?.GetComponent<RectTransform>();

        var obj = new GameObject("CustomSquadsContent");
        Content = obj.AddComponent<RectTransform>();
        Content.SetParent(viewport, false);
        Content.anchorMin = new Vector2(0, 1);
        Content.anchorMax = Vector2.one;
        Content.pivot = new Vector2(.5f, 1);
        Content.sizeDelta = Vector2.zero;
        Content.anchoredPosition = Vector2.zero;

        Grid = obj.AddComponent<GridLayoutGroup>();
        obj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        viewport.gameObject.AddComponent<RectMask2D>();
        // Receive wheel/drag gestures in the gaps. Card graphics retain their
        // original raycasts and clicks, and clipped cards cannot intercept them.
        var surface = viewport.gameObject.AddComponent<Image>();
        surface.color = Color.clear;
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = Content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = Mathf.Max(1, nativeGrid.cellSize.y + nativeGrid.spacing.y);
    }

    internal void SetOpen(bool open)
    {
        if (Content == null) return;
        Content.anchoredPosition = Vector2.zero;
        if (open) FitViewport();
    }

    internal bool FitViewport()
    {
        if (column == null || nativeRoot == null || viewport == null) return false;
        // TeamsGrid has a native 500-unit minimum which can extend below the
        // column on shorter screens. On taller screens the column can extend
        // below TeamsGrid. Keep the native top, below the localized title, and
        // place the mask's lower edge at the bottom of MetaTeamsContainer.
        float columnBottom = nativeRoot.InverseTransformPoint(column.TransformPoint(
            new Vector3(column.rect.center.x, column.rect.yMin, 0))).y;
        var rootRect = nativeRoot.rect;
        float bottomInset = Mathf.Min(columnBottom, rootRect.yMax) - rootRect.yMin;
        var inset = viewport.offsetMin;
        if (!Mathf.Approximately(inset.y, bottomInset))
            viewport.offsetMin = new Vector2(inset.x, bottomInset);

        float height = viewport.rect.height;
        if (Mathf.Approximately(lastViewportHeight, height)) return false;
        lastViewportHeight = height;
        return true;
    }

    internal void Reveal(RectTransform tile)
    {
        if (tile == null || tile.parent != Content || !viewport.gameObject.activeInHierarchy) return;
        FitViewport();
        var rect = tile.rect;
        float top = Content.rect.yMax - Content.InverseTransformPoint(tile.TransformPoint(
            new Vector3(rect.center.x, rect.yMax, 0))).y;
        float bottom = Content.rect.yMax - Content.InverseTransformPoint(tile.TransformPoint(
            new Vector3(rect.center.x, rect.yMin, 0))).y;
        var position = Content.anchoredPosition;
        float offset = SquadScrollOffset.Reveal(position.y, viewport.rect.height, Content.rect.height, top, bottom);
        if (Mathf.Approximately(offset, position.y)) return;
        scroll.StopMovement();
        Content.anchoredPosition = new Vector2(position.x, offset);
    }
}
