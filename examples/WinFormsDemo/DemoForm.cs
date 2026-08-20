using System.ComponentModel;
using System.Drawing.Drawing2D;
using GptLoginButton;

namespace GptLoginButton.Demo;

internal sealed class DemoForm : Form
{
    private static readonly Color Ink = Color.FromArgb(23, 25, 24);
    private static readonly Color Muted = Color.FromArgb(77, 90, 84);
    private static readonly Color Teal = Color.FromArgb(23, 107, 91);
    private static readonly Color Canvas = Color.FromArgb(243, 244, 246);
    private static readonly Color Card = Color.FromArgb(255, 255, 255);
    private static readonly Color Line = Color.FromArgb(205, 214, 209);

    private readonly GptLoginButton _loginButton = new();
    private readonly GptLocalClient _client = new();
    private readonly Label _connectionStatus = new();
    private readonly Label _modelStatus = new();
    private readonly Label _statusBar = new();
    private readonly Label _warning = new();
    private readonly Button _cancelButton = new();
    private readonly Button _sendButton = new();
    private readonly ComboBox _modelSelector = new();
    private readonly TextBox _composer = new();
    private readonly FlowLayoutPanel _messages = new();
    private readonly Panel _chatPanel = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<GptMessage> _history = [];
    private CancellationTokenSource? _operation;

    public DemoForm()
    {
        Text = "GPT-LoginButton · real ChatGPT demo";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(820, 650);
        ClientSize = new Size(960, 760);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        BuildUi();

        Shown += async (_, _) => await RestoreExistingProxyAsync();
        FormClosed += (_, _) =>
        {
            _operation?.Cancel();
            _lifetime.Cancel();
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // The app is closing; no UI error can be shown here.
        }
        finally
        {
            _lifetime.Dispose();
            base.OnFormClosed(e);
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(24, 20, 24, 18),
            BackColor = Canvas,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildWarning(), 0, 1);
        root.Controls.Add(BuildAuthCard(), 0, 2);
        root.Controls.Add(BuildModelBar(), 0, 3);
        root.Controls.Add(BuildChatCard(), 0, 4);
        root.Controls.Add(BuildComposer(), 0, 5);
        root.Controls.Add(BuildStatusBar(), 0, 6);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        var mark = new MarkPanel { Location = new Point(0, 9), Size = new Size(48, 48) };
        var title = new Label
        {
            AutoSize = true,
            Text = "Sign in with ChatGPT",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(62, 4),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = "A real local account flow · no API key field · no fake response",
            ForeColor = Muted,
            Location = new Point(64, 42),
        };
        var badge = new Label
        {
            AutoSize = true,
            Text = "LOCAL · CHATGPT",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Teal,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        badge.Location = new Point(panel.ClientSize.Width - 118, 22);
        panel.Resize += (_, _) => badge.Left = panel.ClientSize.Width - badge.Width;
        panel.Controls.AddRange([mark, title, subtitle, badge]);
        return panel;
    }

    private Control BuildWarning()
    {
        var panel = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(255, 248, 235),
            BorderColor = Color.FromArgb(229, 163, 63),
            Padding = new Padding(18, 13, 18, 10),
        };
        var heading = new Label
        {
            AutoSize = true,
            Text = "WARNING · READ BEFORE USE",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(155, 83, 14),
            Location = new Point(18, 12),
        };
        _warning.AutoSize = false;
        _warning.Dock = DockStyle.Fill;
        _warning.Padding = new Padding(0, 31, 0, 0);
        _warning.ForeColor = Color.FromArgb(106, 76, 45);
        _warning.Text =
            "Needs: Windows 10+, .NET 9, Node.js 20+ with npx, and a ChatGPT account. " +
            "The first click opens the browser for the local openai-oauth sign-in. " +
            "No OpenAI API key is used. Models and image access depend on your account. " +
            "The proxy stays on 127.0.0.1; never share the local auth files.";
        panel.Controls.Add(_warning);
        panel.Controls.Add(heading);
        return panel;
    }

    private Control BuildAuthCard()
    {
        var panel = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Card,
            BorderColor = Line,
            Padding = new Padding(18, 14, 18, 12),
        };
        var eyebrow = new Label
        {
            AutoSize = true,
            Text = "CHATGPT ACCOUNT",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Teal,
            Location = new Point(18, 13),
        };
        _loginButton.Location = new Point(18, 36);
        _loginButton.Size = new Size(302, 54);
        _loginButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _loginButton.LoginRequested += async (_, _) => await ConnectAsync();
        _loginButton.LogoutRequested += async (_, _) => await DisconnectAsync();

        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(88, 34);
        _cancelButton.Location = new Point(334, 46);
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => _operation?.Cancel();

