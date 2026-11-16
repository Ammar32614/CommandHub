using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Grades;

/// <summary>The Grades tab: a card per course with your standing next to the class's, semester GPA and CGPA.</summary>
public sealed class GradesPage : UserControl
{
    private readonly Book _book;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };

    public GradesPage(Book book)
    {
        _book = book;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; _book.Changed += OnChanged; Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _book.Changed -= OnChanged; };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);
    private GradesData D => _book.Data;
    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    internal static string Num(decimal v) => v.ToString(v % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);
    internal static decimal? ParseNum(string text) =>
        decimal.TryParse((text ?? "").Trim().TrimEnd('%'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

    private void Render()
    {
        _content.Children.Clear();
        var semester = _book.Current;
        var top = new DockPanel { Height = 40 };
        var crumb = T($"GRADES   /   {(semester?.Name ?? "").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var cgpa = GradeLogic.Cgpa(D.Semesters, D.Scale);
        var gpa = semester is null ? null : GradeLogic.Gpa(semester.Courses, D.Scale);
        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Styled("Grading scale", "GhostButton", (_, _) => new ScaleSheet(_book).Show(), new Thickness(14, 5, 14, 5)));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label("SEMESTER GPA AND CGPA"));
        var big = Big(gpa is { } g ? $"GPA {g:0.00}" : "No grades yet", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var credits = semester?.Courses.Sum(c => c.Credits) ?? 0;
        var parts = new List<string>();
        if (cgpa is { } cg) parts.Add($"CGPA {cg:0.00}");
        parts.Add($"{semester?.Courses.Count ?? 0} courses, {Num(credits)} credit hours this semester");
        parts.Add("projected from marks so far, until you enter final grades");
        left.Children.Add(T(string.Join("   ·   ", parts), 13, "Muted"));
        head.Children.Add(left);
        _content.Children.Add(head);

        // Semesters
        var tabs = new WrapPanel { Margin = new Thickness(0, 22, 0, 18) };
        foreach (var s in D.Semesters)
        {
            bool on = s == semester;
            var chip = new Border { Padding = new Thickness(12, 5, 12, 6), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8), Child = T(s.Name, 12, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null) };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "Hairline");
            var b = Bare(chip, $"Semester {s.Name}");
            var captured = s;
            b.Click += (_, _) => { D.CurrentSemesterId = captured.Id; _book.Save(); };
            b.MouseRightButtonUp += (_, _) => new SemesterSheet(_book, captured).Show();
            b.ToolTip = "Right-click to rename or delete";
            tabs.Children.Add(b);
        }
        var add = Styled("+ Semester", "GhostButton", (_, _) => new SemesterSheet(_book, null).Show(), new Thickness(12, 4, 12, 4));
        add.Margin = new Thickness(0, 0, 8, 8);
        tabs.Children.Add(add);
        if (semester is not null)
        {
            var edit = Styled("Rename", "GhostButton", (_, _) => new SemesterSheet(_book, semester).Show(), new Thickness(12, 4, 12, 4));
            edit.Margin = new Thickness(0, 0, 8, 8);
            tabs.Children.Add(edit);
        }
        _content.Children.Add(tabs);
        if (semester is null) return;

        var grid = new WrapPanel();
        foreach (var c in semester.Courses) grid.Children.Add(CourseCard(semester, c));
        var newCard = new StackPanel { Width = 290, Height = 190 };
        newCard.Children.Add(Glyph("", 22, "AccentBrush"));
        newCard.Children.Add(Top(T("Add a course", 14, "Text", FontWeights.SemiBold), 10));
        newCard.Children.Add(Top(T("It starts with Quizzes 10%, Assignments 15%, Mid 25% and Final 50%. Change them to match your course outline.", 11, "Muted", wrap: true), 4));
        var nb = Bare(Card(newCard, new Thickness(18, 16, 18, 16)), "Add a course");
        nb.Margin = new Thickness(0, 0, 14, 14);
        nb.VerticalAlignment = VerticalAlignment.Top;
        nb.Click += (_, _) =>
        {
            var course = GradeLogic.NewCourse($"Course {semester.Courses.Count + 1}");
            semester.Courses.Add(course);
            _book.Save();
            new CourseSheet(_book, semester, course).Show(focusName: true);
        };
        grid.Children.Add(nb);
        _content.Children.Add(grid);
    }

    private static FrameworkElement Top(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    private Button CourseCard(Semester semester, Course c)
    {
        var s = GradeLogic.Standing(c);
        var grade = GradeLogic.CourseGrade(c, D.Scale);
        var st = new StackPanel { Width = 290 };
        var head = new DockPanel();
        if (grade is not null)
        {
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5), VerticalAlignment = VerticalAlignment.Top };
            badge.SetResourceReference(Border.BackgroundProperty, c.FinalGrade.Trim().Length > 0 ? "Accent2Brush" : "AccentBrush");
            badge.Child = new TextBlock { Text = grade.Letter, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
            badge.ToolTip = c.FinalGrade.Trim().Length > 0 ? "Final grade" : "Projected from your marks so far";
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }
        var name = new StackPanel();
        var title = T(c.Name, 15, "Text", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        name.Children.Add(title);
        name.Children.Add(T(string.Join(" · ", new[] { c.Code, $"{Num(c.Credits)} credit hours" }.Where(x => x.Length > 0)), 11, "Muted"));
        head.Children.Add(name);
        st.Children.Add(head);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 2) };
        line.Children.Add(Big(s.Percent is { } p ? $"{Num(p)}%" : "–", 34));
        if (s.ClassPercent is { } cls)
        {
            var diff = (s.Percent ?? 0) - cls;
            var cmp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 6) };
            cmp.Children.Add(T($"class {Num(cls)}%", 12, "Muted"));
            if (s.Percent is not null) cmp.Children.Add(T((diff >= 0 ? "+" : "") + Num(Math.Round(diff, 1)) + " vs class", 12, diff >= 0 ? "Accent2Brush" : "Danger", FontWeights.SemiBold));
            line.Children.Add(cmp);
        }
        st.Children.Add(line);
        st.Children.Add(T(s.WeightGraded == 0 ? "Nothing marked yet" : $"{Num(s.WeightGraded)}% of the course marked · {Num(s.Earned)} of {Num(s.WeightTotal)} so far", 11, "Muted"));
        st.Children.Add(Top(Bar(s.WeightTotal > 0 ? (double)(s.WeightGraded / s.WeightTotal) : 0), 6));
        if (c.FinalGrade.Trim().Length == 0 && s.Remaining > 0 && s.WeightGraded > 0)
        {
            var next = GradeLogic.Needed(c, D.Scale).FirstOrDefault(n => n.Needed is not null);
            if (next.Band is not null)
                st.Children.Add(Top(T(next.Needed == 0 ? $"{next.Band.Letter} is already safe" : $"For {next.Band.Letter}: {Num(next.Needed!.Value)}% on the rest", 12, "AccentBrush"), 8));
        }
        var card = Card(st, new Thickness(18, 16, 18, 16));
        var b = Bare(card, $"Open {c.Name}");
        b.Margin = new Thickness(0, 0, 14, 14);
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Click += (_, _) => new CourseSheet(_book, semester, c).Show();
        return b;
    }

    internal static Grid Bar(double fraction)
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

    /// <summary>Shows content in a centred card over the window; Escape or a click outside closes it.</summary>
    internal static void ShowCard(UIElement content, double width)
    {
        var card = new Border
        {
            MaxWidth = width, Margin = new Thickness(24), MaxHeight = 820, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 22), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        var scrim = new Grid();
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => Main?.HideSheet();
        scrim.KeyDown += (_, e) => { if (e.Key == Key.Escape) Main?.HideSheet(); };
        scrim.Children.Add(card);
        Main?.ShowSheet(scrim);
    }
}

