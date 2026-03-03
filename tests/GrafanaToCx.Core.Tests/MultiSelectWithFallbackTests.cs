using GrafanaToCx.Cli.Cli;

namespace GrafanaToCx.Core.Tests;

public class MultiSelectWithFallbackTests
{
    [Fact]
    public void TryParseNumericSelection_EmptyInput_FailsWithMinimumSelectionMessage()
    {
        var parsed = MultiSelectWithFallback.TryParseNumericSelection(
            input: "   ",
            itemCount: 4,
            out var selectedIndices,
            out var validationError);

        Assert.False(parsed);
        Assert.Empty(selectedIndices);
        Assert.Equal("Select at least one folder.", validationError);
    }

    [Fact]
    public void TryParseNumericSelection_ValidInput_DedupesAndPreservesOrder()
    {
        var parsed = MultiSelectWithFallback.TryParseNumericSelection(
            input: "3,1,3,2",
            itemCount: 5,
            out var selectedIndices,
            out var validationError);

        Assert.True(parsed);
        Assert.Equal(string.Empty, validationError);
        Assert.Equal([3, 1, 2], selectedIndices);
    }

    [Fact]
    public void SelectRequired_WhenSharpromptThrows_FallsBackToNumericSelection()
    {
        var items = new[] { "Folder A", "Folder B", "Folder C" };
        var inputs = new Queue<string?>(["x", "2,1"]);
        var logs = new List<string>();

        var selected = MultiSelectWithFallback.SelectRequired(
            "Select folders",
            items,
            x => x,
            multiSelect: (_, _, _) => throw new ArgumentOutOfRangeException("top", "cursor issue"),
            readLine: () => inputs.Dequeue(),
            writeLine: logs.Add);

        Assert.Equal(["Folder B", "Folder A"], selected);
        Assert.Contains(logs, line => line.Contains("Falling back to numeric selection.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, line => line.Contains("not a valid number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectRequired_WhenFallbackInputRemainsInvalid_ReturnsEmptyWithoutThrowing()
    {
        var items = new[] { "Folder A", "Folder B" };
        var inputs = new Queue<string?>(["", "0", "3"]);

        var selected = MultiSelectWithFallback.SelectRequired(
            "Select folders",
            items,
            x => x,
            multiSelect: (_, _, _) => throw new IOException("rendering failed"),
            readLine: () => inputs.Dequeue(),
            writeLine: _ => { });

        Assert.Empty(selected);
    }
}
