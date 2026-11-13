using System.Drawing.Drawing2D;

namespace ClaudeBar;

/// <summary>
/// Fenêtre qui s'ouvre au clic sur l'icône (équivalent du NSPopover + PopoverView macOS).
/// Entièrement dessinée à la main pour suivre le thème clair/sombre de Windows et le DPI de l'écran.
/// </summary>
internal sealed class UsagePopup : Form
{
    private const int LogicalWidth = 300;
    private const TextFormatFlags SingleLine =
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

    private const string GlyphRefresh = "";
    private const string GlyphSettings = "";
    private const string GlyphWarning = "";

    private readonly UsageState _state;
    private readonly List<HitTarget> _targets = [];
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _spinTimer = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 20_000 };

    private Palette _palette = Palette.Light;
    private FontSet? _fonts;
    private float _scale = 1f;
    private string? _hoverId;
    private float _spinAngle;
    private Point _anchor;
    private DateTime _lastHiddenUtc = DateTime.MinValue;

    // Résultat de la mise en page (pixels physiques)
    private ViewMode _mode;
    private int _headerHeight, _contentTop, _contentBottom, _footerTop;
    private Rectangle _refreshRect, _settingsRect, _warningRect, _messageRect, _primaryRect, _secondaryRect;
    private Rectangle _openRect, _quitRect, _updatedRect;
    private string _primaryText = "", _secondaryText = "";

    public event Action? RefreshClicked;
    public event Action? SettingsClicked;
    public event Action? LoginClicked;
    public event Action? OpenClaudeClicked;
    public event Action? QuitClicked;
    public event Action? InstallWebViewClicked;

    private enum ViewMode { Loading, Data, Error }

    private sealed record HitTarget(string Id, Rectangle Bounds, Action OnClick, string? Tooltip = null);

    public UsagePopup(UsageState state)
    {
        _state = state;

        Text = "Claude Bar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _spinTimer.Tick += (_, _) =>
        {
            _spinAngle = (_spinAngle + 14f) % 360f;
            Invalidate(_refreshRect);
        };
        _clockTimer.Tick += (_, _) => Invalidate();
        _state.Changed += OnStateChanged;
    }

    /// <summary>Vrai si la fenêtre vient d'être masquée (clic sur l'icône alors qu'elle était ouverte).</summary>
    public bool WasJustHidden => (DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds < 400;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int CS_DROPSHADOW = 0x00020000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // pas d'entrée dans Alt+Tab
            if (!NativeMethods.IsWindows11) cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.TryRoundCorners(Handle);
    }

    public void ShowNear(Point anchor)
    {
        _anchor = anchor;
        _palette = NativeMethods.SystemUsesLightTheme() ? Palette.Light : Palette.Dark;
        ApplyScale(NativeMethods.GetDpiForPoint(anchor, DeviceDpi));
        Relayout();
        PlaceWindow();

        Show();
        Activate();
        NativeMethods.SetForegroundWindow(Handle);

        _clockTimer.Start();
        UpdateSpinner();
        Invalidate();
    }

    public void HidePopup()
    {
        if (!Visible) return;
        Hide();
        _lastHiddenUtc = DateTime.UtcNow;
        _clockTimer.Stop();
        _spinTimer.Stop();
        _hoverId = null;
    }

    /// <summary>Rendu hors écran (mode --selftest) pour vérifier l'affichage sans interaction.</summary>
    public Bitmap RenderSnapshot(bool dark, int dpi)
    {
        _palette = dark ? Palette.Dark : Palette.Light;
        ApplyScale(dpi);
        Relayout();
        var size = ClientSize;
        var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, size)));
        return bmp;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HidePopup();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HidePopup();
            return true;
        }
        if (keyData == Keys.F5)
        {
            RefreshClicked?.Invoke();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale(e.DeviceDpiNew);
        Relayout();
        PlaceWindow();
        Invalidate();
    }

    private void OnStateChanged()
    {
        if (!Visible) return;
        Relayout();
        PlaceWindow();
        UpdateSpinner();
        Invalidate();
    }

    private void UpdateSpinner()
    {
        if (Visible && _state.IsLoading) _spinTimer.Start();
        else
        {
            _spinTimer.Stop();
            _spinAngle = 0;
        }
    }

    // ── Mise en page ────────────────────────────────────────────────────────

    private int S(float logical) => (int)Math.Round(logical * _scale);

    private void ApplyScale(int dpi)
    {
        float scale = Math.Max(1f, dpi / 96f);
        if (_fonts != null && Math.Abs(scale - _scale) < 0.01f) return;
        _scale = scale;
        _fonts?.Dispose();
        _fonts = new FontSet(_scale);
    }

    private ViewMode ComputeMode()
    {
        if (_state.IsLoading && !_state.HasData && _state.Error == ErrorKind.None) return ViewMode.Loading;
        if (_state.Error != ErrorKind.None) return ViewMode.Error;
        return _state.HasData ? ViewMode.Data : ViewMode.Loading;
    }

    private bool ShowOrgName => _state.Organizations.Count > 1 && !string.IsNullOrEmpty(_state.CurrentOrgName);

    private void Relayout()
    {
        _fonts ??= new FontSet(_scale);
        var f = _fonts;
        _targets.Clear();

        int w = S(LogicalWidth);
        int pad = S(16);

        // En-tête
        _headerHeight = S(ShowOrgName ? 54 : 46);
        int btn = S(28);
        int btnTop = (_headerHeight - btn) / 2;
        _settingsRect = new Rectangle(w - S(10) - btn, btnTop, btn, btn);
        _refreshRect = new Rectangle(_settingsRect.Left - btn - S(2), btnTop, btn, btn);
        _targets.Add(new HitTarget("refresh", _refreshRect, () => { if (!_state.IsLoading) RefreshClicked?.Invoke(); }, "Rafraîchir (F5)"));
        _targets.Add(new HitTarget("settings", _settingsRect, () => SettingsClicked?.Invoke(), "Paramètres"));

        // Contenu
        int y = _headerHeight + 1;
        _contentTop = y;
        _mode = ComputeMode();
        switch (_mode)
        {
            case ViewMode.Loading:
                y += S(64);
                break;
            case ViewMode.Data:
                y += pad + RowHeight + S(18) + RowHeight + pad;
                break;
            case ViewMode.Error:
                y = LayoutError(y, w, pad, f);
                break;
        }
        _contentBottom = y;

        // Pied de page
        _footerTop = y + 1;
        int footerH = S(40);
        var quitSize = MeasureLine("Quitter", f.Small);
        var openSize = MeasureLine("Ouvrir Claude", f.Small);
        int linkH = S(24);
        int linkTop = _footerTop + (footerH - linkH) / 2;
        _quitRect = new Rectangle(w - pad - quitSize.Width, linkTop, quitSize.Width, linkH);
        _openRect = new Rectangle(_quitRect.Left - S(14) - openSize.Width, linkTop, openSize.Width, linkH);
        _updatedRect = new Rectangle(pad, linkTop, Math.Max(0, _openRect.Left - pad - S(8)), linkH);
        _targets.Add(new HitTarget("open", Inflate(_openRect, S(4)), () => OpenClaudeClicked?.Invoke()));
        _targets.Add(new HitTarget("quit", Inflate(_quitRect, S(4)), () => QuitClicked?.Invoke()));

        ClientSize = new Size(w, _footerTop + footerH);
    }

    private int RowHeight => S(18) + S(8) + S(6);

    private int LayoutError(int y, int w, int pad, FontSet f)
    {
        y += S(16);
        _warningRect = new Rectangle(0, y, w, S(26));
        y += S(26) + S(8);

        string message = _state.ErrorMessage ?? "Erreur inconnue.";
        var msgSize = TextRenderer.MeasureText(message, f.Body, new Size(w - 2 * pad, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
        _messageRect = new Rectangle(pad, y, w - 2 * pad, msgSize.Height);
        y += msgSize.Height + S(12);

        Action primary;
        Action? secondary = null;
        _secondaryText = "";
        switch (_state.Error)
        {
            case ErrorKind.NotLoggedIn:
                _primaryText = "Se connecter";
                primary = () => LoginClicked?.Invoke();
                _secondaryText = "Coller une session key…";
                secondary = () => SettingsClicked?.Invoke();
                break;
            case ErrorKind.Cloudflare:
                _primaryText = "Ouvrir la fenêtre claude.ai";
                primary = () => LoginClicked?.Invoke();
                _secondaryText = "Réessayer";
                secondary = () => RefreshClicked?.Invoke();
                break;
            case ErrorKind.NoUsageData:
                _primaryText = "Paramètres";
                primary = () => SettingsClicked?.Invoke();
                break;
            case ErrorKind.WebViewMissing:
                _primaryText = "Installer WebView2";
                primary = () => InstallWebViewClicked?.Invoke();
                break;
            default:
                _primaryText = "Réessayer";
                primary = () => RefreshClicked?.Invoke();
                break;
        }

        var primarySize = MeasureLine(_primaryText, f.Button);
        int bw = primarySize.Width + S(32), bh = S(30);
        _primaryRect = new Rectangle((w - bw) / 2, y, bw, bh);
        _targets.Add(new HitTarget("primary", _primaryRect, primary));
        y += bh;

        if (secondary != null)
        {
            y += S(6);
            var secSize = MeasureLine(_secondaryText, f.Small);
            _secondaryRect = new Rectangle((w - secSize.Width) / 2, y, secSize.Width, S(22));
            _targets.Add(new HitTarget("secondary", Inflate(_secondaryRect, S(4)), secondary));
            y += S(22);
        }
        else
        {
            _secondaryRect = Rectangle.Empty;
        }

        return y + S(16);
    }

    /// <summary>Largeur réelle d'un texte sur une ligne. Surtout pas d'EndEllipsis ici : avec une taille
    /// proposée vide, GDI renverrait la largeur de « … » au lieu de celle du texte.</summary>
    private static Size MeasureLine(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    private static Rectangle Inflate(Rectangle r, int by)
    {
        r.Inflate(by, by / 2);
        return r;
    }

    /// <summary>Place la fenêtre près de l'icône, du bon côté de la barre des tâches.</summary>
    private void PlaceWindow()
    {
        var screen = Screen.FromPoint(_anchor);
        Rectangle wa = screen.WorkingArea, b = screen.Bounds;
        int w = Width, h = Height, m = S(12);
        int x, y;

        if (wa.Top > b.Top) { x = _anchor.X - w / 2; y = wa.Top + m; }                 // barre en haut
        else if (wa.Left > b.Left) { x = wa.Left + m; y = _anchor.Y - h / 2; }        // barre à gauche
        else if (wa.Right < b.Right) { x = wa.Right - w - m; y = _anchor.Y - h / 2; } // barre à droite
        else { x = _anchor.X - w / 2; y = wa.Bottom - h - m; }                        // barre en bas (défaut)

        x = Math.Clamp(x, wa.Left + m, Math.Max(wa.Left + m, wa.Right - w - m));
        y = Math.Clamp(y, wa.Top + m, Math.Max(wa.Top + m, wa.Bottom - h - m));
        Location = new Point(x, y);
    }

    // ── Dessin ─────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_fonts == null) return;
        var g = e.Graphics;
        var p = _palette;
        var f = _fonts;
        int w = ClientSize.Width, pad = S(16);

        g.Clear(p.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // En-tête : ✦ Claude Usage  [↻] [⚙]
        int titleBlockH = S(ShowOrgName ? 36 : 20);
        int titleTop = (_headerHeight - titleBlockH) / 2;
        var sparkleRect = new Rectangle(pad - S(2), titleTop, S(20), S(20));
        TextRenderer.DrawText(g, "✦", f.Symbol, sparkleRect, p.Sparkle,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var titleRect = new Rectangle(sparkleRect.Right + S(4), titleTop, _refreshRect.Left - sparkleRect.Right - S(8), S(20));
        TextRenderer.DrawText(g, "Claude Usage", f.Title, titleRect, p.Foreground, SingleLine);
        if (ShowOrgName)
        {
            var orgRect = new Rectangle(titleRect.Left, titleRect.Bottom, titleRect.Width, S(16));
            TextRenderer.DrawText(g, _state.CurrentOrgName, f.Small, orgRect, p.Secondary, SingleLine);
        }

        DrawIconButton(g, "refresh", _refreshRect, GlyphRefresh, _state.IsLoading ? _spinAngle : 0f, enabled: !_state.IsLoading);
        DrawIconButton(g, "settings", _settingsRect, GlyphSettings, 0f, enabled: true);

        DrawDivider(g, _headerHeight, w);

        // Contenu
        switch (_mode)
        {
            case ViewMode.Loading:
                TextRenderer.DrawText(g, "Chargement…", f.Body,
                    new Rectangle(0, _contentTop, w, _contentBottom - _contentTop), p.Secondary,
                    TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                break;

            case ViewMode.Data:
                int rowTop = _contentTop + pad;
                DrawUsageRow(g, rowTop, w, pad, "Session actuelle", _state.SessionPercent, SessionResetLabel());
                DrawUsageRow(g, rowTop + RowHeight + S(18), w, pad, "Cette semaine", _state.WeeklyPercent, null);
                break;

            case ViewMode.Error:
                DrawGlyph(g, GlyphWarning, f.IconLarge, p.Warning, _warningRect, 0f);
                TextRenderer.DrawText(g, _state.ErrorMessage, f.Body, _messageRect, p.Secondary,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
                DrawPrimaryButton(g, _primaryRect, _primaryText);
                if (!_secondaryRect.IsEmpty)
                {
                    DrawLink(g, "secondary", _secondaryRect, _secondaryText, p.Accent, underlineOnHover: true);
                }
                break;
        }

        DrawDivider(g, _contentBottom, w);

        // Pied de page
        if (_state.LastUpdated is { } updated)
        {
            TextRenderer.DrawText(g, $"Mis à jour {TimeAgo(updated)}", f.Small, _updatedRect, p.Secondary, SingleLine);
        }
        DrawLink(g, "open", _openRect, "Ouvrir Claude", p.Accent, underlineOnHover: true);
        DrawLink(g, "quit", _quitRect, "Quitter", _hoverId == "quit" ? p.Foreground : p.Secondary, underlineOnHover: false);

        // Bordure fine (Windows 11 dessine la sienne avec les coins arrondis)
        if (!NativeMethods.IsWindows11)
        {
            using var border = new Pen(p.Border);
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(border, 0, 0, w - 1, ClientSize.Height - 1);
        }
    }

    private void DrawUsageRow(Graphics g, int top, int w, int pad, string label, int percent, string? reset)
    {
        var f = _fonts!;
        var p = _palette;
        var color = BarColor(percent);
        int lineH = S(18);

        string pct = $"{percent}%";
        var pctSize = MeasureLine(pct, f.Percent);
        var pctRect = new Rectangle(w - pad - pctSize.Width, top, pctSize.Width, lineH);
        TextRenderer.DrawText(g, pct, f.Percent, pctRect, color, SingleLine);

        var labelSize = MeasureLine(label, f.Label);
        var labelRect = new Rectangle(pad, top, Math.Min(labelSize.Width, pctRect.Left - pad), lineH);
        TextRenderer.DrawText(g, label, f.Label, labelRect, p.Foreground, SingleLine);

        if (reset != null)
        {
            var resetRect = new Rectangle(labelRect.Right + S(6), top, Math.Max(0, pctRect.Left - labelRect.Right - S(10)), lineH);
            TextRenderer.DrawText(g, reset, f.Small, resetRect, p.Secondary, SingleLine);
        }

        int barTop = top + lineH + S(8);
        float barH = S(6);
        var track = new RectangleF(pad, barTop, w - 2 * pad, barH);
        using (var trackPath = TrayIconRenderer.RoundedRect(track, barH / 2))
        using (var trackBrush = new SolidBrush(p.Track))
        {
            g.FillPath(trackBrush, trackPath);
        }

        float fillW = track.Width * Math.Clamp(percent, 0, 100) / 100f;
        if (fillW >= 1f)
        {
            using var fillPath = TrayIconRenderer.RoundedRect(new RectangleF(track.X, track.Y, Math.Max(fillW, barH), barH), barH / 2);
            using var fillBrush = new SolidBrush(color);
            g.FillPath(fillBrush, fillPath);
        }
    }

    private void DrawIconButton(Graphics g, string id, Rectangle rect, string glyph, float angle, bool enabled)
    {
        if (enabled && _hoverId == id)
        {
            using var path = TrayIconRenderer.RoundedRect(rect, S(4));
            using var hover = new SolidBrush(_palette.Hover);
            g.FillPath(hover, path);
        }
        DrawGlyph(g, glyph, _fonts!.Icon, _palette.Secondary, rect, angle);
    }

    private void DrawPrimaryButton(Graphics g, Rectangle rect, string text)
    {
        var color = _hoverId == "primary" ? _palette.AccentHover : _palette.Accent;
        using (var path = TrayIconRenderer.RoundedRect(rect, S(4)))
        using (var brush = new SolidBrush(color))
        {
            g.FillPath(brush, path);
        }
        TextRenderer.DrawText(g, text, _fonts!.Button, rect, _palette.OnAccent, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawLink(Graphics g, string id, Rectangle rect, string text, Color color, bool underlineOnHover)
    {
        var font = underlineOnHover && _hoverId == id ? _fonts!.SmallUnderline : _fonts!.Small;
        TextRenderer.DrawText(g, text, font, rect, color, SingleLine);
    }

    private void DrawDivider(Graphics g, int y, int w)
    {
        using var pen = new Pen(_palette.Divider);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawLine(pen, 0, y, w, y);
        g.SmoothingMode = old;
    }

    /// <summary>Glyphe d'icône converti en tracé, centré exactement (rotation sans « tremblement »).</summary>
    private static void DrawGlyph(Graphics g, string glyph, Font font, Color color, Rectangle rect, float angle)
    {
        using var path = new GraphicsPath();
        path.AddString(glyph, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;

        using var m = new Matrix();
        m.Translate(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
        if (angle != 0) m.Rotate(angle);
        m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
        path.Transform(m);

        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    private static Color BarColor(int percent) => percent switch
    {
        < 60 => Color.FromArgb(0x33, 0x99, 0xFF),
        < 85 => Color.FromArgb(0xFF, 0x95, 0x00),
        _ => Color.FromArgb(0xFF, 0x3B, 0x30),
    };

    private string? SessionResetLabel() =>
        _state.SessionResetAt is { } reset ? $"Refresh : {reset.ToLocalTime():HH:mm}" : null;

    private static string TimeAgo(DateTime date)
    {
        int minutes = (int)(DateTime.Now - date).TotalMinutes;
        return minutes < 1 ? "à l'instant" : $"il y a {minutes} min";
    }

    // ── Souris ─────────────────────────────────────────────────────────────

    private HitTarget? HitTest(Point p) => _targets.FirstOrDefault(t => t.Bounds.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit?.Id == _hoverId) return;

        _hoverId = hit?.Id;
        Cursor = hit != null && !(hit.Id == "refresh" && _state.IsLoading) ? Cursors.Hand : Cursors.Default;
        _toolTip.SetToolTip(this, hit?.Tooltip);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverId == null) return;
        _hoverId = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) HitTest(e.Location)?.OnClick();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _state.Changed -= OnStateChanged;
            _spinTimer.Dispose();
            _clockTimer.Dispose();
            _toolTip.Dispose();
            _fonts?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Thème & polices ────────────────────────────────────────────────────

    private sealed record Palette(
        Color Background, Color Foreground, Color Secondary, Color Divider, Color Track, Color Hover,
        Color Border, Color Accent, Color AccentHover, Color OnAccent, Color Sparkle, Color Warning)
    {
        public static readonly Palette Light = new(
            Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
            Foreground: Color.FromArgb(0x1B, 0x1B, 0x1B),
            Secondary: Color.FromArgb(0x5F, 0x5F, 0x5F),
            Divider: Color.FromArgb(0xE5, 0xE5, 0xE5),
            Track: Color.FromArgb(0xE3, 0xE3, 0xE3),
            Hover: Color.FromArgb(0xEA, 0xEA, 0xEA),
            Border: Color.FromArgb(0xCF, 0xCF, 0xCF),
            Accent: Color.FromArgb(0x00, 0x5F, 0xB8),
            AccentHover: Color.FromArgb(0x19, 0x75, 0xC5),
            OnAccent: Color.White,
            Sparkle: Color.FromArgb(0x7C, 0x3A, 0xED),
            Warning: Color.FromArgb(0xE0, 0x7B, 0x00));

        public static readonly Palette Dark = new(
            Background: Color.FromArgb(0x2B, 0x2B, 0x2B),
            Foreground: Color.FromArgb(0xFF, 0xFF, 0xFF),
            Secondary: Color.FromArgb(0xA8, 0xA8, 0xA8),
            Divider: Color.FromArgb(0x3D, 0x3D, 0x3D),
            Track: Color.FromArgb(0x48, 0x48, 0x48),
            Hover: Color.FromArgb(0x3A, 0x3A, 0x3A),
            Border: Color.FromArgb(0x45, 0x45, 0x45),
            Accent: Color.FromArgb(0x4C, 0xC2, 0xFF),
            AccentHover: Color.FromArgb(0x6F, 0xCF, 0xFF),
            OnAccent: Color.FromArgb(0x00, 0x1A, 0x2E),
            Sparkle: Color.FromArgb(0xA7, 0x8B, 0xFA),
            Warning: Color.FromArgb(0xFF, 0xA5, 0x2E));
    }

    private sealed class FontSet : IDisposable
    {
        public Font Title { get; }
        public Font Body { get; }
        public Font Label { get; }
        public Font Small { get; }
        public Font SmallUnderline { get; }
        public Font Percent { get; }
        public Font Button { get; }
        public Font Symbol { get; }
        public Font Icon { get; }
        public Font IconLarge { get; }

        public FontSet(float s)
        {
            Title = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
            Body = new Font("Segoe UI", 12f * s, GraphicsUnit.Pixel);
            Label = new Font("Segoe UI", 12f * s, GraphicsUnit.Pixel);
            Small = new Font("Segoe UI", 11f * s, GraphicsUnit.Pixel);
            SmallUnderline = new Font("Segoe UI", 11f * s, FontStyle.Underline, GraphicsUnit.Pixel);
            Percent = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
            Button = new Font("Segoe UI Semibold", 12f * s, GraphicsUnit.Pixel);
            Symbol = new Font("Segoe UI Symbol", 14f * s, GraphicsUnit.Pixel);
            string iconFamily = ResolveIconFamily();
            Icon = new Font(iconFamily, 14f * s, GraphicsUnit.Pixel);
            IconLarge = new Font(iconFamily, 24f * s, GraphicsUnit.Pixel);
        }

        /// <summary>Segoe Fluent Icons (Windows 11) sinon Segoe MDL2 Assets (Windows 10).</summary>
        private static string ResolveIconFamily()
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                try
                {
                    using var family = new FontFamily(name);
                    return name;
                }
                catch (ArgumentException)
                {
                    // police absente
                }
            }
            return "Segoe UI Symbol";
        }

        public void Dispose()
        {
            foreach (var font in new[] { Title, Body, Label, Small, SmallUnderline, Percent, Button, Symbol, Icon, IconLarge })
            {
                font.Dispose();
            }
        }
    }
}
