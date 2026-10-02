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
        _icon = new Forms.NotifyIcon { Icon = _ownedIcon, Text = "Codex Reset · 公开公告", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => show());
        menu.Items.Add("收进托盘", null, (_, _) => hide());
        menu.Items.Add("刷新数据", null, (_, _) => refresh());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) show(); };
    }
    public void ExplainClose() => _icon.ShowBalloonTip(5000, "Codex Reset 已收进托盘", "点击托盘图标恢复窗口；右键菜单选择“退出”结束程序。", Forms.ToolTipIcon.Info);
    public void Dispose() { _icon.Visible = false; _icon.ContextMenuStrip?.Dispose(); _icon.Dispose(); _ownedIcon.Dispose(); }
}
