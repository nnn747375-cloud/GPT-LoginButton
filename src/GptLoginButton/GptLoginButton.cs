using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace GptLoginButton;

public enum GptLoginState
{
    SignedOut,
    SigningIn,
    Connected,
    Error,
}

/// <summary>
/// A precise, accessible WinForms button for starting a host-owned ChatGPT
/// authorization flow. The button raises events; the host owns credentials.
/// </summary>
[DefaultEvent(nameof(LoginRequested))]
public sealed class GptLoginButton : Control
{
    private GptLoginState _state = GptLoginState.SignedOut;
    private string? _accountLabel;
    private string? _errorMessage;
    private bool _hovered;
    private bool _keyboardActivation;

    public GptLoginButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Continue with ChatGPT. Opens secure sign-in in your browser.";
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        MinimumSize = new Size(220, 48);
        Size = new Size(280, 54);
        TabStop = true;
    }

    public event EventHandler? LoginRequested;
    public event EventHandler? LogoutRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GptLoginState State => _state;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? AccountLabel => _accountLabel;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? ErrorMessage => _errorMessage;

    public void SetSigningIn()
        => SetState(GptLoginState.SigningIn, null);

    public void SetConnected(string? accountLabel = null)
        => SetState(GptLoginState.Connected, accountLabel);

    public void SetSignedOut()
        => SetState(GptLoginState.SignedOut, null);

    public void SetError(string? message = null)
        => SetState(GptLoginState.Error, message);

    public void PerformLogin()
    {
        if (_state != GptLoginState.SigningIn)
        {
            LoginRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PerformLogout()
    {
        if (_state == GptLoginState.Connected)
        {
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_state == GptLoginState.Connected)
        {
            PerformLogout();
        }
        else
        {
            PerformLogin();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyCode is Keys.Enter or Keys.Space) && !_keyboardActivation)
        {
            _keyboardActivation = true;
            PerformClickFromKeyboard();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            _keyboardActivation = false;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        var radius = Math.Min(14, Math.Max(6, bounds.Height / 3));
        using var path = RoundedRectangle(bounds, radius);

        var palette = PaletteForState();
        using var fill = new SolidBrush(palette.Fill);
        using var border = new Pen(palette.Border, Focused ? 2f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        DrawMark(e.Graphics, new Rectangle(bounds.X + 14, bounds.Y + 13, 26, 26), palette.Text);

        using var textBrush = new SolidBrush(palette.Text);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
        };
        var textBounds = new Rectangle(bounds.X + 52, bounds.Y, Math.Max(0, bounds.Width - 64), bounds.Height);
        e.Graphics.DrawString(CurrentText(), Font, textBrush, textBounds, format);

        if (Focused)
        {
            var focusBounds = Rectangle.Inflate(bounds, -5, -5);
            using var focusPath = RoundedRectangle(focusBounds, Math.Max(4, radius - 3));
            using var focusPen = new Pen(Color.FromArgb(140, palette.Focus), 1f) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private void SetState(GptLoginState state, string? accountLabel)
    {
        if (IsHandleCreated && InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => SetState(state, accountLabel)));
            }
            catch (InvalidOperationException)
            {
                // The host is closing; no UI update is needed.
            }

            return;
        }

        _state = state;
        _accountLabel = state == GptLoginState.Connected ? accountLabel : null;
        _errorMessage = state == GptLoginState.Error ? accountLabel : null;
        Enabled = state != GptLoginState.SigningIn;
        AccessibleName = state switch
        {
            GptLoginState.SigningIn => "ChatGPT sign-in is in progress. Return here after completing the browser login.",
            GptLoginState.Connected => "ChatGPT connected. Activate to disconnect the local proxy.",
            GptLoginState.Error => "ChatGPT sign-in failed. Activate to try again.",
            _ => "Continue with ChatGPT. Opens secure sign-in in your browser.",
        };
        Cursor = state == GptLoginState.SigningIn ? Cursors.WaitCursor : Cursors.Hand;
        Invalidate();
    }

    private string CurrentText()
        => _state switch
        {
            GptLoginState.SigningIn => "Waiting for ChatGPT…",
            GptLoginState.Connected => "ChatGPT connected",
            GptLoginState.Error => "Try ChatGPT login again",
            _ => "Continue with ChatGPT",
        };

    private void PerformClickFromKeyboard()
    {
        if (Enabled)
        {
            OnClick(EventArgs.Empty);
        }
    }

    private (Color Fill, Color Border, Color Text, Color Focus) PaletteForState()
    {
        if (!Enabled)
        {
            return (Color.FromArgb(228, 232, 230), Color.FromArgb(166, 178, 173), Color.FromArgb(100, 113, 108), Color.FromArgb(23, 107, 91));
        }

        if (_state == GptLoginState.Connected)
        {
            return (_hovered ? Color.FromArgb(218, 242, 232) : Color.FromArgb(232, 248, 241), Color.FromArgb(23, 107, 91), Color.FromArgb(18, 79, 68), Color.FromArgb(23, 107, 91));
        }

        if (_state == GptLoginState.Error)
        {
            return (Color.FromArgb(255, 239, 240), Color.FromArgb(174, 55, 65), Color.FromArgb(117, 37, 45), Color.FromArgb(174, 55, 65));
        }

        return (_hovered ? Color.FromArgb(220, 244, 235) : Color.FromArgb(243, 250, 247), Color.FromArgb(23, 107, 91), Color.FromArgb(20, 78, 67), Color.FromArgb(23, 107, 91));
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

    private static void DrawMark(Graphics graphics, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var orbit = Math.Max(5f, bounds.Width * 0.24f);
        var state = graphics.Save();
        graphics.TranslateTransform(center.X, center.Y);
        graphics.DrawEllipse(pen, -orbit, -orbit, orbit * 2, orbit * 2);
        graphics.RotateTransform(60f);
        graphics.DrawEllipse(pen, -orbit, -orbit, orbit * 2, orbit * 2);
        graphics.RotateTransform(60f);
        graphics.DrawEllipse(pen, -orbit, -orbit, orbit * 2, orbit * 2);
        graphics.Restore(state);
        using var dot = new SolidBrush(color);
        graphics.FillEllipse(dot, center.X - 2f, center.Y - 2f, 4f, 4f);
    }
}
