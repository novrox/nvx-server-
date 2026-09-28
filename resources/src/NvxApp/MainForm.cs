using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

sealed class MainForm : Form
{
    readonly string _root;
    readonly NvxConfig _config;
    readonly NvxLog _log;
    readonly NvxServices _services;
    readonly NvxDatabase _databases;
    readonly Dictionary<string, Panel> _pages = new Dictionary<string, Panel>();
    readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
    readonly Timer _timer = new Timer();
    volatile bool _closing;
    volatile bool _webActionInProgress;

    Label _address;
    Label _webStatus;
    Label _apacheStatus;
    Label _mysqlStatus;
    PowerButton _webButton;
    PowerButton _apacheButton;
    PowerButton _mysqlButton;
    TextBox _logBox;
    string _logText = "";
    Panel _projects;
    string _projectKey = "";
    ListView _dbList;
    ListView _appList;
    TextBox _nameBox;
    TextBox _zoneBox;
    TextBox _httpBox;
    TextBox _mysqlBox;

    public MainForm()
    {
        _root = NvxPaths.Root();
        _config = new NvxConfig(_root);
        _log = new NvxLog(_root);
        _services = new NvxServices(_root, _config, _log);
        _databases = new NvxDatabase(_root);
        Text = "NVX Server";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(860, 760);
        MinimumSize = new Size(760, 640);
        BackColor = NvxTheme.Ink;
        ForeColor = NvxTheme.Text;
        Font = NvxTheme.UiFont;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BuildShell();
        ShowPage("status");
        _timer.Interval = 2000;
        _timer.Tick += delegate { RefreshStatus(); };
        _timer.Start();
        FormClosing += delegate { _closing = true; _services.StopWeb(); };
        Shown += delegate { StartOnOpen(); };
    }

