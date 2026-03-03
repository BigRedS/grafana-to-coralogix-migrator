using Sharprompt;

namespace GrafanaToCx.Cli.Cli;

public static class MultiSelectWithFallback
{
    public static IReadOnlyList<T> SelectRequired<T>(
        string message,
        IReadOnlyList<T> items,
        Func<T, string> textSelector,
        Func<string, IReadOnlyList<T>, Func<T, string>, IReadOnlyList<T>>? multiSelect = null,
        Func<string?>? readLine = null,
        Action<string>? writeLine = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(textSelector);

        if (items.Count == 0)
            return [];

        multiSelect ??= PromptWithSharprompt;
        readLine ??= Console.ReadLine;
        writeLine ??= Console.WriteLine;

        try
        {
            var selected = multiSelect(message, items, textSelector);
            if (selected.Count > 0)
                return selected;

            writeLine("No folders selected.");
            return [];
        }
        catch (Exception)
        {
            writeLine("Interactive multi-select is unavailable in this terminal. Falling back to numeric selection.");
            return SelectUsingNumericFallback(items, textSelector, readLine, writeLine);
        }
    }

    public static bool TryParseNumericSelection(
        string? input,
        int itemCount,
        out IReadOnlyList<int> selectedIndices,
        out string validationError)
    {
        selectedIndices = [];

        if (itemCount <= 0)
        {
            validationError = "No selectable items are available.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            validationError = "Select at least one folder.";
            return false;
        }

        var unique = new HashSet<int>();
        var ordered = new List<int>();
        var tokens = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            validationError = "Select at least one folder.";
            return false;
        }

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var index))
            {
                validationError = $"'{token}' is not a valid number.";
                return false;
            }

            if (index < 1 || index > itemCount)
            {
                validationError = $"'{index}' is out of range. Enter values between 1 and {itemCount}.";
                return false;
            }

            if (unique.Add(index))
                ordered.Add(index);
        }

        if (ordered.Count == 0)
        {
            validationError = "Select at least one folder.";
            return false;
        }

        selectedIndices = ordered;
        validationError = string.Empty;
        return true;
    }

    private static IReadOnlyList<T> PromptWithSharprompt<T>(
        string message,
        IReadOnlyList<T> items,
        Func<T, string> textSelector)
        where T : notnull
    {
        return Prompt.MultiSelect(message, items, textSelector: textSelector).ToList();
    }

    private static IReadOnlyList<T> SelectUsingNumericFallback<T>(
        IReadOnlyList<T> items,
        Func<T, string> textSelector,
        Func<string?> readLine,
        Action<string> writeLine)
        where T : notnull
    {
        writeLine("Select one or more folders by number (comma-separated):");
        for (var i = 0; i < items.Count; i++)
            writeLine($"  {i + 1}. {textSelector(items[i])}");

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            writeLine("Enter selection (example: 1,3):");
            var input = readLine();

            if (TryParseNumericSelection(input, items.Count, out var selectedIndices, out var validationError))
                return selectedIndices.Select(index => items[index - 1]).ToList();

            writeLine(validationError);
        }

        writeLine("No valid folder selection provided.");
        return [];
    }

}
