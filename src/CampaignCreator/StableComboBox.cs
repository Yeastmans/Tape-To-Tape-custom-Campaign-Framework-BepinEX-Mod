using Avalonia;
using Avalonia.Controls;

namespace CampaignCreator;

// The field's layout owns its width, never the currently selected label.
internal sealed class StableComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width;
        var measured = base.MeasureOverride(new Size(width, availableSize.Height));
        return new Size(width, measured.Height);
    }
}