    void StartOnOpen()
    {
        _log.Server("Preparing PHP and MySQL runtimes in the background.");
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                NvxRuntimes.Ensure(_root, _log);
                if (_closing) return;
                _services.StartWeb();
                if (_services.ComponentInstalled("mysql") && _services.Status("mysql") != "Running")
                {
                    string mysql = _services.StartComponent("mysql");
                    _log.Server(mysql);
                }
                _log.Server("Local databases are ready (" + _databases.List().Count + ").");
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message);
            }
            if (!_closing && !IsDisposed)
            {
                try { BeginInvoke((MethodInvoker)delegate { RefreshStatus(); }); }
                catch (InvalidOperationException) { }
            }
        });
    }

    void BuildShell()
    {
        Panel header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = 132;
        header.BackColor = NvxTheme.Ink;
        PictureBox mark = new PictureBox();
        mark.Size = new Size(56, 56);
        mark.Location = new Point(24, 12);
        mark.SizeMode = PictureBoxSizeMode.Zoom;
        mark.Image = Image.FromFile(Path.Combine(_root, "nvx-ico.png"));
        Label title = new Label();
        title.Text = "NVX";
        title.Font = NvxTheme.TitleFont;
        title.ForeColor = NvxTheme.Text;
        title.AutoSize = true;
        title.Location = new Point(92, 5);
        Label subtitle = new Label();
        subtitle.Text = "SERVER";
        subtitle.Font = new Font("Segoe UI", 14f, FontStyle.Italic);
        subtitle.ForeColor = NvxTheme.Text;
        subtitle.AutoSize = true;
        subtitle.Location = new Point(96, 43);
        _address = NvxTheme.Mute("");
        _address.AutoSize = false;
        _address.Location = new Point(250, 29);
        _address.Size = new Size(560, 24);
        _address.AutoEllipsis = true;
        header.Resize += delegate { _address.Width = Math.Max(220, header.ClientSize.Width - _address.Left - 24); };
        header.Controls.Add(mark);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_address);
        LinkLabel credit = new LinkLabel();
        credit.Text = "Created by Novrox · hello@novrox.com";
        credit.AutoSize = true;
        credit.Location = new Point(250, 54);
        credit.LinkColor = NvxTheme.Green;
        credit.ActiveLinkColor = NvxTheme.Text;
        credit.VisitedLinkColor = NvxTheme.Green;
        credit.LinkClicked += delegate
        {
            try { Process.Start("mailto:hello@novrox.com"); }
            catch (Exception ex) { _log.Error("Could not open email link: " + ex.Message); }
        };
        header.Controls.Add(credit);
        AddTab(header, "status", "Status", 24, 84);
        AddTab(header, "databases", "Databases", 116, 110);
        AddTab(header, "apps", "Apps", 234, 72);
        AddTab(header, "settings", "Settings", 314, 96);

        Panel body = new Panel();
        body.Dock = DockStyle.Fill;
        body.Padding = new Padding(24, 0, 24, 16);
        body.BackColor = NvxTheme.Ink;
        _pages["status"] = PageStatus();
        _pages["databases"] = PageDatabases();
        _pages["apps"] = PageApps();
        _pages["settings"] = PageSettings();
        foreach (KeyValuePair<string, Panel> page in _pages)
        {
            page.Value.Dock = DockStyle.Fill;
            page.Value.Visible = false;
            body.Controls.Add(page.Value);
        }
        Controls.Add(body);
        Controls.Add(header);
    }

    void AddTab(Control header, string key, string text, int left, int width)
    {
        Button button = NvxTheme.Button(text);
        button.AutoSize = false;
        button.Size = new Size(width, 32);
        button.Padding = new Padding(8, 0, 8, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Location = new Point(left, 84);
        button.Click += delegate { ShowPage(key); };
        _tabs[key] = button;
        header.Controls.Add(button);
    }

    void ShowPage(string key)
    {
        foreach (KeyValuePair<string, Panel> page in _pages) page.Value.Visible = page.Key == key;
        foreach (KeyValuePair<string, Button> item in _tabs)
        {
            bool on = item.Key == key;
            item.Value.ForeColor = on ? NvxTheme.Text : NvxTheme.Muted;
            item.Value.FlatAppearance.BorderColor = on ? NvxTheme.Line : NvxTheme.Ink;
        }
        if (key == "databases") LoadDatabases();
        if (key == "apps") LoadApps();
        if (key == "settings") LoadSettings();
        RefreshStatus();
    }

    Panel PageStatus()
    {
        Panel page = new Panel();
        page.BackColor = NvxTheme.Ink;
        Panel card = new Panel();
        card.Dock = DockStyle.Top;
        card.Height = 204;
        card.BackColor = NvxTheme.Card;
        card.Padding = new Padding(8, 8, 8, 8);
        _webStatus = StatusLabel();
        _apacheStatus = StatusLabel();
        _mysqlStatus = StatusLabel();
        _webButton = ToggleButton();
        _apacheButton = ToggleButton();
        _mysqlButton = ToggleButton();
        _webButton.Click += delegate { Toggle("web"); };
        _apacheButton.Click += delegate { Toggle("apache"); };
        _mysqlButton.Click += delegate { Toggle("mysql"); };
        Panel web = FunctionRow("Web", _webStatus, _webButton);
        Panel apache = FunctionRow("Apache", _apacheStatus, _apacheButton);
        Panel mysql = FunctionRow("MySQL", _mysqlStatus, _mysqlButton);
        card.Controls.Add(mysql);
        card.Controls.Add(apache);
        card.Controls.Add(web);

        _projects = new Panel();
        _projects.Dock = DockStyle.Top;
        _projects.Height = 120;
        _projects.BackColor = NvxTheme.Card;
        _projects.Margin = new Padding(0, 12, 0, 0);
        _projects.Padding = new Padding(0, 8, 0, 8);

        Label logTitle = NvxTheme.Mute("Log");
        logTitle.Dock = DockStyle.Top;
        logTitle.Height = 32;
        logTitle.Padding = new Padding(0, 12, 0, 0);
        _logBox = new TextBox();
        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = NvxTheme.MonoFont;
        _logBox.BackColor = NvxTheme.Card;
        _logBox.ForeColor = NvxTheme.Text;
        _logBox.BorderStyle = BorderStyle.None;
        page.Controls.Add(_logBox);
        page.Controls.Add(logTitle);
        page.Controls.Add(_projects);
        Panel gap = new Panel();
        gap.Dock = DockStyle.Top;
        gap.Height = 12;
        gap.BackColor = NvxTheme.Ink;
        page.Controls.Add(gap);
        page.Controls.Add(card);
        return page;
    }

    static Label StatusLabel()
    {
        Label label = new Label();
        label.AutoSize = false;
        label.Size = new Size(360, 24);
        label.Location = new Point(140, 18);
        label.Font = NvxTheme.UiFont;
        label.ForeColor = NvxTheme.Muted;
        return label;
    }

    static PowerButton ToggleButton()
    {
        PowerButton button = new PowerButton();
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Location = new Point(640, 12);
        return button;
    }

    static Panel FunctionRow(string name, Label status, PowerButton button)
    {
        Panel row = new Panel();
        row.Dock = DockStyle.Top;
        row.Height = 60;
        row.BackColor = NvxTheme.Card;
        Label title = new Label();
        title.Text = name;
        title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        title.ForeColor = NvxTheme.Text;
        title.AutoSize = true;
        title.Location = new Point(12, 16);
        row.Controls.Add(title);
        row.Controls.Add(status);
        row.Controls.Add(button);
        row.Resize += delegate { button.Left = row.ClientSize.Width - button.Width - 12; };
        return row;
    }

    Panel PageDatabases()
    {
        Panel page = new Panel();
        page.BackColor = NvxTheme.Ink;
        Panel header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = 88;
        header.BackColor = NvxTheme.Ink;
        Label lead = NvxTheme.Mute("Local databases on this computer.");
        lead.Location = new Point(0, 8);
        TextBox name = NvxTheme.Box();
        name.Location = new Point(0, 40);
        name.Width = 260;
        name.PlaceholderTextCompat("Database name");
        Button create = NvxTheme.Button("Create");
        create.Location = new Point(272, 36);
        create.Click += delegate
        {
            Run(delegate { _databases.Create(name.Text.Trim()); name.Text = ""; LoadDatabases(); });
        };
        header.Controls.Add(lead);
        header.Controls.Add(name);
        header.Controls.Add(create);
        _dbList = new ListView();
        _dbList.Dock = DockStyle.Fill;
        _dbList.View = View.Details;
        _dbList.FullRowSelect = true;
        _dbList.BackColor = NvxTheme.Card;
        _dbList.ForeColor = NvxTheme.Text;
        _dbList.BorderStyle = BorderStyle.None;
        _dbList.Columns.Add("Name", 240);
        _dbList.Columns.Add("Tables", 100);
        _dbList.Columns.Add("Size", 120);
        _dbList.Columns.Add("Updated", 180);
        Button open = NvxTheme.Button("Open");
        Button remove = NvxTheme.Button("Delete");
        remove.ForeColor = NvxTheme.Red;
        open.Location = new Point(0, 8);
        remove.Location = new Point(90, 8);
        open.Click += delegate { OpenSelectedDatabase(); };
        remove.Click += delegate { DeleteSelectedDatabase(); };
        _dbList.DoubleClick += delegate { OpenSelectedDatabase(); };
        Panel footer = new Panel();
        footer.Dock = DockStyle.Bottom;
        footer.Height = 48;
        footer.BackColor = NvxTheme.Ink;
        footer.Controls.Add(open);
        footer.Controls.Add(remove);
        page.Controls.Add(_dbList);
        page.Controls.Add(header);
        page.Controls.Add(footer);
        return page;
    }

    Panel PageApps()
    {
        Panel page = new Panel();
        page.BackColor = NvxTheme.Ink;
        Panel header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = 72;
        header.BackColor = NvxTheme.Ink;
        Label lead = NvxTheme.Mute("Any drive works. Each project is a folder here.");
        lead.Location = new Point(0, 0);
        Label path = NvxTheme.Mute(AppsFolder());
        path.Location = new Point(0, 24);
        path.AutoEllipsis = true;
        path.AutoSize = false;
        path.Width = 640;
        header.Controls.Add(lead);
        header.Controls.Add(path);
        _appList = new ListView();
        _appList.Dock = DockStyle.Fill;
        _appList.View = View.Details;
        _appList.FullRowSelect = true;
        _appList.BackColor = NvxTheme.Card;
        _appList.ForeColor = NvxTheme.Text;
        _appList.BorderStyle = BorderStyle.None;
        _appList.Columns.Add("App", 220);
        _appList.Columns.Add("Folder", 460);
        Button openApp = NvxTheme.Button("Open");
        openApp.Location = new Point(0, 8);
        openApp.Click += delegate { OpenSelectedApp(); };
        Button folder = NvxTheme.Button("Open folder");
        folder.Location = new Point(90, 8);
        folder.Click += delegate
        {
            Process.Start(AppsFolder());
        };
        _appList.DoubleClick += delegate { OpenSelectedApp(); };
        Panel footer = new Panel();
        footer.Dock = DockStyle.Bottom;
        footer.Height = 48;
        footer.BackColor = NvxTheme.Ink;
        footer.Controls.Add(openApp);
        footer.Controls.Add(folder);
        page.Controls.Add(_appList);
        page.Controls.Add(header);
        page.Controls.Add(footer);
        return page;
    }

    Panel PageSettings()
    {
        Panel page = new Panel();
        page.BackColor = NvxTheme.Ink;
        _nameBox = Field(page, "Server name", 8);
        _zoneBox = Field(page, "Timezone", 78);
        _httpBox = Field(page, "HTTP port", 148);
        _mysqlBox = Field(page, "MySQL port", 218);
        Label bind = NvxTheme.Mute("Only this computer can connect. Restart Web after a port change.");
        bind.Location = new Point(0, 292);
        Button save = NvxTheme.Button("Save");
        save.Location = new Point(0, 324);
        save.Click += delegate { SaveSettings(); };
        page.Controls.Add(bind);
        page.Controls.Add(save);
        return page;
    }

    TextBox Field(Control page, string caption, int top)
    {
        Label label = NvxTheme.Mute(caption);
        label.Location = new Point(0, top);
        TextBox box = NvxTheme.Box();
        box.Location = new Point(0, top + 24);
        box.Width = 280;
        page.Controls.Add(label);
        page.Controls.Add(box);
        return box;
    }

    void Toggle(string key)
    {
        if (_webActionInProgress) return;
        if (key == "web")
        {
            ToggleWeb();
            return;
        }
        try
        {
            if (IsOn(key))
            {
                _services.StopComponent(key);
                if (key == "apache") _services.StartWeb();
            }
            else
            {
                string message = _services.StartComponent(key);
                if (!IsOn(key)) _log.Server(message);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
        }
        RefreshStatus();
    }

    void ToggleWeb()
    {
        if (_webActionInProgress) return;
        bool starting = !IsOn("web");
        _webActionInProgress = true;
        SetServiceButtonsEnabled(false);
        _webStatus.Text = starting ? "Starting..." : "Stopping...";
        _webStatus.ForeColor = NvxTheme.Muted;

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                if (starting) _services.StartWeb();
                else _services.StopWeb();
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message);
            }
            finally
            {
                if (_closing) _services.StopWeb();
                if (!_closing && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _webActionInProgress = false;
                            SetServiceButtonsEnabled(true);
                            RefreshStatus();
                        });
                    }
                    catch (InvalidOperationException) { }
                }
            }
        });
    }

    void SetServiceButtonsEnabled(bool enabled)
    {
        _webButton.Enabled = enabled;
        _apacheButton.Enabled = enabled;
        _mysqlButton.Enabled = enabled;
    }

    bool IsOn(string key)
    {
        if (key == "web") return _services.WebRunning;
        return _services.Status(key) == "Running";
    }

    void RefreshStatus()
    {
        if (_address == null) return;
        _address.Text = _services.Url();
        if (!_webActionInProgress)
        {
            PaintFunction(_webStatus, _webButton, _services.WebRunning ? "Running" : "Stopped", _services.WebRunning);
        }
        PaintFunction(_apacheStatus, _apacheButton, ComponentText("apache"), IsOn("apache"));
        PaintFunction(_mysqlStatus, _mysqlButton, ComponentText("mysql"), IsOn("mysql"));
        LoadProjectLinks();
        string story = _log.Story(160);
        if (story != _logText)
        {
            _logText = story;
            _logBox.Text = story;
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    static void PaintFunction(Label status, PowerButton button, string text, bool active)
    {
        status.Text = text;
        status.ForeColor = active ? NvxTheme.Green : NvxTheme.Muted;
        NvxTheme.MarkActive(button, active);
    }

    string ComponentText(string name)
    {
        if (!_services.ComponentInstalled(name)) return "Not installed";
        return _services.Status(name);
    }

    void LoadDatabases()
    {
        _dbList.Items.Clear();
        List<string> names = _databases.List();
        for (int i = 0; i < names.Count; i++)
        {
            string path = _databases.PathOf(names[i]);
            FileInfo info = new FileInfo(path);
            int tables = 0;
            try { tables = _databases.Tables(names[i]).Count; } catch (Exception) { }
            ListViewItem item = new ListViewItem(names[i]);
            item.SubItems.Add(tables.ToString());
            item.SubItems.Add(info.Length + " bytes");
            item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            _dbList.Items.Add(item);
        }
    }

    void OpenSelectedDatabase()
    {
        if (_dbList.SelectedItems.Count == 0) return;
        string name = _dbList.SelectedItems[0].Text;
        using (DatabaseForm form = new DatabaseForm(_databases, name)) form.ShowDialog(this);
        LoadDatabases();
    }

    void DeleteSelectedDatabase()
    {
        if (_dbList.SelectedItems.Count == 0) return;
        string name = _dbList.SelectedItems[0].Text;
        string typed = Prompt.Ask(this, "Delete database", "Type " + name + " to delete it.");
        if (typed == null) return;
        Run(delegate { _databases.Drop(name, typed); LoadDatabases(); });
    }

    void LoadApps()
    {
        _appList.Items.Clear();
        string[] folders = ProjectFolders();
        for (int i = 0; i < folders.Length; i++)
        {
            ListViewItem item = new ListViewItem(Path.GetFileName(folders[i]));
            item.SubItems.Add(folders[i]);
            _appList.Items.Add(item);
        }
    }

    void LoadProjectLinks()
    {
        if (_projects == null) return;
        string[] folders = ProjectFolders();
        StringBuilder key = new StringBuilder(AppsFolder());
        for (int i = 0; i < folders.Length; i++) key.Append("|").Append(folders[i]);
        if (key.ToString() == _projectKey) return;
        _projectKey = key.ToString();
        _projects.Controls.Clear();
        Label title = new Label();
        title.Text = "Projects";
        title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        title.ForeColor = NvxTheme.Text;
        title.AutoSize = true;
        title.Location = new Point(12, 10);
        Label note = NvxTheme.Mute("Any drive works. Each project is a folder in " + AppsFolder());
        note.Location = new Point(12, 34);
        note.AutoSize = false;
        note.Size = new Size(620, 36);
        _projects.Controls.Add(title);
        _projects.Controls.Add(note);
        int top = 74;
        if (folders.Length == 0)
        {
            Label empty = NvxTheme.Mute("No projects yet.");
            empty.Location = new Point(12, top);
            _projects.Controls.Add(empty);
            top += 28;
        }
        for (int i = 0; i < folders.Length; i++)
        {
            string name = Path.GetFileName(folders[i]);
            Label label = new Label();
            label.Text = name;
            label.ForeColor = NvxTheme.Text;
            label.Font = NvxTheme.UiFont;
            label.AutoSize = true;
            label.Location = new Point(12, top);
            LinkLabel link = new LinkLabel();
            link.Text = "Open";
            link.AutoSize = true;
            link.Location = new Point(220, top);
            link.LinkColor = NvxTheme.Green;
            link.ActiveLinkColor = NvxTheme.Text;
            link.VisitedLinkColor = NvxTheme.Green;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.Font = NvxTheme.UiFont;
            string project = name;
            link.LinkClicked += delegate { OpenProject(project); };
            _projects.Controls.Add(label);
            _projects.Controls.Add(link);
            top += 28;
        }
        _projects.Height = top + 12;
    }

    string AppsFolder()
    {
        return Path.Combine(_root, "nvxh\\apps");
    }

    string[] ProjectFolders()
    {
        string folder = AppsFolder();
        if (!Directory.Exists(folder)) return new string[0];
        List<string> projects = new List<string>();
        foreach (string project in Directory.GetDirectories(folder))
        {
            if (File.Exists(Path.Combine(project, "index.php")) || File.Exists(Path.Combine(project, "index.html")))
            {
                projects.Add(project);
            }
        }
        return projects.ToArray();
    }

    void OpenSelectedApp()
    {
        if (_appList.SelectedItems.Count == 0) return;
        OpenProject(_appList.SelectedItems[0].Text);
    }

    void OpenProject(string name)
    {
        if (name == null || name.IndexOf("..") >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0) return;
        string project = Path.Combine(AppsFolder(), name);
        if (!Directory.Exists(project)) return;
        if (!File.Exists(Path.Combine(project, "index.php")) && !File.Exists(Path.Combine(project, "index.html")))
        {
            MessageBox.Show(this, "This project needs an index.php or index.html file in its folder.", "Cannot open project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (!_services.WebRunning) _services.StartWeb();
            string url = _services.Url();
            if (!url.EndsWith("/")) url += "/";
            Process.Start(url + "apps/" + Uri.EscapeDataString(name) + "/");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            _log.Error("Could not open project " + name + ": " + ex.Message);
            MessageBox.Show(this, "Could not open the project. " + ex.Message, "Cannot open project", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }
    }

    void LoadSettings()
    {
        _nameBox.Text = _config.Get("server", "name", "NVX Server");
        _zoneBox.Text = _config.Get("server", "timezone", "UTC");
        _httpBox.Text = _config.Port("http", 80).ToString();
        _mysqlBox.Text = _config.Port("mysql", 3307).ToString();
    }

    void SaveSettings()
    {
        int http;
        int mysql;
        if (!int.TryParse(_httpBox.Text.Trim(), out http) || http < 1 || http > 65535)
        {
            _log.Error("Enter an HTTP port from 1 to 65535.");
            RefreshStatus();
            return;
        }
        if (!int.TryParse(_mysqlBox.Text.Trim(), out mysql) || mysql < 1 || mysql > 65535)
        {
            _log.Error("Enter a MySQL port from 1 to 65535.");
            RefreshStatus();
            return;
        }
        Dictionary<string, string> server = new Dictionary<string, string>();
        server["name"] = _nameBox.Text.Trim().Length == 0 ? "NVX Server" : _nameBox.Text.Trim();
        server["timezone"] = _zoneBox.Text.Trim().Length == 0 ? "UTC" : _zoneBox.Text.Trim();
        _config.Update("server", server);
        Dictionary<string, string> ports = new Dictionary<string, string>();
        ports["http"] = http.ToString();
        ports["mysql"] = mysql.ToString();
        _config.Update("ports", ports);
        Text = server["name"];
        _log.Server("Settings saved. Turn Web off and on to use a new HTTP port.");
        ShowPage("status");
    }

    void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
        }
        RefreshStatus();
    }
}

static class Prompt
{
    public static string Ask(IWin32Window owner, string title, string label)
    {
        Form form = new Form();
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(440, 150);
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.BackColor = NvxTheme.Ink;
        form.ForeColor = NvxTheme.Text;
        form.Font = NvxTheme.UiFont;
        Label caption = NvxTheme.Mute(label);
        caption.Location = new Point(16, 16);
        TextBox box = NvxTheme.Box();
        box.Location = new Point(16, 48);
        box.Width = 400;
        Button ok = NvxTheme.Button("OK");
        ok.Location = new Point(16, 96);
        Button cancel = NvxTheme.Button("Cancel");
        cancel.Location = new Point(90, 96);
        string result = null;
        ok.Click += delegate { result = box.Text.Trim(); form.DialogResult = DialogResult.OK; };
        cancel.Click += delegate { form.DialogResult = DialogResult.Cancel; };
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Controls.Add(caption);
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        return result;
    }
}

static class TextBoxCompat
{
    public static void PlaceholderTextCompat(this TextBox box, string text)
    {
        box.ForeColor = NvxTheme.Muted;
        box.Text = text;
        box.GotFocus += delegate
        {
            if (box.Text == text)
            {
                box.Text = "";
                box.ForeColor = NvxTheme.Text;
            }
        };
        box.LostFocus += delegate
        {
            if (box.Text.Length == 0)
            {
                box.Text = text;
                box.ForeColor = NvxTheme.Muted;
            }
        };
    }
}

sealed class DatabaseForm : Form
{
    readonly NvxDatabase _databases;
    readonly string _name;
    ListBox _tables;
    DataGridView _grid;
    TextBox _sql;
    Label _result;

    public DatabaseForm(NvxDatabase databases, string name)
    {
        _databases = databases;
        _name = name;
        Text = name;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 640);
        MinimumSize = new Size(760, 520);
        BackColor = NvxTheme.Ink;
        ForeColor = NvxTheme.Text;
        Font = NvxTheme.UiFont;
        Build();
        LoadTables();
    }

    void Build()
    {
        Label title = new Label();
        title.Text = _name;
        title.Font = NvxTheme.TitleFont;
        title.AutoSize = true;
        title.Location = new Point(16, 12);
        _tables = new ListBox();
        _tables.Location = new Point(16, 64);
        _tables.Size = new Size(220, 360);
        _tables.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _tables.BackColor = NvxTheme.Card;
        _tables.ForeColor = NvxTheme.Text;
        _tables.BorderStyle = BorderStyle.None;
        _tables.SelectedIndexChanged += delegate { BrowseSelected(); };
        Button create = NvxTheme.Button("New table");
        Button drop = NvxTheme.Button("Drop table");
        drop.ForeColor = NvxTheme.Red;
        create.Location = new Point(16, 436);
        drop.Location = new Point(120, 436);
        create.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        drop.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        create.Click += delegate { CreateTable(); };
        drop.Click += delegate { DropTable(); };
        _grid = new DataGridView();
        _grid.Location = new Point(252, 64);
        _grid.Size = new Size(612, 280);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        NvxTheme.StyleGrid(_grid);
        _sql = NvxTheme.Box();
        _sql.Multiline = true;
        _sql.Font = NvxTheme.MonoFont;
        _sql.Location = new Point(252, 360);
        _sql.Size = new Size(500, 90);
        _sql.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Button run = NvxTheme.Button("Run SQL");
        run.Location = new Point(764, 360);
        run.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        run.Click += delegate { RunSql(); };
        _result = NvxTheme.Mute("");
        _result.Location = new Point(252, 460);
        _result.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(title);
        Controls.Add(_tables);
        Controls.Add(create);
        Controls.Add(drop);
        Controls.Add(_grid);
        Controls.Add(_sql);
        Controls.Add(run);
        Controls.Add(_result);
    }

    void LoadTables()
    {
        _tables.Items.Clear();
        List<string> tables = _databases.Tables(_name);
        for (int i = 0; i < tables.Count; i++) _tables.Items.Add(tables[i]);
    }

    void BrowseSelected()
    {
        if (_tables.SelectedItem == null) return;
        string table = _tables.SelectedItem.ToString();
        ShowSet(_databases.Query(_name, "SELECT * FROM \"" + table + "\""));
    }

    void CreateTable()
    {
        using (TableForm form = new TableForm())
        {
            if (form.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _databases.CreateTable(_name, form.TableName, form.Columns);
                LoadTables();
                _result.Text = "Table created.";
            }
            catch (Exception ex)
            {
                _result.Text = ex.Message;
                _result.ForeColor = NvxTheme.Red;
            }
        }
    }

    void DropTable()
    {
        if (_tables.SelectedItem == null) return;
        string table = _tables.SelectedItem.ToString();
        if (MessageBox.Show(this, "Drop table " + table + "?", "NVX Server", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            _databases.DropTable(_name, table);
            LoadTables();
            _grid.Columns.Clear();
            _grid.Rows.Clear();
        }
        catch (Exception ex)
        {
            _result.Text = ex.Message;
            _result.ForeColor = NvxTheme.Red;
        }
    }

    void RunSql()
    {
        try
        {
            string sql = _sql.Text.Trim();
            if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                ShowSet(_databases.Query(_name, sql));
            }
            else
            {
                int changed = _databases.Execute(_name, sql);
                _result.Text = changed + " row(s) changed.";
                _result.ForeColor = NvxTheme.Muted;
                LoadTables();
            }
        }
        catch (Exception ex)
        {
            _result.Text = ex.Message;
            _result.ForeColor = NvxTheme.Red;
        }
    }

    void ShowSet(NvxRowSet set)
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        for (int i = 0; i < set.Columns.Count; i++) _grid.Columns.Add(set.Columns[i], set.Columns[i]);
        for (int r = 0; r < set.Rows.Count; r++) _grid.Rows.Add(set.Rows[r]);
        _result.Text = set.Rows.Count + " row(s)" + (set.Truncated ? " (first 100)" : "");
        _result.ForeColor = NvxTheme.Muted;
    }
}

