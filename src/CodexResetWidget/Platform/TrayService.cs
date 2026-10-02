using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace CodexResetWidget.Platform;

public sealed class TrayService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint handle);
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _ownedIcon;
    public bool IsVisible => _icon.Visible;
    public TrayService(ImageSource logo, Action show, Action hide, Action refresh, Action exit)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(logo, new System.Windows.Rect(0, 0, 32, 32));
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream(); encoder.Save(memory); memory.Position = 0;
        using var raster = new System.Drawing.Bitmap(memory);
        var handle = raster.GetHicon();
        try { using var borrowed = System.Drawing.Icon.FromHandle(handle); _ownedIcon = (System.Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
        _icon = new Forms.NotifyIcon { Icon = _ownedIcon, Text = L10n.Get("Tray.Tooltip"), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L10n.Get("Tray.Show"), null, (_, _) => show()).Tag = "Tray.Show";
        menu.Items.Add(L10n.Get("Action.Hide"), null, (_, _) => hide()).Tag = "Action.Hide";
        menu.Items.Add(L10n.Get("Action.Refresh"), null, (_, _) => refresh()).Tag = "Action.Refresh";
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L10n.Get("Action.Exit"), null, (_, _) => exit()).Tag = "Action.Exit";
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) show(); };
        L10n.Changed += RefreshLanguage;
    }
    private void RefreshLanguage(object? sender, EventArgs e)
    {
        _icon.Text = L10n.Get("Tray.Tooltip");
        foreach (Forms.ToolStripItem item in _icon.ContextMenuStrip!.Items)
            if (item.Tag is string key) item.Text = L10n.Get(key);
    }
    public void ExplainClose() => _icon.ShowBalloonTip(5000, L10n.Get("Tray.HiddenTitle"), L10n.Get("Tray.HiddenMessage"), Forms.ToolTipIcon.Info);
    public void Dispose() { L10n.Changed -= RefreshLanguage; _icon.Visible = false; _icon.ContextMenuStrip?.Dispose(); _icon.Dispose(); _ownedIcon.Dispose(); }
}
