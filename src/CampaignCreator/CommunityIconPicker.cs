using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CampaignCreator;

internal sealed class CommunityIconPicker : StackPanel, IDisposable
{
    private readonly Image preview = new() { Name = "UploadIconPreview", Width = 96, Height = 96, Stretch = Stretch.Uniform };
    private readonly TextBlock status = new() { Name = "UploadIconStatus", Text = "No custom icon selected.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
    private readonly Button remove = new() { Name = "RemoveUploadIcon", Content = "Remove", IsEnabled = false };
    private Bitmap? bitmap;
    private bool disposed;
    internal string Icon { get; private set; } = "";
    internal bool IsBusy { get; private set; }
    internal event Action? BusyChanged;

    internal CommunityIconPicker(Func<Task<string?>> choosePng)
    {
        Name = "UploadIconPicker"; Spacing = StudioTheme.SpaceSm;
        var choose = new Button { Name = "ChooseUploadIcon", Content = "Choose PNG…" };
        choose.Click += async (_, _) =>
        {
            if (disposed || IsBusy) return;
            IsBusy = true; IsEnabled = false; BusyChanged?.Invoke();
            try
            {
                string? path = await choosePng();
                if (!disposed && path != null) SelectPng(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                status.Text = ex.Message + (Icon.Length > 0 ? " Your previous icon is still selected." : "");
                status.Foreground = Brush.Parse("#FFD18C");
            }
            finally { IsBusy = false; if (!disposed) { IsEnabled = true; BusyChanged?.Invoke(); } }
        };
        remove.Click += (_, _) => Clear();
        Children.Add(new TextBlock { Text = "Campaign icon (optional)", FontWeight = FontWeight.SemiBold });
        Children.Add(new TextBlock { Text = "Choose your own PNG. Studio makes a small preview for the community browser.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted });
        var actions = new StackPanel { Spacing = StudioTheme.SpaceSm, VerticalAlignment = VerticalAlignment.Center, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceSm, Children = { choose, remove } }, status
        } };
        Grid.SetColumn(actions, 1);
        Children.Add(new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = StudioTheme.SpaceMd, Children = {
            new Border { Width = 108, Height = 108, Background = StudioTheme.Input, CornerRadius = StudioTheme.Radius, Child = preview }, actions
        } });
    }

    internal void SelectPng(string path)
    {
        if (disposed) return;
        string selected = CommunityIcon.LoadPng(path);
        using var stream = new MemoryStream(Convert.FromBase64String(selected["data:image/png;base64,".Length..]));
        var next = new Bitmap(stream);
        preview.Source = next;
        bitmap?.Dispose(); bitmap = next;
        Icon = selected;
        status.Text = Path.GetFileName(path); status.Foreground = StudioTheme.Muted;
        remove.IsEnabled = true;
    }

    internal void Clear()
    {
        preview.Source = null; bitmap?.Dispose(); bitmap = null; Icon = "";
        status.Text = "No custom icon selected."; status.Foreground = StudioTheme.Muted; remove.IsEnabled = false;
    }

    public void Dispose() { disposed = true; Clear(); }
}