sealed class TableForm : Form
{
    readonly TextBox _name = NvxTheme.Box();
    readonly List<TextBox> _columns = new List<TextBox>();
    readonly List<ComboBox> _types = new List<ComboBox>();
    public string TableName;
    public List<string[]> Columns = new List<string[]>();

    public TableForm()
    {
        Text = "New table";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 360);
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = NvxTheme.Ink;
        ForeColor = NvxTheme.Text;
        Font = NvxTheme.UiFont;
        Label caption = NvxTheme.Mute("Table name");
        caption.Location = new Point(16, 16);
        _name.Location = new Point(16, 40);
        _name.Width = 420;
        Controls.Add(caption);
        Controls.Add(_name);
        for (int i = 0; i < 5; i++)
        {
            TextBox column = NvxTheme.Box();
            column.Location = new Point(16, 84 + (i * 36));
            column.Width = 250;
            ComboBox type = new ComboBox();
            type.DropDownStyle = ComboBoxStyle.DropDownList;
            type.Location = new Point(276, 84 + (i * 36));
            type.Width = 160;
            type.BackColor = NvxTheme.Panel;
            type.ForeColor = NvxTheme.Text;
            type.Items.AddRange(new object[] { "TEXT", "INTEGER", "REAL", "NUMERIC", "BLOB" });
            type.SelectedIndex = 0;
            _columns.Add(column);
            _types.Add(type);
            Controls.Add(column);
            Controls.Add(type);
        }
        Button ok = NvxTheme.Button("Create");
        ok.Location = new Point(16, 280);
        ok.Click += delegate { Accept(); };
        Button cancel = NvxTheme.Button("Cancel");
        cancel.Location = new Point(110, 280);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    void Accept()
    {
        TableName = _name.Text.Trim();
        Columns.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            string column = _columns[i].Text.Trim();
            if (column.Length == 0) continue;
            Columns.Add(new string[] { column, _types[i].SelectedItem.ToString() });
        }
        if (TableName.Length == 0 || Columns.Count == 0)
        {
            MessageBox.Show(this, "Enter a table name and at least one column.", "NVX Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
    }
}
