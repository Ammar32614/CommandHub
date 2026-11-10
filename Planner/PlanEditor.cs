using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommandHub.Home;
using CommandHub.Study;
using static CommandHub.Home.PageUi;

namespace CommandHub.Planner;

/// <summary>
/// Add or edit a planner item, shown as a sheet over the window. Dates and times can be typed naturally
/// ("fri", "20 nov", "2pm") or picked on the mini calendar. Raises <see cref="Closed"/> when done.
/// </summary>
public sealed class PlanEditor : UserControl
{
    private readonly PlanItem? _editing;
    private readonly IReadOnlyList<StudyCourse> _courses;
    private readonly TextBox _title, _date, _start, _end, _course, _until, _notes;
    private readonly TextBlock _dateHint = T("", 11, "Muted"), _error = T("", 12, "Danger");
    private readonly UniformGrid _calendarDays = new() { Columns = 7 };
    private readonly TextBlock _calendarTitle = T("", 12, "Text", FontWeights.SemiBold);
    private readonly CheckBox _weekly, _done;
    private readonly StackPanel _untilRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel _courseChips = new();
    private PlanKind _kind;
    private DateOnly _calendarMonth;
    private bool _confirmDelete;

    /// <summary>Raised after save, delete or cancel. True when the planner changed.</summary>
    public event Action<bool>? Closed;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public PlanEditor(PlanItem? editing, DateOnly suggestedDate, IReadOnlyList<StudyCourse> courses)
    {
        _editing = editing;
        _courses = courses;
        var date = editing?.Date ?? suggestedDate;
        _kind = editing?.Kind ?? PlanKind.Deadline;
        _calendarMonth = new DateOnly(date.Year, date.Month, 1);

        _title = Field("ML assignment 3", editing?.Title ?? "", "Title");
        _date = Field("fri, 20 nov, 2026-11-20", date.ToString("yyyy-MM-dd"), "Date");
        _start = Field("2pm", editing?.Start is { } s ? PlannerLogic.FormatTime(s) : "", "Start time");
        _end = Field("3:30pm", editing?.End is { } e ? PlannerLogic.FormatTime(e) : "", "End time");
        _course = Field("Course (optional)", editing?.Course ?? "", "Course");
        _until = Field("Until (optional)", editing?.Until is { } u ? u.ToString("yyyy-MM-dd") : "", "Repeat until");
        _notes = Field("Notes", editing?.Notes ?? "", "Notes");
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Height = 58;
        _notes.VerticalContentAlignment = VerticalAlignment.Top;
        _weekly = new CheckBox { IsChecked = editing?.Weekly ?? false };
        _weekly.SetResourceReference(StyleProperty, "Switch");
        System.Windows.Automation.AutomationProperties.SetName(_weekly, "Repeats every week");
        _done = new CheckBox { IsChecked = editing?.Done ?? false };
        _done.SetResourceReference(StyleProperty, "Switch");
        System.Windows.Automation.AutomationProperties.SetName(_done, "Done");

        Content = Build();
        _date.TextChanged += (_, _) => { UpdateDateHint(); };
        _course.TextChanged += (_, _) => BuildCourseChips();
        _weekly.Checked += (_, _) => _untilRow.Visibility = Visibility.Visible;
        _weekly.Unchecked += (_, _) => _untilRow.Visibility = Visibility.Collapsed;
        _untilRow.Visibility = _weekly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateDateHint();
        BuildCourseChips();
        BuildCalendar();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Closed?.Invoke(false); }
            else if (e.Key == Key.Enter && !_notes.IsKeyboardFocusWithin) { e.Handled = true; Save(); }
        };
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { _title.Focus(); _title.SelectAll(); }, DispatcherPriority.Input);
    }

    // ---------------- Layout ----------------

    private UIElement Build()
    {
        var form = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var close = PageUi.Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Closed?.Invoke(false);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(Label(_editing is null ? "ADD TO PLANNER" : "EDIT"));
        form.Children.Add(head);

        form.Children.Add(_title);

        // Kind
        var kinds = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var k in new[] { PlanKind.Class, PlanKind.Deadline, PlanKind.Exam, PlanKind.Event })
        {
            var rb = new RadioButton { Content = k.ToString(), GroupName = "PlanKind" + GetHashCode(), IsChecked = k == _kind };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) => _kind = k;
            kinds.Children.Add(rb);
        }
        var kindBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Child = kinds, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x20), Margin = new Thickness(0, 10, 0, 0) };
        kindBox.SetResourceReference(Border.BackgroundProperty, "Bg");
        form.Children.Add(kindBox);

        // Date + calendar side by side
        var dateGrid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition());
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
        var left = new StackPanel();
        left.Children.Add(Small("DATE"));
        left.Children.Add(_date);
        _dateHint.Margin = new Thickness(2, 5, 0, 0);
        left.Children.Add(_dateHint);
        var times = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        times.ColumnDefinitions.Add(new ColumnDefinition());
        var startBox = new StackPanel();
        startBox.Children.Add(Small("START"));
        startBox.Children.Add(_start);
        var endBox = new StackPanel();
        endBox.Children.Add(Small("END"));
        endBox.Children.Add(_end);
        Grid.SetColumn(endBox, 2);
        times.Children.Add(startBox);
        times.Children.Add(endBox);
        left.Children.Add(times);
        var repeat = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_weekly, Dock.Right);
        repeat.Children.Add(_weekly);
        repeat.Children.Add(T("Repeats every week", 12));
        left.Children.Add(repeat);
        _until.Width = 170;
        _untilRow.Children.Add(_until);
        left.Children.Add(_untilRow);
        var doneRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_done, Dock.Right);
        doneRow.Children.Add(_done);
        doneRow.Children.Add(T("Done", 12));
        left.Children.Add(doneRow);
        dateGrid.Children.Add(left);
        var cal = Calendar();
        Grid.SetColumn(cal, 2);
        dateGrid.Children.Add(cal);
        form.Children.Add(dateGrid);

        // Course
        form.Children.Add(Spaced(Small("COURSE"), 14));
        form.Children.Add(_course);
        _courseChips.Margin = new Thickness(0, 6, 0, 0);
        form.Children.Add(_courseChips);

        form.Children.Add(Spaced(Small("NOTES"), 10));
        form.Children.Add(_notes);

        _error.Margin = new Thickness(0, 10, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;
        form.Children.Add(_error);

        // Buttons
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Styled("Cancel", "GhostButton", (_, _) => Closed?.Invoke(false)));
        var save = Styled(_editing is null ? "Add" : "Save", "AccentButton", (_, _) => Save(), new Thickness(18, 6, 18, 6));
        save.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(save, "Save to planner");
        right.Children.Add(save);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        if (_editing is not null)
        {
            Button? delete = null;
            delete = Styled("Delete", "GhostButton", (_, _) =>
            {
                if (_confirmDelete) { App.Data.Planner.Remove(_editing); App.SaveNow(); Closed?.Invoke(true); return; }
                _confirmDelete = true;
                delete!.Content = "Delete? Click again";
                delete.SetResourceReference(ForegroundProperty, "Danger");
            });
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Children.Add(delete);
        }
        form.Children.Add(buttons);

        var card = new Border
        {
            Width = 560, CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 18, 22, 20), Child = form,
            BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x24),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;

        var scrim = new Grid();
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => Closed?.Invoke(false);
        scrim.Children.Add(new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(20) });
        return scrim;
    }

    private static TextBlock Small(string text)
    {
        var t = Label(text, "Muted");
        t.Margin = new Thickness(2, 0, 0, 5);
        return t;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }

    // ---------------- Date ----------------

    private void UpdateDateHint()
    {
        if (PlannerLogic.ParseDate(_date.Text, Today) is { } d)
        {
            _dateHint.Text = $"{d:dddd, d MMMM yyyy}  ·  {PlannerLogic.When(d, Today)}";
            _dateHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            if (d.Year != _calendarMonth.Year || d.Month != _calendarMonth.Month) _calendarMonth = new DateOnly(d.Year, d.Month, 1);
            BuildCalendar();
        }
        else
        {
            _dateHint.Text = _date.Text.Trim().Length == 0 ? "Pick a day on the calendar" : "Try \"fri\", \"20 nov\" or 2026-11-20";
            _dateHint.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        }
    }

    private UIElement Calendar()
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var prev = PageUi.Bare(Glyph("", 10, "Muted"), "Previous month");
        prev.Padding = new Thickness(6);
        prev.Click += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(-1); BuildCalendar(); };
        var next = PageUi.Bare(Glyph("", 10, "Muted"), "Next month");
        next.Click += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(1); BuildCalendar(); };
        DockPanel.SetDock(prev, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        head.Children.Add(prev);
        head.Children.Add(next);
        _calendarTitle.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(_calendarTitle);

        var names = new UniformGrid { Columns = 7 };
        foreach (var n in new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" })
        {
            var t = T(n, 10, "Muted");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            names.Children.Add(t);
        }
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(names);
        stack.Children.Add(_calendarDays);
        var box = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(10), Child = stack, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x18) };
        box.SetResourceReference(Border.BackgroundProperty, "Bg");
        return box;
    }

    private void BuildCalendar()
    {
        _calendarTitle.Text = _calendarMonth.ToString("MMMM yyyy");
        _calendarDays.Children.Clear();
        var selected = PlannerLogic.ParseDate(_date.Text, Today);
        foreach (var day in PlannerLogic.MonthGrid(_calendarMonth.Year, _calendarMonth.Month))
        {
            bool inMonth = day.Month == _calendarMonth.Month, isSel = day == selected, isToday = day == Today;
            var text = T(day.Day.ToString(), 11, isSel ? "OnAccent" : inMonth ? "Text" : "Muted");
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var cell = new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(6), Child = text, Margin = new Thickness(0, 1, 0, 1) };
            if (isSel) cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else if (isToday) { cell.BorderThickness = new Thickness(1); cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
            var b = PageUi.Bare(cell, day.ToString("dddd d MMMM"));
            b.Click += (_, _) => _date.Text = day.ToString("yyyy-MM-dd");
            _calendarDays.Children.Add(b);
        }
    }

    // ---------------- Course ----------------

    private void BuildCourseChips()
    {
        _courseChips.Children.Clear();
        foreach (var c in _courses)
        {
            bool on = string.Equals(_course.Text.Trim(), c.Name, StringComparison.OrdinalIgnoreCase);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = ColorBrush(c.Color), VerticalAlignment = VerticalAlignment.Center });
            var name = T(c.Name, 11, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null);
            name.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(name);
            var chip = new Border { Child = row, Padding = new Thickness(9, 4, 10, 4), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "Hairline");
            var b = PageUi.Bare(chip, $"Course {c.Name}");
            b.Click += (_, _) => _course.Text = on ? "" : c.Name;
            _courseChips.Children.Add(b);
        }
    }

    // ---------------- Save ----------------

    private void Save()
    {
        var date = PlannerLogic.ParseDate(_date.Text, Today);
        if (!PlannerLogic.TryParseOptionalTime(_start.Text, out var start)) { Fail("Start time: try \"2pm\" or 14:00.", _start); return; }
        if (!PlannerLogic.TryParseOptionalTime(_end.Text, out var end)) { Fail("End time: try \"3:30pm\" or 15:30.", _end); return; }
        if (PlannerLogic.Validate(_title.Text, date, start, end) is { } problem)
        {
            Fail(problem, problem.StartsWith("Give") ? _title : problem.StartsWith("Pick") ? _date : _end);
            return;
        }
        DateOnly? until = null;
        if (_weekly.IsChecked == true && _until.Text.Trim().Length > 0)
        {
            until = PlannerLogic.ParseDate(_until.Text, Today);
            if (until is null) { Fail("\"Until\" isn't a date I understand.", _until); return; }
            if (until < date) { Fail("\"Until\" should be after the first date.", _until); return; }
        }

        var item = _editing ?? new PlanItem();
        item.Title = _title.Text.Trim();
        item.Kind = _kind;
        item.Date = date!.Value;
        item.Start = start;
        item.End = start is null ? null : end;
        item.Course = _course.Text.Trim();
        item.Notes = _notes.Text.Trim();
        item.Weekly = _weekly.IsChecked == true;
        item.Until = item.Weekly ? until : null;
        item.Done = _done.IsChecked == true;
        if (_editing is null) App.Data.Planner.Add(item);
        App.SaveNow();
        Closed?.Invoke(true);
    }

    private void Fail(string message, UIElement focus)
    {
        _error.Text = message;
        focus.Focus();
    }
}