/// <summary>Rename, add or delete a semester.</summary>
internal sealed class SemesterSheet(Book book, Semester? semester)
{
    public void Show()
    {
        var s = new StackPanel();
        s.Children.Add(Label(semester is null ? "NEW SEMESTER" : "SEMESTER"));
        var name = Field("e.g. Spring 2027", semester?.Name ?? "", "Semester name");
        name.Margin = new Thickness(0, 12, 0, 0);
        s.Children.Add(name);
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        if (semester is not null && book.Data.Semesters.Count > 1)
        {
            bool armed = false;
            Button? del = null;
            del = Styled("Delete semester", "GhostButton", (_, _) =>
            {
                if (!armed) { armed = true; del!.Content = $"Delete {semester.Courses.Count} courses too? Click again"; return; }
                book.Data.Semesters.Remove(semester);
                book.Data.CurrentSemesterId = book.Data.Semesters[^1].Id;
                book.Save();
                (Application.Current.MainWindow as MainWindow)?.HideSheet();
            }, new Thickness(12, 5, 12, 5));
            DockPanel.SetDock(del, Dock.Left);
            row.Children.Add(del);
        }
        void Save()
        {
            var text = name.Text.Trim();
            if (text.Length == 0) { name.Focus(); return; }
            if (semester is null)
            {
                var n = new Semester { Name = text };
                book.Data.Semesters.Add(n);
                book.Data.CurrentSemesterId = n.Id;
            }
            else semester.Name = text;
            book.Save();
            (Application.Current.MainWindow as MainWindow)?.HideSheet();
        }
        var save = Styled("Save", "AccentButton", (_, _) => Save(), new Thickness(18, 6, 18, 6));
        save.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(save);
        s.Children.Add(row);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        GradesPage.ShowCard(s, 420);
        name.Dispatcher.BeginInvoke(() => name.Focus(), DispatcherPriority.Input);
    }
}

