using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace OsuHitsound.App;

public partial class MainWindow
{
    // Capture before opening the picker so the saved image reflects the editor, not the dialog.
    private async void SaveEditorImage_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var image = CaptureEditorImage();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save editor image",
                SuggestedFileName = "editor.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            image.Save(stream);
            flash = "Editor image saved.";
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            flash = $"Couldn't save the editor image: {ex.Message}";
            UpdateStatus();
        }
    }

    internal RenderTargetBitmap CaptureEditorImage()
    {
        var scale = RenderScaling;
        var width = (int)Math.Ceiling(Root.Bounds.Width * scale);
        var height = (int)Math.Ceiling(Root.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The editor view has no visible size.");

        var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96 * scale, 96 * scale));
        try
        {
            image.Render(Root);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
