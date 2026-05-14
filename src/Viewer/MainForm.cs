namespace EmailArchiveViewer;

/// <summary>
/// The whole viewer: a folder dropdown up top, an email list below, and — on opening a
/// message — the list area swaps to the detail view with a Back button. The backup root is
/// the directory this exe runs from (MailArchiver drops it into each backup).
/// </summary>
public sealed class MainForm : Form
{
    private enum View { Status, Grid, Email }

    private readonly string _root;
    private readonly ComboBox _folders;
    private readonly Button _back;
    private readonly DataGridView _grid;
    private readonly EmailView _email;
    private readonly Label _status;

    public MainForm()
    {
        Text = "Email Archive Viewer";
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(FontFamily.GenericMonospace, 9f);

        _root = AppContext.BaseDirectory;

        // --- top bar: Back button + folder dropdown ---
        _folders = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
        };
        _folders.SelectedIndexChanged += async (_, _) => await LoadSelectedFolder();

        _back = new Button
        {
            Text = "< Back",
            Dock = DockStyle.Left,
            Width = 90,
            Visible = false,
            FlatStyle = FlatStyle.Flat,
        };
        _back.Click += (_, _) => SetView(View.Grid);

        var comboHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 3, 2, 2) };
        comboHost.Controls.Add(_folders);
        var top = new Panel { Dock = DockStyle.Top, Height = 30 };
        top.Controls.Add(comboHost); // fill (added first)
        top.Controls.Add(_back);     // left edge (added last -> docks first)

        // --- centre: list grid / detail view / status label, stacked ---
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = new Font(FontFamily.GenericMonospace, 9f),
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sender", DataPropertyName = "Sender", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Receiver", DataPropertyName = "Receiver", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date", DataPropertyName = "DateText", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Subject", DataPropertyName = "Subject", FillWeight = 42 });
        _grid.CellDoubleClick += async (_, e) => await OpenEmail(e.RowIndex);

        _email = new EmailView { Dock = DockStyle.Fill, Visible = false };

        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Visible = false,
        };

        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_grid);
        content.Controls.Add(_email);
        content.Controls.Add(_status);

        Controls.Add(content);
        Controls.Add(top);

        Load += async (_, _) => await Initialize();
    }

    // These run as async-void event handlers, so an unhandled exception would crash the
    // app — guard them and surface the failure in the status pane instead.
    private async Task Initialize()
    {
        try
        {
            SetStatus("Scanning backup folders…");
            List<MailFolder> folders = await Task.Run(() => MailScanner.Scan(_root));

            _folders.Items.Clear();
            foreach (MailFolder f in folders)
                _folders.Items.Add(f);

            if (_folders.Items.Count == 0)
            {
                SetStatus($"No mail folders found under:\n{_root}\n\n" +
                          "Put EmailArchiveViewer.exe in the root of a backup and run it there.");
                return;
            }
            _folders.SelectedIndex = 0; // fires LoadSelectedFolder
        }
        catch (Exception ex)
        {
            SetStatus($"Could not scan the backup:\n{ex.Message}");
        }
    }

    private async Task LoadSelectedFolder()
    {
        if (_folders.SelectedItem is not MailFolder folder) return;
        try
        {
            SetStatus($"Loading {folder.DisplayPath}…");
            _grid.DataSource = null;

            List<MessageRow> rows = await Task.Run(() => MailScanner.LoadFolder(folder.FullPath));
            _grid.DataSource = rows;

            Text = $"Email Archive Viewer — {folder.DisplayPath} ({rows.Count})";
            if (rows.Count == 0)
                SetStatus($"{folder.DisplayPath} — no messages.");
            else
                SetView(View.Grid);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load {folder.DisplayPath}:\n{ex.Message}");
        }
    }

    private async Task OpenEmail(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[rowIndex].DataBoundItem is not MessageRow row) return;

        SetStatus("Opening message…");

        EmailDetail? detail = null;
        string? error = null;
        await Task.Run(() =>
        {
            try { detail = MailFacade.ReadFull(row.FilePath); }
            catch (Exception ex) { error = ex.Message; }
        });

        if (detail is null)
        {
            SetStatus($"Could not open message:\n{error}");
            return;
        }
        _email.Show(detail);
        SetView(View.Email);
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        SetView(View.Status);
    }

    private void SetView(View view)
    {
        _status.Visible = view == View.Status;
        _grid.Visible = view == View.Grid;
        _email.Visible = view == View.Email;
        _back.Visible = view == View.Email;

        Control front = view switch
        {
            View.Status => _status,
            View.Grid => _grid,
            _ => _email,
        };
        front.BringToFront();
    }
}
