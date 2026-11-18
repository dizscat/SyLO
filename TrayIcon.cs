using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WinForms = System.Windows.Forms;

namespace SyLO;

/// <summary>Ikon di system tray (pojok kanan bawah) untuk membuka pengaturan / keluar.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly WinForms.ToolStripMenuItem _toggleItem;

    public TrayIcon(Action toggleOverlay, Action openSettings, Action exit,
        Func<System.Collections.Generic.IReadOnlyList<string>> presetNames, Action<int> loadPreset)
    {
        var menu = new WinForms.ContextMenuStrip();
        _toggleItem = new WinForms.ToolStripMenuItem(Loc.T("Sembunyikan overlay"), null, (_, _) => toggleOverlay());
        menu.Items.Add(_toggleItem);

        // Submenu template: diisi ulang setiap kali menu dibuka supaya selalu terbaru
        _templatesItem = new WinForms.ToolStripMenuItem(Loc.T("Muat template"));
        menu.Items.Add(_templatesItem);
        menu.Opening += (_, _) =>
        {
            _templatesItem.DropDownItems.Clear();
            var names = presetNames();
            if (names.Count == 0)
                _templatesItem.DropDownItems.Add(new WinForms.ToolStripMenuItem(Loc.T("(belum ada template)")) { Enabled = false });
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                _templatesItem.DropDownItems.Add(new WinForms.ToolStripMenuItem(names[i].Replace("&", "&&"), null, (_, _) => loadPreset(index)));
            }
        };

        _settingsItem = new WinForms.ToolStripMenuItem(Loc.T("Pengaturan…"), null, (_, _) => openSettings());
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _exitItem = new WinForms.ToolStripMenuItem(Loc.T("Keluar"), null, (_, _) => exit());
        menu.Items.Add(_exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "SyLO",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) openSettings();
        };
    }

    readonly WinForms.ToolStripMenuItem _templatesItem, _settingsItem, _exitItem;

    public void SetOverlayVisible(bool visible) =>
        _toggleItem.Text = visible ? Loc.T("Sembunyikan overlay") : Loc.T("Tampilkan overlay");

    /// <summary>Perbarui teks menu setelah bahasa diganti.</summary>
    public void Relabel(bool overlayVisible)
    {
        SetOverlayVisible(overlayVisible);
        _templatesItem.Text = Loc.T("Muat template");
        _settingsItem.Text = Loc.T("Pengaturan…");
        _exitItem.Text = Loc.T("Keluar");
    }

    public void SetTooltip(string text) =>
        _icon.Text = text.Length > 120 ? text[..117] + "…" : text;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(124, 92, 255));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var font = new Font("Segoe UI Symbol", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("♪", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
        }
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { Native.DestroyIcon(h); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
