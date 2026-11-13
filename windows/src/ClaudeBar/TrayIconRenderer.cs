using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeBar;

/// <summary>
/// La zone de notification Windows n'affiche qu'une icône (pas de texte comme la barre de menus macOS) :
/// on dessine donc le pourcentage de la session directement dans l'icône, sur un carré coloré
/// vert / orange / rouge (mêmes seuils que les pastilles 🟢🟡🔴 de la version macOS).
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly Color Green = Color.FromArgb(0x1F, 0x9D, 0x55);
    private static readonly Color Amber = Color.FromArgb(0xD9, 0x8E, 0x04);
    private static readonly Color Red = Color.FromArgb(0xD9, 0x30, 0x25);
    private static readonly Color Neutral = Color.FromArgb(0x6B, 0x6B, 0x6B);

    public static Color ColorFor(int percent) => percent >= 90 ? Red : percent >= 60 ? Amber : Green;

    public static Icon RenderPercent(int percent) =>
        Render(Math.Clamp(percent, 0, 100).ToString(), ColorFor(percent));

    public static Icon RenderLoading() => Render("…", Neutral);

    public static Icon RenderLoggedOut() => Render("?", Neutral);

    public static Icon RenderError() => Render("!", Neutral);

    private static Icon Render(string text, Color background)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float radius = size * 0.22f;
            using (var bg = RoundedRect(new RectangleF(0, 0, size, size), radius))
            using (var brush = new SolidBrush(background))
            {
                g.FillPath(brush, bg);
            }

            // Texte converti en tracé pour un centrage exact, réduit jusqu'à tenir dans l'icône.
            using var family = new FontFamily("Segoe UI");
            float maxW = size * (text.Length >= 3 ? 0.92f : 0.84f);
            float maxH = size * 0.66f;
            float em = size * 0.80f;

            GraphicsPath? path = null;
            for (int i = 0; i < 12; i++)
            {
                path?.Dispose();
                path = new GraphicsPath();
                path.AddString(text, family, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                if (b.Width <= maxW && b.Height <= maxH) break;
                em *= 0.9f;
            }

            if (path != null)
            {
                var bounds = path.GetBounds();
                using var m = new Matrix();
                m.Translate(size / 2f - (bounds.X + bounds.Width / 2f), size / 2f - (bounds.Y + bounds.Height / 2f));
                path.Transform(m);
                g.FillPath(Brushes.White, path);
                path.Dispose();
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone(); // copie qui possède son propre handle
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
