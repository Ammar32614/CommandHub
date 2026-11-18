using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.AddOns;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.PhoneDrop;

public sealed class PhoneDropAddOn : IAddOn
{
    private DropService? _service;

    public void Start(IAddOnHost host)
    {
        _service = new DropService(host);
        _service.Load();
        // Once you've turned it on, it's ready whenever Command hub is running.
        if (_service.Settings.Enabled) _service.Start();
        _service.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Say so when a file arrives, wherever you are in Command hub.
            var (_, incoming, history) = _service.Snapshot();
            if (incoming.Count == 0 && history.Count > 0 && history[0].At > DateTime.Now.AddSeconds(-2) && history[0].Path != _lastAnnounced)
            {
                _lastAnnounced = history[0].Path;
                if ((Application.Current.MainWindow as MainWindow)?.FindName("PageHost") is ContentControl { Content: not PhoneDropPage })
                    host.Notify($"Phone drop: {history[0].Name} arrived ({DropLogic.Size(history[0].Size)}).");
            }
        });
    }

    private string _lastAnnounced = "";

    public UIElement CreatePage() => new PhoneDropPage(_service!);
}

/// <summary>The Phone drop tab: the QR code to pair, what's arriving, what's received, and files to send to the phone.</summary>
public sealed class PhoneDropPage : UserControl
{
    private readonly DropService _svc;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dirty;

