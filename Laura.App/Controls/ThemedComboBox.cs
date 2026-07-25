using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// A drop-down list with its items and border drawn in the dark theme.
///
/// The native visual style is stripped from the control so the drop-down button no
/// longer flashes its white hot-track highlight when the pointer is over it — the
/// flicker an earlier version suffered from. The item text is owner-drawn, and a thin
/// themed border is painted on top.
/// </summary>
public sealed partial class ThemedComboBox : ComboBox
{
    private const int WmPaint = 0x000F;

    /// <summary>
    /// Initializes the combo box already dressed in the dark theme.
    /// </summary>
    public ThemedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Palette.Field;
        ForeColor = Palette.TextPrimary;
        ItemHeight = 22;
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Empty theme names remove the visual style, which is what stops the
        // drop-down button from hot-tracking white on hover.
        _ = SetWindowTheme(Handle, "​", "​");
    }

    /// <inheritdoc />
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        base.OnDrawItem(e);

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var background = new SolidBrush(isSelected ? Palette.Accent : Palette.Field))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        if (e.Index < 0)
        {
            return;
        }

        Rectangle textBounds = e.Bounds with { X = e.Bounds.X + 4, Width = e.Bounds.Width - 4 };

        TextRenderer.DrawText(
            e.Graphics,
            Items[e.Index]?.ToString() ?? string.Empty,
            Font,
            textBounds,
            isSelected ? Color.White : Palette.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    /// <inheritdoc />
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmPaint)
        {
            PaintBorder();
        }
    }

    /// <summary>
    /// Draws a thin themed border so the field reads as one flat dark rectangle.
    /// </summary>
    private void PaintBorder()
    {
        using Graphics graphics = Graphics.FromHwnd(Handle);
        using var borderPen = new Pen(Focused ? Palette.Accent : Palette.Border);
        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(nint hWnd, string appName, string subIdList);
}
