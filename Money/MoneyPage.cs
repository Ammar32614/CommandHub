using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.Money;

/// <summary>
/// The Money tab: log spending in seconds, a monthly budget with per-category limits, and where it all goes.
/// </summary>
public sealed class MoneyPage : UserControl
{
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private MoneyKind _kind = MoneyKind.Expense;
    private bool _showAll;
    private string _category = MoneyData.ExpenseCategories[0];

    // Quick-add fields survive re-renders so typing is never lost.
    private readonly TextBox _amount = Field("Amount", "", "Amount");
    private readonly TextBox _note = Field("What for? (optional)", "", "Note");
    private readonly TextBox _date = Field("today", "", "Date");
    private readonly TextBlock _error = T("", 12, "Danger");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static MoneyData Data => App.Data.Money;

    public MoneyPage()
    {
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;

        foreach (var box in new[] { _amount, _note, _date })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        _amount.TextChanged += (_, _) => _error.Text = "";

        Loaded += (_, _) => { App.ThemeChanged += OnTheme; Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => App.ThemeChanged -= OnTheme;
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }

    private static readonly string[] Palette = ["#F5B14C", "#4FD1C5", "#FF7A6B", "#9B8CFF", "#6CB6FF", "#7BD88F", "#F38BC4", "#C8CDD8"];

    /// <summary>A stable colour per category (the built-in ones in order, others by name).</summary>
    public static Brush CategoryBrush(string category)
    {
        int i = Array.FindIndex(MoneyData.ExpenseCategories, c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(category)) % Palette.Length;
        return ColorBrush(Palette[i % Palette.Length]);
    }

    private string Money(decimal amount) => MoneyLogic.Format(amount, Data.Currency);

    // ---------------- Page ----------------

    private void Render()
    {
        _content.Children.Clear();
        var month = MoneyLogic.Month(Data.Transactions, _month.Year, _month.Month);

        var top = new DockPanel { Height = 40 };
        var crumb = T($"MONEY   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        _content.Children.Add(Headline(month));
        _content.Children.Add(QuickAdd());

        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.Children.Add(BudgetCard(month));
        var cats = CategoryCard(month);
        Grid.SetColumn(cats, 2);
        row.Children.Add(cats);
        _content.Children.Add(row);

        _content.Children.Add(DailyCard());

        var row2 = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row2.ColumnDefinitions.Add(new ColumnDefinition());
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        var insights = (FrameworkElement)InsightsCard();
        insights.VerticalAlignment = VerticalAlignment.Top;
        row2.Children.Add(insights);
        var recent = RecentCard(month);
        Grid.SetColumn(recent, 2);
        row2.Children.Add(recent);
        _content.Children.Add(row2);
    }

    private UIElement Headline(MonthSummary month)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        Button Nav(string glyph, string name, int step)
        {
            var b = new Button { Content = glyph, Width = 34, Height = 34, ToolTip = name };
            b.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { _month = _month.AddMonths(step); Render(); };
            return b;
        }
        controls.Children.Add(Nav("", "Previous month", -1));
        var thisMonth = Styled("This month", "GhostButton", (_, _) => { _month = new DateOnly(Today.Year, Today.Month, 1); Render(); }, new Thickness(12, 5, 12, 5));
        thisMonth.Margin = new Thickness(4, 0, 4, 0);
        controls.Children.Add(thisMonth);
        controls.Children.Add(Nav("", "Next month", 1));
        var budgets = Styled("Budgets", "GhostButton", (_, _) => OpenBudgets(), new Thickness(14, 5, 14, 5));
        budgets.Margin = new Thickness(12, 0, 0, 0);
        controls.Children.Add(budgets);
        DockPanel.SetDock(controls, Dock.Right);
        dock.Children.Add(controls);

        var left = new StackPanel();
        left.Children.Add(Label($"SPENT IN {_month.ToString("MMMM yyyy").ToUpperInvariant()}"));
        var big = Big(Money(month.Spent), 52);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var parts = new List<string>();
        bool current = _month.Year == Today.Year && _month.Month == Today.Month;
        if (Data.MonthlyBudget > 0)
        {
            var b = MoneyLogic.Budget(Data.MonthlyBudget, month.Spent, current ? Today : new DateOnly(_month.Year, _month.Month, DateTime.DaysInMonth(_month.Year, _month.Month)));
            parts.Add($"of {Money(Data.MonthlyBudget)} budget");
            parts.Add(b.Over ? $"{Money(-b.Left)} over" : $"{Money(b.Left)} left");
            if (current && !b.Over) parts.Add($"{Money(Math.Floor(b.PerDay))} a day for {b.DaysLeft} days");
        }
        else parts.Add("No budget set");
        if (month.Income > 0) parts.Add($"income {Money(month.Income)}");
        left.Children.Add(T(string.Join("   ·   ", parts), 13, Data.MonthlyBudget > 0 && month.Spent > Data.MonthlyBudget ? "Danger" : "Muted"));
        dock.Children.Add(left);
        return dock;
    }

    // ---------------- Quick add ----------------

    private UIElement QuickAdd()
    {
        var stack = new StackPanel();
        var line = new Grid();
        foreach (var w in new[] { 172.0, 10, 150, 10, -1, 10, 120, 10, 90 })
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        var seg = new UniformGrid { Columns = 2 };
        foreach (var (k, text) in new[] { (MoneyKind.Expense, "Spent"), (MoneyKind.Income, "Received") })
        {
            var rb = new RadioButton { Content = text, GroupName = "MoneyKind", IsChecked = k == _kind };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) =>
            {
                if (_kind == k) return;
                _kind = k;
                _category = (k == MoneyKind.Expense ? MoneyData.ExpenseCategories : MoneyData.IncomeCategories)[0];
                Render();
            };
            seg.Children.Add(rb);
        }
        var segBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Child = seg, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x20), VerticalAlignment = VerticalAlignment.Center };
        segBox.SetResourceReference(Border.BackgroundProperty, "Bg");
        line.Children.Add(segBox);
        Detach(_amount); Detach(_note); Detach(_date);
        Grid.SetColumn(_amount, 2); line.Children.Add(_amount);
        Grid.SetColumn(_note, 4); line.Children.Add(_note);
        Grid.SetColumn(_date, 6); line.Children.Add(_date);
        var add = Styled("Add", "AccentButton", (_, _) => Add(), new Thickness(0, 7, 0, 7));
        System.Windows.Automation.AutomationProperties.SetName(add, "Add transaction");
        Grid.SetColumn(add, 8);
        line.Children.Add(add);
        stack.Children.Add(line);

        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var cats = (_kind == MoneyKind.Expense ? MoneyData.ExpenseCategories : MoneyData.IncomeCategories).ToList();
        foreach (var used in Data.Transactions.Where(t => t.Kind == _kind).Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!cats.Contains(used, StringComparer.OrdinalIgnoreCase)) cats.Add(used);
        foreach (var c in cats)
        {
            bool on = c.Equals(_category, StringComparison.OrdinalIgnoreCase);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = _kind == MoneyKind.Expense ? CategoryBrush(c) : (Brush)FindResource("Accent2Brush"), VerticalAlignment = VerticalAlignment.Center });
            var name = T(c, 11, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null);
            name.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(name);
            var chip = new Border { Child = row, Padding = new Thickness(10, 4, 11, 4), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "Hairline");
            var b = Bare(chip, $"Category {c}");
            var captured = c;
            b.Click += (_, _) => { _category = captured; Render(); _amount.Focus(); };
            chips.Children.Add(b);
        }
        stack.Children.Add(chips);
        _error.Margin = new Thickness(2, 2, 0, 0);
        Detach(_error);
        stack.Children.Add(_error);

        var card = Card(stack, new Thickness(16, 14, 16, 10));
        card.Margin = new Thickness(0, 22, 0, 0);
        return card;
    }

    private static void Detach(FrameworkElement e) => (e.Parent as Panel)?.Children.Remove(e);

    private void Add()
    {
        if (MoneyLogic.ParseAmount(_amount.Text) is not { } amount) { _error.Text = "Type an amount, like 450, 1,200 or 1.5k."; _amount.Focus(); return; }
        var date = MoneyLogic.ParsePastDate(_date.Text, Today);
        if (date is null) { _error.Text = "Try \"yesterday\", \"mon\" or \"5 nov\" for the date (not a future one)."; _date.Focus(); return; }
        Data.Transactions.Add(new Transaction { Amount = amount, Kind = _kind, Category = _category, Note = _note.Text.Trim(), Date = date.Value });
        App.SaveNow();
        _amount.Clear();
        _note.Clear();
        _date.Clear();
        if (date.Value.Year != _month.Year || date.Value.Month != _month.Month) _month = new DateOnly(date.Value.Year, date.Value.Month, 1);
        Render();
        Dispatcher.BeginInvoke(() => _amount.Focus(), DispatcherPriority.Input);
    }

    // ---------------- Budget and categories ----------------

    private UIElement BudgetCard(MonthSummary month)
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("BUDGET"), 0, 12));
        if (Data.MonthlyBudget <= 0)
        {
            stack.Children.Add(T("Set a monthly budget to see what's left and how much you can spend each day.", 12, "Muted", wrap: true));
            var set = Styled("Set a budget", "GhostButton", (_, _) => OpenBudgets(), new Thickness(12, 5, 12, 5));
            set.HorizontalAlignment = HorizontalAlignment.Left;
            set.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(set);
            return Card(stack);
        }
        double used = (double)(month.Spent / Data.MonthlyBudget);
        var head = new DockPanel();
        var pct = T($"{Math.Round(used * 100)}% used", 12, used > 1 ? "Danger" : "Muted");
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(T($"{Money(month.Spent)} of {Money(Data.MonthlyBudget)}", 13));
        stack.Children.Add(head);
        stack.Children.Add(Spaced(Bar(Math.Min(1, used), used > 1 ? "Danger" : used > 0.85 ? "Accent3Brush" : "AccentBrush", 8), 8, 14));

        foreach (var (cat, limit) in Data.CategoryBudgets.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value))
        {
            var spent = month.Expenses.Where(t => t.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            double f = (double)(spent / limit);
            var r = new DockPanel();
            var v = T($"{Money(spent)} / {Money(limit)}", 11, f > 1 ? "Danger" : "Muted");
            DockPanel.SetDock(v, Dock.Right);
            r.Children.Add(v);
            r.Children.Add(T(cat, 12));
            stack.Children.Add(r);
            stack.Children.Add(Spaced(Bar(Math.Min(1, f), f > 1 ? "Danger" : null, 4, CategoryBrush(cat)), 5, 10));
        }
        return Card(stack);
    }

    private UIElement CategoryCard(MonthSummary month)
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("WHERE IT WENT"), 0, 12));
        var cats = MoneyLogic.ByCategory(month.Expenses);
        if (cats.Count == 0) stack.Children.Add(T("Nothing spent this month yet.", 12, "Muted"));
        foreach (var c in cats)
        {
            var head = new DockPanel();
            var right = T($"{Money(c.Amount)}   {Math.Round(c.Share * 100)}%", 12, "Muted");
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = CategoryBrush(c.Category), VerticalAlignment = VerticalAlignment.Center });
            var n = T($"{c.Category}", 13);
            n.Margin = new Thickness(8, 0, 0, 0);
            name.Children.Add(n);
            name.Children.Add(Spaced(T($"{c.Count} {(c.Count == 1 ? "payment" : "payments")}", 11, "Muted"), 8, 0, horizontal: true));
            head.Children.Add(name);
            stack.Children.Add(head);
            stack.Children.Add(Spaced(Bar(c.Share, null, 6, CategoryBrush(c.Category)), 6, 12));
        }
        return Card(stack);
    }

    private static Grid Bar(double fraction, string? brushKey, double height, Brush? brush = null)
    {
        var g = new Grid { Height = height };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2) };
        if (brushKey != null) fill.SetResourceReference(Border.BackgroundProperty, brushKey);
        else fill.Background = brush;
        g.Children.Add(fill);
        return g;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top, double bottom, bool horizontal = false)
    {
        e.Margin = horizontal ? new Thickness(top, 0, bottom, 0) : new Thickness(0, top, 0, bottom);
        return e;
    }

    // ---------------- Daily chart ----------------

    private UIElement DailyCard()
    {
        var days = MoneyLogic.DailySpend(Data.Transactions, _month.Year, _month.Month);
        decimal max = Math.Max(1, days.Max());
        const double h = 120;
        var grid = new Grid { Height = h + 20 };
        for (int i = 0; i < days.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = new DateOnly(_month.Year, _month.Month, i + 1);
            bool isToday = day == Today;
            var bar = new Border
            {
                Height = days[i] > 0 ? Math.Max(3, h * (double)(days[i] / max)) : 2, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 20), CornerRadius = new CornerRadius(3, 3, 1, 1), MaxWidth = 26,
                ToolTip = $"{day:ddd d MMM}: {Money(days[i])}", Opacity = day > Today ? 0.35 : 1,
            };
            bar.SetResourceReference(Border.BackgroundProperty, days[i] > 0 ? (isToday ? "AccentBrush" : "Accent2Brush") : "Raised");
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
            if (i == 0 || (i + 1) % 5 == 0 || isToday)
            {
                var lbl = T((i + 1).ToString(), 10, isToday ? "Text" : "Muted");
                lbl.HorizontalAlignment = HorizontalAlignment.Center;
                lbl.VerticalAlignment = VerticalAlignment.Bottom;
                lbl.TextTrimming = TextTrimming.None;
                Grid.SetColumn(lbl, i);
                grid.Children.Add(lbl);
            }
        }
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("EVERY DAY"), 0, 12));
        stack.Children.Add(grid);
        var card = Card(stack);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    // ---------------- Insights and recent ----------------

    private UIElement InsightsCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("INSIGHTS"), 0, 12));
        var lines = MoneyLogic.Insights(Data.Transactions, _month.Year, _month.Month, Data.Currency, Today);
        if (lines.Count == 0) stack.Children.Add(T("Log a few payments and patterns show up here.", 12, "Muted", wrap: true));
        foreach (var line in lines)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var dot = T("●", 8, "AccentBrush");
            dot.Margin = new Thickness(0, 4, 10, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(T(line, 13, wrap: true));
            stack.Children.Add(row);
        }
        return Card(stack);
    }

    private UIElement RecentCard(MonthSummary month)
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("THIS MONTH"), 0, 10));
        var list = month.Expenses.Concat(month.Incomes).OrderByDescending(t => t.Date).ThenByDescending(t => Data.Transactions.IndexOf(t)).ToList();
        if (list.Count == 0) stack.Children.Add(T("No payments logged this month.", 12, "Muted"));
        foreach (var t in list.Take(_showAll ? int.MaxValue : 10))
        {
            var row = new Grid { Background = Brushes.Transparent };
            foreach (var w in new[] { 76.0, -1, 110, 30 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            row.Children.Add(T(t.Date == Today ? "Today" : t.Date.ToString("ddd d"), 12, "Muted"));
            var what = new StackPanel { Orientation = Orientation.Horizontal };
            what.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = t.Kind == MoneyKind.Expense ? CategoryBrush(t.Category) : (Brush)FindResource("Accent2Brush"), VerticalAlignment = VerticalAlignment.Center });
            var label = T(t.Note.Length > 0 ? $"{t.Note}  ·  {t.Category}" : t.Category, 12);
            label.Margin = new Thickness(8, 0, 0, 0);
            what.Children.Add(label);
            Grid.SetColumn(what, 1);
            row.Children.Add(what);
            var amt = T((t.Kind == MoneyKind.Income ? "+ " : "") + Money(t.Amount), 12, t.Kind == MoneyKind.Income ? "Accent2Brush" : "Text", FontWeights.SemiBold);
            amt.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(amt, 2);
            row.Children.Add(amt);
            var del = new Button { Content = "", Width = 22, Height = 22, FontSize = 9, ToolTip = "Delete", Opacity = 0.0, HorizontalAlignment = HorizontalAlignment.Right };
            del.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(del, $"Delete {t.Category} {Money(t.Amount)} on {t.Date:d MMM}");
            var captured = t;
            del.Click += (_, _) => Delete(captured);
            Grid.SetColumn(del, 3);
            row.Children.Add(del);
            void Show() => del.Opacity = row.IsMouseOver || row.IsKeyboardFocusWithin ? 1 : 0;
            row.MouseEnter += (_, _) => Show();
            row.MouseLeave += (_, _) => Show();
            row.IsKeyboardFocusWithinChanged += (_, _) => Show();
            stack.Children.Add(new Border { Child = row, Padding = new Thickness(0, 7, 0, 7), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Backdrop.Wash("TextColor", 0x10) });
        }
        if (list.Count > 10)
        {
            var more = Styled(_showAll ? "Show fewer" : $"Show all {list.Count}", "GhostButton", (_, _) => { _showAll = !_showAll; Render(); }, new Thickness(12, 4, 12, 4));
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(more);
        }
        return Card(stack);
    }

    private void Delete(Transaction t)
    {
        int index = Data.Transactions.IndexOf(t);
        if (index < 0) return;
        Data.Transactions.RemoveAt(index);
        App.SaveNow();
        Render();
        (Application.Current.MainWindow as MainWindow)?.ShowToast("Payment deleted", "Undo", () =>
        {
            Data.Transactions.Insert(Math.Min(index, Data.Transactions.Count), t);
            App.SaveNow();
            if (IsLoaded) Render();
        }, TimeSpan.FromSeconds(6));
    }

    // ---------------- Budgets sheet ----------------

    private void OpenBudgets()
    {
        var form = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Main?.HideSheet();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(Label("BUDGETS"));
        form.Children.Add(head);

        var currency = Field("Rs", Data.Currency, "Currency symbol");
        var total = Field("Monthly budget, like 20000", Data.MonthlyBudget > 0 ? Data.MonthlyBudget.ToString("0.##") : "", "Monthly budget");
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.Children.Add(Labeled("CURRENCY", currency));
        var totalBox = Labeled("WHOLE MONTH", total);
        Grid.SetColumn(totalBox, 2);
        top.Children.Add(totalBox);
        form.Children.Add(top);

        form.Children.Add(Spaced(Label("PER CATEGORY (OPTIONAL)", "Muted"), 16, 8));
        var fields = new Dictionary<string, TextBox>();
        var grid = new UniformGrid { Columns = 2 };
        foreach (var c in MoneyData.ExpenseCategories)
        {
            var f = Field("No limit", Data.CategoryBudgets.TryGetValue(c, out var v) && v > 0 ? v.ToString("0.##") : "", $"{c} budget");
            fields[c] = f;
            var box = Labeled(c.ToUpperInvariant(), f);
            box.Margin = new Thickness(0, 0, 8, 8);
            grid.Children.Add(box);
        }
        form.Children.Add(grid);
        var error = T("", 12, "Danger");
        form.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Styled("Cancel", "GhostButton", (_, _) => Main?.HideSheet()));
        var save = Styled("Save", "AccentButton", (_, _) =>
        {
            decimal Read(TextBox b, string name, out bool ok)
            {
                ok = true;
                if (b.Text.Trim().Length == 0) return 0;
                if (MoneyLogic.ParseAmount(b.Text) is { } v) return v;
                ok = false;
                error.Text = $"{name}: type an amount like 20000 or 5k.";
                b.Focus();
                return 0;
            }
            var totalValue = Read(total, "Monthly budget", out var okTotal);
            if (!okTotal) return;
            var perCat = new Dictionary<string, decimal>();
            foreach (var (c, f) in fields)
            {
                var v = Read(f, c, out var ok);
                if (!ok) return;
                if (v > 0) perCat[c] = v;
            }
            Data.Currency = currency.Text.Trim().Length > 0 ? currency.Text.Trim() : MoneyData.DefaultCurrency();
            Data.MonthlyBudget = totalValue;
            Data.CategoryBudgets = perCat;
            App.SaveNow();
            Main?.HideSheet();
            Render();
        }, new Thickness(18, 6, 18, 6));
        save.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(save, "Save budgets");
        buttons.Children.Add(save);
        form.Children.Add(buttons);

        var card = new Border { Width = 460, CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 18, 22, 20), Child = form, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        var scrim = new Grid();
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => Main?.HideSheet();
        scrim.KeyDown += (_, e) => { if (e.Key == Key.Escape) Main?.HideSheet(); };
        scrim.Children.Add(card);
        Main?.ShowSheet(scrim);
        Dispatcher.BeginInvoke(() => total.Focus(), DispatcherPriority.Input);
    }

    private static StackPanel Labeled(string label, UIElement field)
    {
        var s = new StackPanel();
        var l = Label(label, "Muted");
        l.Margin = new Thickness(2, 0, 0, 5);
        s.Children.Add(l);
        s.Children.Add(field);
        return s;
    }

    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;
}
