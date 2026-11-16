using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Grades;

/// <summary>
/// A course: its details, each weighted part with its marks (yours beside the class average), and what you need
/// on the rest for each grade. Every box saves when you leave it.
/// </summary>
internal sealed class CourseSheet(Book book, Semester semester, Course course)
{
    private readonly StackPanel _root = new();
    private readonly StackPanel _summary = new();
    private const double SummaryWidth = 220;
    private TextBox? _focusNext;

    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;
    private IReadOnlyList<GradeBand> Scale => book.Data.Scale;

    public void Show(bool focusName = false)
    {
        Build();
        GradesPage.ShowCard(_root, 940);
        // Closing the sheet refreshes the course cards behind it.
        _root.Unloaded += (_, _) => book.Save();
        if (focusName && _root.Children.Count > 0 && FindName("Course name") is { } box)
            _root.Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    private TextBox? FindName(string automationName) =>
        Descendants(_root).OfType<TextBox>().FirstOrDefault(t => System.Windows.Automation.AutomationProperties.GetName(t) == automationName);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var x in Descendants(c)) yield return x;
        }
    }

    private void Quiet() => book.Save(notify: false);

    /// <summary>A text box that commits its value when you leave it or press Enter.</summary>
    private static TextBox Box(string placeholder, string text, string name, double width, Action<string> commit)
    {
        var b = Field(placeholder, text, name);
        b.Width = width;
        b.Padding = new Thickness(8, 5, 8, 5);
        b.FontSize = 12;
        string last = text;
        void Commit() { if (b.Text != last) { last = b.Text; commit(b.Text); } }
        b.LostFocus += (_, _) => Commit();
        b.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Commit();
            b.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        };
        return b;
    }

    private static string N(decimal? v) => v is { } x ? GradesPage.Num(x) : "";

    private void Build()
    {
        _root.Children.Clear();

        // Header: the course's details.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Main?.HideSheet();
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var fields = new WrapPanel();
        StackPanel Labeled(string label, UIElement field)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var l = Label(label, "Muted");
            l.Margin = new Thickness(2, 0, 0, 5);
            s.Children.Add(l);
            s.Children.Add(field);
            return s;
        }
        fields.Children.Add(Labeled("COURSE", Box("e.g. Physics I", course.Name, "Course name", 260, t => { course.Name = t.Trim().Length > 0 ? t.Trim() : course.Name; Quiet(); })));
        fields.Children.Add(Labeled("CODE", Box("PHY101", course.Code, "Course code", 110, t => { course.Code = t.Trim(); Quiet(); })));
        fields.Children.Add(Labeled("CREDITS", Box("3", N(course.Credits), "Credit hours", 70, t =>
        {
            if (GradesPage.ParseNum(t) is { } v && v <= 12) course.Credits = v;
            Quiet();
            Summary();
        })));
        fields.Children.Add(Labeled("FINAL GRADE", Box("When it's out", course.FinalGrade, "Final grade", 120, t =>
        {
            var g = t.Trim();
            course.FinalGrade = g.Length == 0 || GradeLogic.BandForLetter(g, Scale) is not null ? (GradeLogic.BandForLetter(g, Scale)?.Letter ?? "") : course.FinalGrade;
            Quiet();
            Summary();
        })));
        head.Children.Add(fields);
        _root.Children.Add(head);

        // Two columns: parts on the left, standing on the right.
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SummaryWidth) });
        var parts = new StackPanel();
        foreach (var p in course.Parts) parts.Children.Add(PartCard(p));
        var addPart = Styled("+ Add a part", "GhostButton", (_, _) =>
        {
            course.Parts.Add(new Part { Name = "Project", Weight = 0 });
            Quiet();
            Build();
        }, new Thickness(12, 5, 12, 5));
        addPart.HorizontalAlignment = HorizontalAlignment.Left;
        parts.Children.Add(addPart);
        columns.Children.Add(parts);
        (_summary.Parent as Panel)?.Children.Remove(_summary); // kept across rebuilds
        Grid.SetColumn(_summary, 2);
        columns.Children.Add(_summary);
        _root.Children.Add(columns);
        Summary();

        // Delete course (asks twice).
        bool armed = false;
        Button? del = null;
        del = Styled("Delete course", "GhostButton", (_, _) =>
        {
            if (!armed) { armed = true; del!.Content = "Delete it and all its marks? Click again"; return; }
            semester.Courses.Remove(course);
            book.Save();
            Main?.HideSheet();
        }, new Thickness(12, 5, 12, 5));
        del.HorizontalAlignment = HorizontalAlignment.Left;
        del.Margin = new Thickness(0, 18, 0, 0);
        _root.Children.Add(del);

        if (_focusNext is { } f) { _focusNext = null; _root.Dispatcher.BeginInvoke(() => f.Focus(), DispatcherPriority.Input); }
    }

    private static readonly double[] Columns = [124, 6, 56, 6, 56, 6, 50, 6, 64, 6, 54, 4, 24];

    private static Grid Row()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var w in Columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        return g;
    }

    private static void Put(Grid g, UIElement e, int column)
    {
        Grid.SetColumn(e, column);
        if (e is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(e);
    }

    private Border PartCard(Part part)
    {
        var s = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var remove = Bare(Glyph("", 12, "Muted"), $"Remove part {part.Name}", "Remove this part and its marks");
        remove.Click += (_, _) => { course.Parts.Remove(part); Quiet(); Build(); };
        DockPanel.SetDock(remove, Dock.Right);
        head.Children.Add(remove);
        var hrow = new StackPanel { Orientation = Orientation.Horizontal };
        hrow.Children.Add(Box("Part", part.Name, $"Part name {part.Name}", 130, t => { if (t.Trim().Length > 0) part.Name = t.Trim(); Quiet(); }));
        hrow.Children.Add(Box("%", N(part.Weight), $"Weight of {part.Name}", 60, t =>
        {
            if (GradesPage.ParseNum(t) is { } v && v <= 100) part.Weight = v;
            Quiet();
            Summary();
        }));
        hrow.Children.Add(Spaced(T("% of the course", 11, "Muted"), 6));
        hrow.Children.Add(Spaced(T("drop lowest", 11, "Muted"), 18));
        hrow.Children.Add(Spaced(Box("0", part.DropLowest.ToString(), $"Drop lowest in {part.Name}", 44, t =>
        {
            if (int.TryParse(t.Trim(), out var v) && v is >= 0 and <= 20) part.DropLowest = v;
            Quiet();
            Build();
        }), 6));
        head.Children.Add(hrow);
        s.Children.Add(head);

        // Column headings
        if (part.Marks.Count > 0)
        {
            var h = Row();
            string[] labels = ["", "", "YOURS", "", "OUT OF", "", "%", "", "CLASS", "", "VS CLASS"];
            for (int i = 0; i < labels.Length; i++) if (labels[i].Length > 0) Put(h, Label(labels[i], "Muted"), i);
            s.Children.Add(h);
        }
        var counted = GradeLogic.Counted(part).ToHashSet();
        foreach (var m in part.Marks) s.Children.Add(MarkRow(part, m, counted));

        var add = Styled($"+ {GradeLogic.NextMarkName(part)}", "GhostButton", (_, _) =>
        {
            var last = part.Marks.LastOrDefault();
            var mark = new Mark { Name = GradeLogic.NextMarkName(part), Total = last?.Total ?? (part.Weight >= 20 ? 50 : 10), Date = DateOnly.FromDateTime(DateTime.Today) };
            part.Marks.Add(mark);
            Quiet();
            Build();
            _focusNext = FindName($"Your marks for {mark.Name}");
            if (_focusNext is { } f) { _focusNext = null; f.Dispatcher.BeginInvoke(() => f.Focus(), DispatcherPriority.Input); }
        }, new Thickness(10, 3, 10, 3));
        System.Windows.Automation.AutomationProperties.SetName(add, $"Add a mark to {part.Name}");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(add);

        var card = Card(s, new Thickness(16, 14, 16, 12));
        card.Margin = new Thickness(0, 0, 0, 10);
        return card;
    }

    private Grid MarkRow(Part part, Mark m, HashSet<Mark> counted)
    {
        var r = Row();
        var pct = T("", 12, "Text", FontWeights.SemiBold);
        var diff = T("", 12, "Muted");
        void Compute()
        {
            var mine = GradeLogic.Percent(m.Obtained, m.Total);
            var cls = GradeLogic.Percent(m.ClassAverage, m.Total);
            pct.Text = mine is { } x ? $"{GradesPage.Num(x)}%" : "";
            if (mine is { } a && cls is { } b)
            {
                var d = Math.Round(a - b, 1);
                diff.Text = (d >= 0 ? "+" : "") + GradesPage.Num(d);
                diff.SetResourceReference(TextBlock.ForegroundProperty, d >= 0 ? "Accent2Brush" : "Danger");
            }
            else diff.Text = "";
            bool dropped = m.Obtained is not null && !GradeLogic.Counted(part).Contains(m);
            r.Opacity = dropped ? 0.5 : 1; // dropped as one of the lowest
            r.ToolTip = dropped ? "Dropped as one of the lowest" : null;
        }
        Put(r, Box("Name", m.Name, $"Name of {m.Name}", 124, t => { if (t.Trim().Length > 0) m.Name = t.Trim(); Quiet(); }), 0);
        Put(r, Box("–", N(m.Obtained), $"Your marks for {m.Name}", 56, t =>
        {
            m.Obtained = t.Trim().Length == 0 ? null : GradesPage.ParseNum(t) is { } v && v <= m.Total * 2 ? v : m.Obtained;
            Quiet(); Compute(); Summary();
        }), 2);
        Put(r, Box("10", N(m.Total), $"Total marks for {m.Name}", 56, t =>
        {
            if (GradesPage.ParseNum(t) is { } v && v > 0) m.Total = v;
            Quiet(); Compute(); Summary();
        }), 4);
        Put(r, pct, 6);
        Put(r, Box("–", N(m.ClassAverage), $"Class average for {m.Name}", 64, t =>
        {
            m.ClassAverage = t.Trim().Length == 0 ? null : GradesPage.ParseNum(t) is { } v && v <= m.Total ? v : m.ClassAverage;
            Quiet(); Compute(); Summary();
        }), 8);
        Put(r, diff, 10);
        var del = Bare(Glyph("", 9, "Muted"), $"Delete {m.Name}", "Delete this mark");
        del.Click += (_, _) => { part.Marks.Remove(m); Quiet(); Build(); };
        Put(r, del, 12);
        Compute();
        return r;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double left)
    {
        e.Margin = new Thickness(left, 0, 0, 0);
        e.VerticalAlignment = VerticalAlignment.Center;
        return e;
    }

    /// <summary>The right column: standing against the class, and what each grade needs.</summary>
    private void Summary()
    {
        _summary.Children.Clear();
        var s = GradeLogic.Standing(course);
        var box = new StackPanel();
        box.Children.Add(Label("SO FAR"));
        box.Children.Add(Big(s.Percent is { } p ? $"{GradesPage.Num(p)}%" : "–", 38));
        var grade = GradeLogic.CourseGrade(course, Scale);
        if (grade is not null)
            box.Children.Add(T((course.FinalGrade.Trim().Length > 0 ? "Final grade " : "On track for ") + $"{grade.Letter} ({grade.Points:0.00})", 13, "AccentBrush", FontWeights.SemiBold));
        if (s.ClassPercent is { } cls)
            box.Children.Add(T($"Class average {GradesPage.Num(cls)}%" + (s.Percent is { } mine ? $" · you're {(mine >= cls ? "+" : "")}{GradesPage.Num(Math.Round(mine - cls, 1))}" : ""), 12, "Muted", wrap: true));
        box.Children.Add(T(s.WeightGraded == 0 ? "Nothing marked yet." : $"{GradesPage.Num(s.WeightGraded)}% of the course marked. You have {GradesPage.Num(s.Earned)} of those {GradesPage.Num(s.WeightGraded)} marks.", 11, "Muted", wrap: true));
        if (s.WeightTotal != 100)
            box.Children.Add(T($"The parts add up to {GradesPage.Num(s.WeightTotal)}%, not 100%.", 11, "Danger", wrap: true));

        if (course.FinalGrade.Trim().Length == 0 && s.Remaining > 0)
        {
            var needed = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            needed.Children.Add(Label("NEEDED ON THE REST"));
            needed.Children.Add(T($"Average you need on the remaining {GradesPage.Num(s.Remaining)}%:", 11, "Muted", wrap: true));
            foreach (var (band, need) in GradeLogic.Needed(course, Scale).Take(8))
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                var v = T(need is null ? "out of reach" : need == 0 ? "safe" : $"{GradesPage.Num(need.Value)}%", 12, need is null ? "Muted" : need == 0 ? "Accent2Brush" : "Text");
                DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                row.Children.Add(T(band.Letter, 12, "Text", FontWeights.SemiBold));
                needed.Children.Add(row);
            }
            box.Children.Add(needed);
        }
        _summary.Children.Add(Card(box, new Thickness(16, 14, 16, 14)));
    }
}
