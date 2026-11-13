namespace ClaudeBar;

/// <summary>Équivalent de SettingsView (macOS) : espace de travail, démarrage auto, compte / session key.</summary>
internal sealed class SettingsForm : Form
{
    private const int ContentWidth = 400;

    private readonly AppSettings _settings;
    private readonly ComboBox? _orgCombo;
    private readonly CheckBox _autostart;
    private readonly TextBox _keyBox;

    public event Action? LoginRequested;
    public event Action<string>? SessionKeyEntered;
    public event Action? SessionCleared;
    public event Action? OrganizationChanged;

    public SettingsForm(AppSettings settings, UsageState state)
    {
        _settings = settings;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Paramètres — Claude Bar";
        Icon = AppIcon.Icon;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 16, 20, 16);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };

        // Espace de travail
        if (state.Organizations.Count > 0)
        {
            layout.Controls.Add(Heading("Espace de travail"));
            _orgCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = ContentWidth,
                Margin = new Padding(0, 4, 0, 14),
            };
            foreach (var org in state.Organizations) _orgCombo.Items.Add(org);
            var selected = state.Organizations.FirstOrDefault(o => o.Id == settings.SelectedOrgId)
                           ?? state.Organizations.FirstOrDefault(o => o.Name == state.CurrentOrgName)
                           ?? state.Organizations[0];
            _orgCombo.SelectedItem = selected;
            layout.Controls.Add(_orgCombo);
        }

        // Démarrage
        _autostart = new CheckBox
        {
            Text = "Lancer Claude Bar au démarrage de Windows",
            Checked = StartupManager.IsEnabled(),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
        };
        _autostart.CheckedChanged += (_, _) => StartupManager.SetEnabled(_autostart.Checked);
        layout.Controls.Add(_autostart);

        // Compte
        layout.Controls.Add(Heading("Compte claude.ai"));
        layout.Controls.Add(Help(
            "Connecte-toi une fois dans la fenêtre intégrée : la session est mémorisée par Claude Bar."));
        var loginButton = new Button
        {
            Text = "Se connecter à claude.ai…",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 14),
        };
        loginButton.Click += (_, _) =>
        {
            Close();
            LoginRequested?.Invoke();
        };
        layout.Controls.Add(loginButton);

        // Session key manuelle
        layout.Controls.Add(Heading("Session key manuelle (optionnel)"));
        layout.Controls.Add(Help(
            "Si la connexion intégrée ne fonctionne pas (ex. connexion Google refusée), colle ici la valeur du cookie " +
            "« sessionKey » de ton navigateur : F12 → Application → Cookies → https://claude.ai → sessionKey."));
        _keyBox = new TextBox
        {
            Width = ContentWidth,
            UseSystemPasswordChar = true,
            PlaceholderText = "Colle la valeur ici…",
            Font = new Font("Consolas", 9F),
            Margin = new Padding(0, 6, 0, 18),
        };
        layout.Controls.Add(_keyBox);

        // Boutons
        var buttons = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var clearButton = new Button
        {
            Text = "Effacer la session",
            AutoSize = true,
            ForeColor = Color.Firebrick,
            Margin = Padding.Empty,
        };
        clearButton.Click += (_, _) =>
        {
            Close();
            SessionCleared?.Invoke();
        };

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Right,
        };
        var cancelButton = new Button { Text = "Annuler", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 8, 0) };
        var saveButton = new Button { Text = "Enregistrer", AutoSize = true, Margin = Padding.Empty };
        saveButton.Click += (_, _) => Save();
        right.Controls.Add(cancelButton);
        right.Controls.Add(saveButton);

        buttons.Controls.Add(clearButton, 0, 0);
        buttons.Controls.Add(right, 1, 0);
        layout.Controls.Add(buttons);

        layout.Controls.Add(new Label
        {
            Text = $"Claude Bar {AppInfo.Version}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 12, 0, 0),
        });

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        ResumeLayout(false);
        PerformLayout();
    }

    private void Save()
    {
        string key = _keyBox.Text.Trim();
        if (key.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase)) key = key["sessionKey=".Length..].Trim();

        bool orgChanged = false;
        if (_orgCombo?.SelectedItem is Organization org && org.Id != _settings.SelectedOrgId)
        {
            _settings.SelectedOrgId = org.Id;
            _settings.Save();
            orgChanged = true;
        }

        Close();

        if (key.Length > 0) SessionKeyEntered?.Invoke(key);
        else if (orgChanged) OrganizationChanged?.Invoke();
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 10F),
        Margin = new Padding(0, 0, 0, 2),
    };

    private static Label Help(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 0, 0, 2),
    };
}