        _connectionStatus.AutoSize = false;
        _connectionStatus.Text = "Ready. Sign in with your ChatGPT account in the browser.";
        _connectionStatus.ForeColor = Muted;
        _connectionStatus.Location = new Point(440, 18);
        _connectionStatus.Size = new Size(460, 62);
        _connectionStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        panel.Controls.AddRange([eyebrow, _loginButton, _cancelButton, _connectionStatus]);
        return panel;
    }

    private Control BuildModelBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        var label = new Label
        {
            AutoSize = true,
            Text = "MODEL",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Teal,
            Location = new Point(4, 8),
        };
        _modelSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelSelector.Enabled = false;
        _modelSelector.Location = new Point(68, 3);
        _modelSelector.Size = new Size(310, 28);
        _modelSelector.Anchor = AnchorStyles.Left;
        _modelSelector.SelectedIndexChanged += (_, _) => UpdateModelStatus();

        _modelStatus.AutoSize = true;
        _modelStatus.Text = "Connect first to load the models available to this ChatGPT account.";
        _modelStatus.ForeColor = Muted;
        _modelStatus.Location = new Point(400, 8);
        _modelStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.AddRange([label, _modelSelector, _modelStatus]);
        return panel;
    }

    private Control BuildChatCard()
    {
        _chatPanel.Dock = DockStyle.Fill;
        _chatPanel.BackColor = Card;
        _chatPanel.Enabled = false;
        _chatPanel.Padding = new Padding(1);

        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Card,
            BorderColor = Line,
            Padding = new Padding(12),
        };
        _messages.Dock = DockStyle.Fill;
        _messages.FlowDirection = FlowDirection.TopDown;
        _messages.WrapContents = false;
        _messages.AutoScroll = true;
        _messages.Padding = new Padding(6);
        _messages.BackColor = Color.FromArgb(249, 250, 250);
        _messages.Resize += (_, _) => ResizeBubbles();
        AddTextBubble("CHATGPT", "Connect your account above, choose a model, and send a real request.", false);
        card.Controls.Add(_messages);
        _chatPanel.Controls.Add(card);
        return _chatPanel;
    }

    private Control BuildComposer()
    {
        var panel = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Card,
            BorderColor = Line,
            Padding = new Padding(14, 12, 14, 12),
        };
        _composer.Multiline = true;
        _composer.AcceptsReturn = true;
        _composer.ScrollBars = ScrollBars.Vertical;
        _composer.PlaceholderText = "Message ChatGPT…  Enter to send · Shift+Enter for a new line";
        _composer.Font = new Font("Segoe UI", 10f);
        _composer.Location = new Point(14, 14);
        _composer.Size = new Size(720, 80);
        _composer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _composer.KeyDown += ComposerKeyDown;

        _sendButton.Text = "Send  ↗";
        _sendButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _sendButton.BackColor = Teal;
        _sendButton.ForeColor = Color.White;
        _sendButton.FlatStyle = FlatStyle.Flat;
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.Size = new Size(126, 54);
        _sendButton.Location = new Point(748, 26);
        _sendButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _sendButton.Enabled = false;
        _sendButton.Click += async (_, _) => await SendAsync();

        panel.Controls.AddRange([_composer, _sendButton]);
        return panel;
    }

    private Control BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Fill;
        _statusBar.AutoEllipsis = true;
        _statusBar.Text = "Local only · no credentials are written to this repository.";
        _statusBar.ForeColor = Muted;
        _statusBar.TextAlign = ContentAlignment.MiddleLeft;
        return _statusBar;
    }

    private async Task RestoreExistingProxyAsync()
    {
        try
        {
            var existing = await _client.TryReuseAsync(_lifetime.Token);
            if (existing is not null)
            {
                ApplyConnection(existing);
                _statusBar.Text = "Reconnected to an existing local ChatGPT proxy.";
            }
        }
        catch (Exception ex)
        {
            _statusBar.Text = ToUiError(ex);
        }
    }

    private async Task ConnectAsync()
    {
        if (_operation is not null)
        {
            return;
        }

        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _loginButton.SetSigningIn();
        _cancelButton.Visible = true;
        _connectionStatus.Text = "Opening the local proxy. If this is the first run, a browser sign-in window will appear.";
        _statusBar.Text = "Waiting for ChatGPT OAuth…";

        try
        {
            var connection = await _client.ConnectAsync(_operation.Token);
            _history.Clear();
            ApplyConnection(connection);
            _statusBar.Text = "Connected locally. Requests go through openai-oauth on 127.0.0.1.";
        }
        catch (OperationCanceledException)
        {
            _loginButton.SetSignedOut();
            _connectionStatus.Text = "Sign-in cancelled. Nothing changed.";
            _statusBar.Text = "Ready to try again.";
        }
        catch (Exception ex)
        {
            var message = ToUiError(ex);
            _loginButton.SetError(message);
            _connectionStatus.Text = message;
            _statusBar.Text = "Connection failed. No credentials were copied or logged.";
        }
        finally
        {
            _cancelButton.Visible = false;
            _operation.Dispose();
            _operation = null;
        }
    }

    private async Task DisconnectAsync()
    {
        _history.Clear();
        try
        {
            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _statusBar.Text = ToUiError(ex);
        }

        _loginButton.SetSignedOut();
        _modelSelector.Items.Clear();
        _modelSelector.Enabled = false;
        _chatPanel.Enabled = false;
        _composer.Clear();
        _sendButton.Enabled = false;
        _connectionStatus.Text = "Disconnected. Your local ChatGPT sign-in remains managed by Codex/openai-oauth.";
        _modelStatus.Text = "Connect first to load the models available to this ChatGPT account.";
        _statusBar.Text = "Local proxy stopped; local auth was not deleted.";
    }

    private void ApplyConnection(GptConnection connection)
    {
        _modelSelector.Items.Clear();
        foreach (var model in connection.Models
                     .OrderBy(model => model.IsImage)
                     .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase))
        {
            _modelSelector.Items.Add(new ModelOption(model));
        }

        var options = _modelSelector.Items.OfType<ModelOption>().ToArray();
        var preferred = options.FirstOrDefault(option =>
            !option.Model.IsImage &&
            (option.Model.Id.Contains("5.6-luna", StringComparison.OrdinalIgnoreCase) ||
             option.Model.Id.Contains("5-6-luna", StringComparison.OrdinalIgnoreCase)));
        preferred ??= options.FirstOrDefault(option => !option.Model.IsImage);

        _modelSelector.SelectedItem = preferred;
        if (preferred is null)
        {
            _modelSelector.SelectedIndex = -1;
        }

        _modelSelector.Enabled = options.Length > 0;
        _chatPanel.Enabled = options.Length > 0;
        _composer.Enabled = options.Length > 0;
        _sendButton.Enabled = preferred is not null;
        _loginButton.SetConnected("ChatGPT");
        _connectionStatus.Text = $"Connected · {connection.Models.Count} account models loaded from the local proxy.";
        if (preferred is null)
        {
            _modelStatus.Text = options.Length == 0
                ? "This account exposed no models."
                : "No non-image chat model was exposed. Choose an image model explicitly if needed.";
            _sendButton.Text = "Send  ↗";
        }
        else
        {
            UpdateModelStatus();
        }
    }

    private void UpdateModelStatus()
    {
        if (_modelSelector.SelectedItem is not ModelOption option)
        {
            _modelStatus.Text = "Select a model to continue.";
            _sendButton.Enabled = false;
            return;
        }

        _modelStatus.Text = option.Model.IsImage
            ? $"Image model · prompt creates an image through {option.Model.Id}."
            : $"Chat model · real account request through {option.Model.Id}.";
        _sendButton.Text = option.Model.IsImage ? "Generate  ✦" : "Send  ↗";
        _sendButton.Enabled = _chatPanel.Enabled;
    }

    private void ComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (!_chatPanel.Enabled || _modelSelector.SelectedItem is not ModelOption option)
        {
            return;
        }

        var prompt = _composer.Text.Trim();
        if (prompt.Length == 0)
        {
            return;
        }

        _composer.Clear();
        AddTextBubble("YOU", prompt, true);
        _sendButton.Enabled = false;
        _composer.Enabled = false;
        _statusBar.Text = option.Model.IsImage
            ? $"Generating with {option.Model.Id}…"
            : $"Asking {option.Model.Id}…";

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            if (option.Model.IsImage)
            {
                var bytes = await _client.GenerateImageAsync(prompt, option.Model.Id, operation.Token);
                AddImageBubble(option.Model.Id, bytes);
                _statusBar.Text = $"Image generated by {option.Model.Id}.";
            }
            else
            {
                _history.Add(new GptMessage("user", prompt));
                var answer = await _client.SendAsync(_history, option.Model.Id, operation.Token);
                _history.Add(new GptMessage("assistant", answer));
                AddTextBubble($"CHATGPT · {option.Model.Id}", answer, false);
                _statusBar.Text = $"Answered by {option.Model.Id}.";
            }
        }
        catch (OperationCanceledException)
        {
            _statusBar.Text = "Request cancelled.";
        }
        catch (Exception ex)
        {
            AddTextBubble("ERROR", ToUiError(ex), false);
            _statusBar.Text = "Request failed. The local session was not shown or logged.";
        }
        finally
        {
            _composer.Enabled = true;
            _sendButton.Enabled = true;
            _composer.Focus();
        }
    }

    private void AddTextBubble(string heading, string text, bool user)
    {
        var bubble = new CardPanel
        {
            Width = BubbleWidth(),
            Height = 82,
            BackColor = user ? Color.FromArgb(255, 248, 239) : Card,
            BorderColor = user ? Color.FromArgb(231, 199, 163) : Line,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        var label = new Label
        {
            AutoSize = true,
            Text = heading,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = user ? Color.FromArgb(159, 90, 18) : Teal,
            Location = new Point(14, 9),
        };
        var body = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Ink,
            Location = new Point(14, 31),
            MaximumSize = new Size(Math.Max(250, bubble.Width - 30), 0),
        };
        bubble.Controls.AddRange([label, body]);
        body.MaximumSize = new Size(Math.Max(250, bubble.Width - 30), 0);
        body.PerformLayout();
        bubble.Height = Math.Max(74, body.Bottom + 18);
        _messages.Controls.Add(bubble);
        _messages.ScrollControlIntoView(bubble);
    }

    private void AddImageBubble(string model, byte[] bytes)
    {
        var bubble = new CardPanel
        {
            Width = BubbleWidth(),
            Height = 290,
            BackColor = Card,
            BorderColor = Line,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        var label = new Label
        {
            AutoSize = true,
            Text = $"CHATGPT · {model}",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Teal,
            Location = new Point(14, 9),
        };
        var image = new PictureBox
        {
            Location = new Point(14, 33),
            Size = new Size(Math.Min(420, bubble.Width - 30), 238),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(247, 248, 248),
        };
        using var stream = new MemoryStream(bytes);
        using var source = new Bitmap(stream);
        image.Image = new Bitmap(source);
        bubble.Controls.AddRange([label, image]);
        _messages.Controls.Add(bubble);
        _messages.ScrollControlIntoView(bubble);
    }

    private int BubbleWidth() => Math.Max(420, _messages.ClientSize.Width - 34);

    private void ResizeBubbles()
    {
        var width = BubbleWidth();
        foreach (Control control in _messages.Controls)
        {
            control.Width = width;
        }
    }

    private static string ToUiError(Exception exception)
    {
        if (exception is GptLocalClientException)
        {
            return exception.Message;
        }

        if (exception is HttpRequestException)
        {
            return "Could not reach the local ChatGPT proxy. Check Node.js and try again.";
        }

        return "Something went wrong. No credentials were changed.";
    }

    private sealed record ModelOption(GptModel Model)
    {
        public override string ToString() => Model.IsImage ? $"{Model.Id}  ·  image" : Model.Id;
    }

    private sealed class CardPanel : Panel
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Line;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rectangle = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using var path = RoundedRectangle(rectangle, 10);
            using var pen = new Pen(BorderColor, 1f);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class MarkPanel : Panel
    {
        public MarkPanel()
        {
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var center = new PointF(24, 24);
            using var background = new SolidBrush(Color.FromArgb(224, 242, 235));
            e.Graphics.FillEllipse(background, 2, 2, 44, 44);
            using var pen = new Pen(Teal, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(center.X, center.Y);
            for (var index = 0; index < 3; index++)
            {
                e.Graphics.DrawEllipse(pen, -8, -8, 16, 16);
                e.Graphics.RotateTransform(60);
            }
            e.Graphics.Restore(state);
            using var dot = new SolidBrush(Teal);
            e.Graphics.FillEllipse(dot, 21, 21, 6, 6);
            base.OnPaint(e);
        }
    }
}