    public PhoneDropPage(DropService svc)
    {
        _svc = svc;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid { AllowDrop = true, Background = Brushes.Transparent };
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        // Drop files anywhere on the page to share them with the phone.
        layers.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        layers.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _svc.AddOutgoing(ExpandFolders(paths));
        };
        // Progress arrives on the server's threads: redraw at most a few times a second.
        _timer.Tick += (_, _) =>
        {
            if (!_dirty) return;
            _dirty = false;
            // Only progress moved: update the bars in place, so buttons stay put and clickable during a transfer.
            if (Shape() == _shape) UpdateProgress(); else Render();
        };
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; _svc.Changed += OnChanged; _timer.Start(); Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _svc.Changed -= OnChanged; _timer.Stop(); };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => _dirty = true;
    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    private static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (File.Exists(p)) yield return p;
            else if (Directory.Exists(p))
                foreach (var f in Directory.EnumerateFiles(p).Take(200)) yield return f;
        }
    }

    private string _shape = "";
    private readonly Dictionary<Transfer, (TextBlock Text, Grid Bar)> _progress = [];
    private List<(string Name, System.Net.IPAddress Address)> _addresses = [];
    private DateTime _addressesAt;
    private BitmapSource? _qr;
    private string _qrUrl = "";

    /// <summary>What the page shows apart from progress; a change means a full redraw.</summary>
    private string Shape()
    {
        var (outgoing, incoming, history) = _svc.Snapshot();
        return string.Join("|", _svc.Running, _svc.PinLocked, _svc.Pin, _svc.Settings.Key, _svc.Settings.Folder, _svc.Error,
            string.Join(",", incoming.Select(t => t.GetHashCode())), history.Count, history.FirstOrDefault()?.Path, string.Join(",", outgoing.Select(o => o.Id)));
    }

    private void UpdateProgress()
    {
        foreach (var (t, (text, bar)) in _progress)
        {
            var done = Interlocked.Read(ref t.Done);
            text.Text = ProgressText(t, done);
            double f = t.Size > 0 ? Math.Clamp((double)done / t.Size, 0, 1) : 0;
            bar.ColumnDefinitions[0].Width = new GridLength(Math.Max(0.0001, f), GridUnitType.Star);
            bar.ColumnDefinitions[1].Width = new GridLength(Math.Max(0.0001, 1 - f), GridUnitType.Star);
        }
    }

    private static string ProgressText(Transfer t, long done)
    {
        var secs = (DateTime.UtcNow - t.Started).TotalSeconds;
        return t.Size > 0 ? $"{100.0 * done / t.Size:0}%  ·  {DropLogic.Size(done)} of {DropLogic.Size(t.Size)}" + (secs > 1 ? $"  ·  {DropLogic.Size((long)(done / secs))}/s" : "") : "";
    }

    private void Render()
    {
        _shape = Shape();
        _progress.Clear();
        _content.Children.Clear();
        var (outgoing, incoming, history) = _svc.Snapshot();
        // Looking up network adapters is slow-ish: once every 15 seconds is plenty.
        if (DateTime.UtcNow - _addressesAt > TimeSpan.FromSeconds(15)) { _addresses = DropLogic.Addresses(); _addressesAt = DateTime.UtcNow; }
        var addresses = _addresses;

        var top = new DockPanel { Height = 40 };
        var crumb = T($"PHONE DROP   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        var toggle = Styled(_svc.Running ? "Turn off" : "Turn on", _svc.Running ? "GhostButton" : "AccentButton", (_, _) =>
        {
            if (_svc.Running) { _svc.Stop(); _svc.Settings.Enabled = false; }
            else if (_svc.Start()) _svc.Settings.Enabled = true;
            _svc.Save();
            Render();
        }, new Thickness(16, 6, 16, 6));
        System.Windows.Automation.AutomationProperties.SetName(toggle, _svc.Running ? "Turn Phone drop off" : "Turn Phone drop on");
        actions.Children.Add(toggle);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label("PHONE  ↔  PC OVER WI-FI"));
        var big = Big(!_svc.Running ? "Phone drop is off" : incoming.Count > 0 ? $"Receiving {incoming.Count} {(incoming.Count == 1 ? "file" : "files")}…" : "Ready for your phone", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        left.Children.Add(T(!_svc.Running
            ? "Turn it on to let your phone send files here. Windows may ask once whether Command hub can use your network: allow it on private networks."
            : addresses.Count == 0 ? "This PC doesn't seem to be on a network right now." : $"Saving to {_svc.Settings.Folder}", 13, _svc.Error.Length > 0 ? "Danger" : "Muted", wrap: true));
        if (_svc.Error.Length > 0) left.Children.Add(T(_svc.Error, 13, "Danger", wrap: true));
        left.MaxWidth = 760;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(left);
        _content.Children.Add(head);

        var columns = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(PairCard(addresses));
        var right = new StackPanel();
        right.Children.Add(IncomingCard(incoming, history));
        right.Children.Add(OutgoingCard(outgoing));
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        _content.Children.Add(columns);
    }

    // ---------------- Pairing ----------------

    private FrameworkElement PairCard(List<(string Name, System.Net.IPAddress Address)> addresses)
    {
        var s = new StackPanel();
        s.Children.Add(Label("CONNECT YOUR PHONE"));
        if (!_svc.Running || addresses.Count == 0)
        {
            s.Children.Add(Gap(T(_svc.Running ? "Connect this PC to Wi-Fi (or Ethernet on the same network as your phone)." : "Turn Phone drop on to show the code.", 13, "Muted", wrap: true), 10, 0));
            return Card(s, new Thickness(18, 16, 18, 18));
        }
        var ip = addresses[0].Address;
        var url = $"http://{ip}:{_svc.Port}/?k={_svc.Settings.Key}";
        s.Children.Add(Gap(T("Scan with your phone's camera, then add the page to your home screen.", 12, "Muted", wrap: true), 6, 12));
        if (_qrUrl != url) { _qr = QrImage(url); _qrUrl = url; }
        var qr = new Image { Source = _qr, Width = 230, Height = 230, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
        var frame = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = qr, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(frame);

        s.Children.Add(Gap(T("Or open this in your phone's browser", 12, "Muted"), 16, 4));
        var addr = T($"{ip}:{_svc.Port}", 22, "Text", FontWeights.SemiBold);
        addr.TextTrimming = TextTrimming.None;
        s.Children.Add(addr);
        s.Children.Add(Gap(T("and type this code", 12, "Muted"), 6, 2));
        var pin = _svc.PinLocked
            ? T("Paused after too many wrong tries. Scan the QR code, or press New pairing code.", 12, "Danger", wrap: true)
            : T(string.Join(" ", _svc.Pin.ToCharArray()), 28, "AccentBrush", FontWeights.SemiBold);
        pin.TextTrimming = TextTrimming.None;
        s.Children.Add(pin);
        if (addresses.Count > 1)
            s.Children.Add(Gap(T("Other addresses: " + string.Join(", ", addresses.Skip(1).Select(a => $"{a.Address} ({a.Name})")), 11, "Muted", wrap: true), 8, 0));
        s.Children.Add(Gap(T("Your phone has to be on the same Wi-Fi. Files never go through the internet.", 11, "Muted", wrap: true), 12, 0));
        var reset = Styled("New pairing code", "GhostButton", (_, _) => { _svc.NewPairing(); Render(); }, new Thickness(12, 4, 12, 4));
        reset.ToolTip = "Phones paired before will need to scan or type the new code";
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 12, 0, 0);
        s.Children.Add(reset);
        return Card(s, new Thickness(18, 16, 18, 18));
    }

    /// <summary>The QR code as a crisp black-on-white bitmap with the standard 4-module quiet zone.</summary>
    private static BitmapSource QrImage(string text)
    {
        var m = Qr.Encode(text);
        int n = m.GetLength(0), quiet = 4, scale = 6, size = (n + quiet * 2) * scale;
        var pixels = new byte[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (!m[y, x]) continue;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        pixels[((y + quiet) * scale + dy) * size + (x + quiet) * scale + dx] = 0;
            }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size);
        bmp.Freeze();
        return bmp;
    }

    // ---------------- Received ----------------

    private FrameworkElement IncomingCard(List<Transfer> incoming, List<Received> history)
    {
        var s = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var open = Styled("Open folder", "GhostButton", (_, _) => OpenFolder(_svc.Settings.Folder), new Thickness(12, 4, 12, 4));
        var change = Styled("Change folder", "GhostButton", (_, _) => ChangeFolder(), new Thickness(12, 4, 12, 4));
        change.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(open);
        buttons.Children.Add(change);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(Label("FROM YOUR PHONE"));
        s.Children.Add(head);

        foreach (var t in incoming)
        {
            var done = Interlocked.Read(ref t.Done);
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var line = new DockPanel();
            var pct = T(ProgressText(t, done), 11, "Muted");
            DockPanel.SetDock(pct, Dock.Right);
            line.Children.Add(pct);
            line.Children.Add(T(t.Name, 13, "Text", FontWeights.SemiBold));
            row.Children.Add(line);
            var bar = Bar(t.Size > 0 ? (double)done / t.Size : 0);
            row.Children.Add(Gap(bar, 6, 0));
            _progress[t] = (pct, bar);
            s.Children.Add(row);
        }
        if (history.Count == 0 && incoming.Count == 0)
            s.Children.Add(T("Nothing received yet. Files you send from your phone land here and in the folder.", 12, "Muted", wrap: true));
        foreach (var h in history.Take(25))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            bool exists = File.Exists(h.Path);
            var show = Styled("Show", "GhostButton", (_, _) => ShowInFolder(h.Path), new Thickness(10, 2, 10, 2));
            show.IsEnabled = exists;
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            var meta = T($"{DropLogic.Size(h.Size)}  ·  {Ago(h.At)}", 11, "Muted");
            meta.Margin = new Thickness(0, 0, 12, 0);
            meta.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(meta, Dock.Right);
            row.Children.Add(meta);
            var name = Bare(T(h.Name, 13, exists ? "Text" : "Muted"), $"Open {h.Name}", exists ? "Open" : "This file was moved or deleted");
            name.HorizontalAlignment = HorizontalAlignment.Left;
            name.IsEnabled = exists;
            name.Click += (_, _) => OpenFile(h.Path);
            row.Children.Add(name);
            s.Children.Add(row);
        }
        if (history.Count > 0)
        {
            var clear = Bare(T("Clear this list (files stay in the folder)", 11, "Muted"), "Clear received list");
            clear.HorizontalAlignment = HorizontalAlignment.Left;
            clear.Margin = new Thickness(0, 6, 0, 0);
            clear.Click += (_, _) => _svc.ClearHistory();
            s.Children.Add(clear);
        }
        var card = Card(s, new Thickness(18, 16, 18, 16));
        card.Margin = new Thickness(0, 0, 0, 14);
        return card;
    }

    private static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : at.Date == DateTime.Today ? at.ToString("h:mm tt") : at.ToString("d MMM, h:mm tt");
    }

    // ---------------- To the phone ----------------

    private FrameworkElement OutgoingCard(List<Outgoing> outgoing)
    {
        var s = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var choose = Styled("Choose files", "GhostButton", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Files to send to your phone" };
            if (dlg.ShowDialog() == true) _svc.AddOutgoing(dlg.FileNames);
        }, new Thickness(12, 4, 12, 4));
        DockPanel.SetDock(choose, Dock.Right);
        head.Children.Add(choose);
        head.Children.Add(Label("TO YOUR PHONE"));
        s.Children.Add(head);
        if (outgoing.Count == 0)
        {
            var zone = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 2, 0, 0) };
            zone.BorderBrush = Backdrop.Wash("TextColor", 0x40);
            var z = new StackPanel();
            var g = Glyph("", 22, "AccentBrush");
            g.HorizontalAlignment = HorizontalAlignment.Center;
            z.Children.Add(g);
            var t = T("Drop files anywhere on this page. They show up on your phone's Phone drop page to download.", 12, "Muted", wrap: true);
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 8, 0, 0);
            z.Children.Add(t);
            zone.Child = z;
            s.Children.Add(zone);
        }
        foreach (var o in outgoing)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var x = Bare(Glyph("", 9, "Muted"), $"Stop sharing {o.Name}");
            x.Click += (_, _) => _svc.RemoveOutgoing(o.Id);
            DockPanel.SetDock(x, Dock.Right);
            row.Children.Add(x);
            var size = T(DropLogic.Size(o.Size), 11, "Muted");
            size.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(T(o.Name, 13));
            s.Children.Add(row);
        }
        if (outgoing.Count > 0) s.Children.Add(Gap(T("Shared until you close Command hub or remove them.", 11, "Muted"), 6, 0));
        return Card(s, new Thickness(18, 16, 18, 16));
    }

    // ---------------- Helpers ----------------

    private void ChangeFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Where should files from your phone go?", InitialDirectory = Directory.Exists(_svc.Settings.Folder) ? _svc.Settings.Folder : "" };
        if (dlg.ShowDialog() != true) return;
        _svc.Settings.Folder = dlg.FolderName;
        _svc.Save();
        Render();
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Main?.ShowNotice("Couldn't open the folder: " + ex.Message); }
    }

    private static void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { Main?.ShowNotice("Couldn't show the file: " + ex.Message); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Main?.ShowNotice("Couldn't open it: " + ex.Message); }
    }

    private static FrameworkElement Gap(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    private static Grid Bar(double fraction)
    {
        var g = new Grid { Height = 5 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(3) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        g.Children.Add(fill);
        return g;
    }
}
