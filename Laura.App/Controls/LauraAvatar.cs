using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Displays the application icon as Laura's chat avatar.
/// </summary>
internal sealed class LauraAvatar : PictureBox
{
    private readonly Image _avatarImage;

    /// <summary>
    /// Initializes the avatar from the icon shipped with the application.
    /// </summary>
    public LauraAvatar()
    {
        Size = new Size(40, 40);
        BackColor = Palette.ChatBackground;
        SizeMode = PictureBoxSizeMode.Zoom;
        TabStop = false;

        _avatarImage = LoadApplicationIcon();
        Image = _avatarImage;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Image = null;
            _avatarImage.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Bitmap LoadApplicationIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");

        if (File.Exists(iconPath))
        {
            using var icon = new Icon(iconPath, new Size(64, 64));
            return icon.ToBitmap();
        }

        return SystemIcons.Application.ToBitmap();
    }
}