/// <summary>Edit the grading scale: letter, minimum percent and grade points.</summary>
internal sealed class ScaleSheet(Book book)
{
    public void Show()
    {
        var s = new StackPanel();
        s.Children.Add(Label("GRADING SCALE"));
        s.Children.Add(T("Set this to your university's scale. A percentage earns the first letter whose minimum it reaches.", 12, "Muted", wrap: true));
        var rows = new List<(TextBox L, TextBox M, TextBox P)>();
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        void AddRow(GradeBand b)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var w in new[] { 90.0, 10, 120, 10, 120 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            var l = Field("Letter", b.Letter, "Letter");
            var m = Field("Min %", GradesPage.Num(b.Min), $"Minimum for {b.Letter}");
            var p = Field("Points", b.Points.ToString("0.00", CultureInfo.InvariantCulture), $"Points for {b.Letter}");
            Grid.SetColumn(m, 2);
            Grid.SetColumn(p, 4);
            g.Children.Add(l); g.Children.Add(m); g.Children.Add(p);
            list.Children.Add(g);
            rows.Add((l, m, p));
        }
        var head = new Grid();
        foreach (var w in new[] { 90.0, 10, 120, 10, 120 }) head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        head.Children.Add(Label("LETTER", "Muted"));
        var hm = Label("FROM %", "Muted"); Grid.SetColumn(hm, 2); head.Children.Add(hm);
        var hp = Label("POINTS", "Muted"); Grid.SetColumn(hp, 4); head.Children.Add(hp);
        s.Children.Add(head);
        foreach (var b in book.Data.Scale.OrderByDescending(b => b.Min)) AddRow(b);
        s.Children.Add(list);
        var error = T("", 12, "Danger");
        s.Children.Add(error);
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var reset = Styled("Use the default", "GhostButton", (_, _) =>
        {
            book.Data.Scale = GradeLogic.DefaultScale();
            book.Save();
            (Application.Current.MainWindow as MainWindow)?.HideSheet();
        }, new Thickness(12, 5, 12, 5));
        DockPanel.SetDock(reset, Dock.Left);
        buttons.Children.Add(reset);
        var save = Styled("Save", "AccentButton", (_, _) =>
        {
            var bands = new List<GradeBand>();
            foreach (var (l, m, p) in rows)
            {
                if (l.Text.Trim().Length == 0) continue;
                if (GradesPage.ParseNum(m.Text) is not { } min || GradesPage.ParseNum(p.Text) is not { } pts || min > 100)
                {
                    error.Text = $"Check the numbers for {l.Text.Trim()}.";
                    return;
                }
                bands.Add(new GradeBand { Letter = l.Text.Trim(), Min = min, Points = pts });
            }
            if (bands.Count == 0) { error.Text = "Keep at least one grade."; return; }
            if (bands.All(b => b.Min > 0)) bands.Add(new GradeBand { Letter = "F", Min = 0, Points = 0 });
            book.Data.Scale = bands.OrderByDescending(b => b.Min).ToList();
            book.Save();
            (Application.Current.MainWindow as MainWindow)?.HideSheet();
        }, new Thickness(18, 6, 18, 6));
        System.Windows.Automation.AutomationProperties.SetName(save, "Save scale");
        save.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Children.Add(save);
        s.Children.Add(buttons);
        GradesPage.ShowCard(s, 420);
    }
}
