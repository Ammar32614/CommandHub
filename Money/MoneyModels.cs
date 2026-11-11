using System.Globalization;

namespace CommandHub.Money;

public enum MoneyKind { Expense, Income }

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    /// <summary>Always positive; <see cref="Kind"/> says which way it went.</summary>
    public decimal Amount { get; set; }
    public MoneyKind Kind { get; set; } = MoneyKind.Expense;
    public string Category { get; set; } = "Other";
    public string Note { get; set; } = "";
}

public class MoneyData
{
    public string Currency { get; set; } = DefaultCurrency();
    /// <summary>0 = no overall budget set.</summary>
    public decimal MonthlyBudget { get; set; }
    public Dictionary<string, decimal> CategoryBudgets { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];

    public static readonly string[] ExpenseCategories = ["Food", "Transport", "Shopping", "Bills", "Education", "Health", "Fun", "Other"];
    public static readonly string[] IncomeCategories = ["Allowance", "Salary", "Freelance", "Gift", "Other"];

    /// <summary>The Windows region's currency symbol (e.g. "Rs"), or "$" if Windows doesn't say.</summary>
    public static string DefaultCurrency()
    {
        try { return RegionInfo.CurrentRegion.CurrencySymbol is { Length: > 0 } s ? s : "$"; }
        catch (ArgumentException) { return "$"; }
    }
}
