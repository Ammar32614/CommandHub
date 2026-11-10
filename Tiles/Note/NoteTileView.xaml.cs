using System.Windows;
using System.Windows.Controls;

namespace CommandHub.Tiles.Note;

public partial class NoteTileView : UserControl
{
    public NoteTileView(TileContext ctx)
    {
        InitializeComponent();
        Editor.Text = NoteContent.Read(ctx.Content);
        UpdatePlaceholder();
        Editor.TextChanged += (_, _) =>
        {
            NoteContent.Write(ctx.Content, Editor.Text);
            UpdatePlaceholder();
            ctx.SaveSoon();
        };
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = Editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
}
