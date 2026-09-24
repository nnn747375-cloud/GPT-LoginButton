using System.Diagnostics;
using GptLoginButton;

namespace GptLoginButton.Demo;

internal sealed class DemoForm : Form
{
    private static readonly Color Ink = Color.FromArgb(29, 42, 39);
    private static readonly Color Muted = Color.FromArgb(107, 119, 115);
    private static readonly Color Accent = Color.FromArgb(24, 105, 85);
    private static readonly Color Soft = Color.FromArgb(244, 247, 245);
    private static readonly Color Line = Color.FromArgb(226, 232, 228);
    private readonly GptLoginButton _loginButton = new();
    private GptCodexClient _client = new();
    private readonly Label _connection = new(), _workspace = new(), _status = new(), _activityHeading = new();
    private readonly RichTextBox _activity = new();
    private readonly TextBox _composer = new(), _model = new();
    private readonly CheckBox _tools = new();
    private readonly Button _send = new(), _stop = new(), _chooseFolder = new(), _newChat = new(), _sample = new();
    private readonly FlowLayoutPanel _messages = new();
    private readonly Panel _empty = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private string? _selectedWorkspace;
    private bool _connected;
    private bool _resourcesDisposed;
    private int _toolCount, _turnCount;

    public DemoForm()
    {
        Text = "ChatGPT Studio · GPT-LoginButton";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ForeColor = Ink;
        BackColor = Color.White;
        MinimumSize = new Size(920, 720);
        ClientSize = new Size(1120, 800);
        BuildUi();
        SetReadyState();
        FormClosed += (_, _) => { _operation?.Cancel(); _lifetime.Cancel(); };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _operation?.Cancel();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _ = _client.DisposeAsync();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 278));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        shell.Controls.Add(BuildSidebar(), 0, 0);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(34, 26, 34, 18), Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        shell.Controls.Add(main, 1, 0);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        header.Controls.Add(MakeLabel("Your workspace", 21, Ink), 0, 0);
        header.Controls.Add(MakeLabel("A conversation. A few useful tools.", 9.5f, Muted), 0, 1);
        var modelCaption = MakeLabel("MODEL", 8, Muted, FontStyle.Bold);
        modelCaption.TextAlign = ContentAlignment.BottomLeft;
        modelCaption.Padding = new Padding(2, 0, 0, 6);
        header.Controls.Add(modelCaption, 1, 0);
        _model.PlaceholderText = "Account default";
        _model.AccessibleName = "Optional model name; leave empty for account default";
        _model.Dock = DockStyle.Fill;
        _model.BorderStyle = BorderStyle.FixedSingle;
        _model.Font = new Font("Segoe UI", 9);
        header.Controls.Add(_model, 1, 1);
        main.Controls.Add(header, 0, 0);

        var conversation = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 14) };
        _messages.Dock = DockStyle.Fill;
        _messages.FlowDirection = FlowDirection.TopDown;
        _messages.WrapContents = false;
        _messages.AutoScroll = true;
        _messages.Visible = false;
        _messages.Resize += (_, _) => ResizeMessages();
        _messages.AccessibleName = "Conversation";
        conversation.Controls.Add(_messages);
        BuildEmpty();
        conversation.Controls.Add(_empty);
        _empty.BringToFront();
        main.Controls.Add(conversation, 0, 1);
        var activityPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 5), Margin = Padding.Empty };
        activityPanel.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, 0, 0, activityPanel.Width, 0); };
        _activityHeading.Text = "ACTIVITY";
        _activityHeading.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _activityHeading.ForeColor = Muted;
        _activityHeading.Dock = DockStyle.Top;
        _activityHeading.Height = 25;
        _activity.ReadOnly = true;
        _activity.BorderStyle = BorderStyle.None;
        _activity.BackColor = Color.White;
        _activity.ForeColor = Muted;
        _activity.Font = new Font("Cascadia Mono", 8.5f);
        _activity.Dock = DockStyle.Fill;
        _activity.DetectUrls = false;
        _activity.Text = "Tool activity appears here when you enable tools.";
        _activity.AccessibleName = "Live tool activity";
        activityPanel.Controls.Add(_activity);
        activityPanel.Controls.Add(_activityHeading);
        main.Controls.Add(activityPanel, 0, 2);
        main.Controls.Add(BuildComposer(), 0, 3);
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 8.5f);
        _status.AutoEllipsis = true;
        main.Controls.Add(_status, 0, 4);

        _loginButton.LoginRequested += async (_, _) => await ConnectAsync();
        _loginButton.LogoutRequested += (_, _) => Disconnect();
        _send.Click += async (_, _) => await SendAsync();
        _stop.Click += (_, _) => _operation?.Cancel();
        _newChat.Click += (_, _) => NewChat();
        _chooseFolder.Click += (_, _) => ChooseFolder();
        _sample.Click += (_, _) => PrepareToolExample();
        _tools.CheckedChanged += (_, _) => { if (_operation is null) ConfigurationChanged(); };
        _model.TextChanged += (_, _) => { if (_operation is null) ConfigurationChanged(); };
        _composer.TextChanged += (_, _) => _send.Enabled = _connected && _operation is null && !string.IsNullOrWhiteSpace(_composer.Text);
        _composer.KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAsync(); }
            else if (e.KeyCode == Keys.Escape && _operation is not null) { e.SuppressKeyPress = true; _operation.Cancel(); }
        };
    }

    private Control BuildSidebar()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Soft, Padding = new Padding(22, 32, 22, 24), ColumnCount = 1, RowCount = 14, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        float[] heights = [44, 41, 30, 56, 67, 52, 36, 39, 42, 50, 54, 50];
        foreach (var h in heights) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.Controls.Add(MakeLabel("ChatGPT", 22, Ink, FontStyle.Bold), 0, 0);
        panel.Controls.Add(MakeLabel("S T U D I O", 9, Accent), 0, 1);
        panel.Controls.Add(MakeLabel("ACCOUNT", 8, Muted, FontStyle.Bold), 0, 2);
        _loginButton.Dock = DockStyle.Fill;
        _loginButton.Margin = new Padding(0, 0, 0, 3);
        panel.Controls.Add(_loginButton, 0, 3);
        _connection.Text = "Connect once. Your account stays with the official Codex app.";
        _connection.Dock = DockStyle.Fill;
        _connection.Padding = new Padding(2, 9, 0, 0);
        _connection.ForeColor = Muted;
        _connection.Font = new Font("Segoe UI", 9);
        panel.Controls.Add(_connection, 0, 4);
        StyleButton(_newChat, "+  New conversation", false);
        panel.Controls.Add(_newChat, 0, 5);
        panel.Controls.Add(MakeLabel("TOOLS", 8, Muted, FontStyle.Bold), 0, 6);
        _tools.Text = "Enable read-only tools";
        _tools.Dock = DockStyle.Fill;
        _tools.ForeColor = Ink;
        _tools.AccessibleDescription = "Allows Codex read-only commands. The selected folder is a starting location, not a strict file-access boundary.";
        panel.Controls.Add(_tools, 0, 7);
        StyleButton(_chooseFolder, "Choose working folder…", false);
        panel.Controls.Add(_chooseFolder, 0, 8);
        _workspace.Text = "Demo workspace";
        _workspace.ForeColor = Muted;
        _workspace.Dock = DockStyle.Fill;
        _workspace.Font = new Font("Segoe UI", 8.5f);
        _workspace.Padding = new Padding(2, 6, 0, 0);
        _workspace.AutoEllipsis = true;
        panel.Controls.Add(_workspace, 0, 9);
        StyleButton(_sample, "Try a file-reading task  ↗", false);
        panel.Controls.Add(_sample, 0, 10);
        panel.Controls.Add(MakeLabel("Read-only commands; no file edits. Folder choice sets the starting location.", 8.5f, Muted), 0, 11);
        panel.Controls.Add(MakeLabel("Independent demo\nPowered by your Codex account", 8.5f, Muted), 0, 13);
        return panel;
    }

    private void BuildEmpty()
    {
        _empty.Dock = DockStyle.Fill;
        _empty.BackColor = Color.White;
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 10) };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var heading = MakeLabel("What are we working on?", 25, Ink);
        heading.TextAlign = ContentAlignment.MiddleCenter;
        inner.Controls.Add(heading, 0, 1);
        var text = MakeLabel("Sign in, ask a question, or explore a few files.", 10, Muted);
        text.TextAlign = ContentAlignment.TopCenter;
        inner.Controls.Add(text, 0, 2);
        var suggestion = new Button();
        StyleButton(suggestion, "Help me turn an idea into a simple plan", false);
        suggestion.Dock = DockStyle.None;
        suggestion.Size = new Size(350, 40);
        suggestion.Anchor = AnchorStyles.Top;
        suggestion.Click += (_, _) => { _composer.Text = "Help me turn an idea into a simple plan. Start by asking what I want to build."; _composer.Focus(); };
        inner.Controls.Add(suggestion, 0, 3);
        _empty.Controls.Add(inner);
    }

    private Control BuildComposer()
    {
        var box = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Soft, Padding = new Padding(16, 13, 16, 10), ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 9, 0, 4) };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _composer.Multiline = true;
        _composer.AcceptsReturn = true;
        _composer.ScrollBars = ScrollBars.Vertical;
        _composer.BorderStyle = BorderStyle.None;
        _composer.BackColor = Soft;
        _composer.Font = new Font("Segoe UI", 11);
        _composer.PlaceholderText = "Message ChatGPT…";
        _composer.AccessibleName = "Message ChatGPT";
        _composer.Dock = DockStyle.Fill;
        box.Controls.Add(_composer, 0, 0);
        box.SetColumnSpan(_composer, 3);
        var hint = MakeLabel("Ctrl + Enter to send", 8, Muted);
        hint.TextAlign = ContentAlignment.MiddleLeft;
        box.Controls.Add(hint, 0, 1);
        StyleButton(_stop, "Stop", false);
        StyleButton(_send, "Send  ↑", true);
        box.Controls.Add(_stop, 1, 1);
        box.Controls.Add(_send, 2, 1);
        return box;
    }

    private async Task ConnectAsync()
    {
        if (_operation is not null) return;
        using var operation = BeginOperation("Opening your ChatGPT connection…");
        _loginButton.SetSigningIn();
        try
        {
            RecreateClient();
            var connection = await _client.ConnectAsync(operation.Token);
            if (IsDisposed) return;
            _connected = true;
            _loginButton.SetConnected("ChatGPT");
            _connection.Text = connection.ReusedSession ? "Connected with your existing account." : "Connected. You're ready to chat.";
            _status.Text = "Connected · send your first message";
        }
        catch (OperationCanceledException) { if (!IsDisposed) { _loginButton.SetSignedOut(); _status.Text = "Connection cancelled. You can try again."; } }
        catch (Exception ex) { if (!IsDisposed) { _loginButton.SetError(ex.Message); _connection.Text = "Connection needs attention."; ShowError(ex.Message); } }
        finally { EndOperation(); }
    }

    private async Task SendAsync()
    {
        var prompt = _composer.Text.Trim();
        if (!_connected || _operation is not null || prompt.Length == 0) return;
        using var operation = BeginOperation("Thinking…");
        _toolCount = 0;
        _activity.Clear();
        _activityHeading.Text = "ACTIVITY";
        AddMessage("YOU", prompt, true);
        _composer.Clear();
        var timer = Stopwatch.StartNew();
        var progress = new Progress<GptCodexProgress>(update =>
        {
            if (IsDisposed || _operation != operation) return;
            if (update.Kind == "tool") { _toolCount++; _activityHeading.Text = $"ACTIVITY · {_toolCount} TOOL EVENTS"; AppendActivity(update.Text); _status.Text = "Using tools…"; }
            else if (update.Kind == "status") _status.Text = update.Text;
        });
        try
        {
            var response = await _client.SendAsync(prompt, progress, operation.Token);
            if (IsDisposed) return;
            AddMessage("CHATGPT", response, false);
            _turnCount++;
            _status.Text = $"Complete · {timer.Elapsed.TotalSeconds:0.0}s · {_turnCount} {(_turnCount == 1 ? "turn" : "turns")}";
            if (_activity.TextLength == 0) _activity.Text = "Answered without using tools.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) { _composer.Text = prompt; _status.Text = "Stopped. Your message is ready to edit or resend."; AddMessage("STOPPED", "This request was cancelled. No completed reply was added to conversation context.", false); } }
        catch (Exception ex) { if (!IsDisposed) { _composer.Text = prompt; ShowError(ex.Message); } }
        finally { EndOperation(); }
    }

    private CancellationTokenSource BeginOperation(string status)
    {
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _status.Text = status;
        SetReadyState();
        return _operation;
    }
    private void EndOperation() { _operation = null; if (!IsDisposed) { SetReadyState(); _composer.Focus(); } }
    private void SetReadyState()
    {
        var ready = _operation is null;
        _loginButton.Enabled = ready;
        _send.Enabled = ready && _connected && !string.IsNullOrWhiteSpace(_composer.Text);
        _stop.Enabled = !ready;
        _tools.Enabled = _model.Enabled = _chooseFolder.Enabled = _newChat.Enabled = _sample.Enabled = ready;
        _composer.ReadOnly = !ready;
        if (_status.Text.Length == 0) _status.Text = "Ready when you are · connect your account to begin";
    }
    private void Disconnect()
    {
        if (_operation is not null) return;
        _client.Disconnect(); _connected = false; _loginButton.SetSignedOut();
        _connection.Text = "Disconnected from this demo. Your Codex account remains signed in.";
        NewChat(); SetReadyState();
    }
    private void RecreateClient()
    {
        var next = new GptCodexClient(_selectedWorkspace, _tools.Checked, string.IsNullOrWhiteSpace(_model.Text) ? null : _model.Text.Trim());
        var previous = _client;
        _client = next;
        _ = previous.DisposeAsync();
    }
    private void ConfigurationChanged()
    {
        var wasConnected = _connected;
        _connected = false; _client.Disconnect(); _loginButton.SetSignedOut(); NewChat(clearDraft: false);
        if (wasConnected) _connection.Text = "Settings changed. Reconnect to apply them.";
        SetReadyState();
    }
    private void NewChat(bool clearDraft = true)
    {
        _client.ResetConversation();
        foreach (Control control in _messages.Controls.Cast<Control>().ToArray()) control.Dispose();
        _turnCount = 0; _messages.Visible = false; _empty.Visible = true;
        if (clearDraft) _composer.Clear();
        _activity.Text = "Tool activity appears here when you enable tools.";
        _activityHeading.Text = "ACTIVITY";
        _status.Text = _connected ? "New conversation · ready" : "Connect your account to begin";
        _composer.Focus();
    }
    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the starting folder for read-only tools", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _selectedWorkspace = dialog.SelectedPath; _workspace.Text = _selectedWorkspace; ConfigurationChanged();
    }
    private void PrepareToolExample()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPT-LoginButton", "DemoWorkspace");
            Directory.CreateDirectory(path);
            var sample = Path.Combine(path, "studio-example.txt");
            if (!File.Exists(sample)) File.WriteAllText(sample, "Project: Pocket Garden\nGoal: A simple plant watering reminder.\nFeatures: Plant list, next watering date, gentle reminders.\nFirst step: Build a single screen with three sample plants.\n");
            _selectedWorkspace = path; _workspace.Text = "Demo workspace · studio-example.txt";
            _tools.Checked = true; ConfigurationChanged();
            _composer.Text = "Use a read-only tool to read studio-example.txt in the current working folder. Summarize the idea and suggest three practical next steps.";
            _status.Text = "Example prepared · reconnect, then send";
        }
        catch (Exception ex) { ShowError("Could not prepare the demo file: " + ex.Message); }
    }
    private void ShowError(string error) { _status.Text = "Request needs attention · details in the conversation"; AddMessage("NEEDS ATTENTION", error, false); }
    private void AppendActivity(string text)
    {
        if (_activity.TextLength > 20000) _activity.Text = _activity.Text[^12000..];
        _activity.AppendText(text + Environment.NewLine); _activity.SelectionStart = _activity.TextLength; _activity.ScrollToCaret();
    }
    private void AddMessage(string role, string text, bool user)
    {
        _empty.Visible = false;
        _messages.Visible = true;
        var card = new Panel { Padding = new Padding(16, 12, 16, 14), Margin = new Padding(0, 0, 0, 18), BackColor = user ? Soft : Color.White };
        var caption = MakeLabel(role, 8, user ? Accent : Muted, FontStyle.Bold);
        caption.Dock = DockStyle.Top; caption.Height = 27;
        var content = new RichTextBox { Text = text, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = card.BackColor, ForeColor = Ink, Font = new Font("Segoe UI", 11), DetectUrls = false, ScrollBars = RichTextBoxScrollBars.None, Dock = DockStyle.Fill, TabStop = true, AccessibleName = role + " message" };
        card.Controls.Add(content); card.Controls.Add(caption); _messages.Controls.Add(card);
        ResizeMessages(); _messages.ScrollControlIntoView(card);
    }
    private void ResizeMessages()
    {
        foreach (Control card in _messages.Controls)
        {
            card.Width = Math.Max(240, _messages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
            var content = card.Controls.OfType<RichTextBox>().First();
            content.Width = Math.Max(180, card.Width - 32);
            var last = content.GetPositionFromCharIndex(content.TextLength);
            card.Height = Math.Max(82, last.Y + content.Font.Height + 58);
        }
    }
    private static Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        => new() { Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color, Dock = DockStyle.Fill, Margin = Padding.Empty, UseMnemonic = false };
    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text; button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 3, 4, 5); button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1; button.FlatAppearance.BorderColor = Line;
        button.BackColor = primary ? Accent : Color.White; button.ForeColor = primary ? Color.White : Ink;
        button.Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand; button.UseVisualStyleBackColor = false; button.AutoEllipsis = true;
    }
}
