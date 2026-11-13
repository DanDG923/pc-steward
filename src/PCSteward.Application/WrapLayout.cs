namespace PCSteward.Application;

/// <summary>
/// AC-UX-03 (UI review 2026-11-13, fault 2): the line breaking behind the Desktop's wrapping action rows, kept here so it can be
/// tested without a window. A row is measured once and then placed from that same measurement: placing never decides the line
/// breaks again. Deciding twice is what stacked "Check for updates" under "Review updates" on a 150% display, where sizes are
/// fractions and the width the row was handed back differed from the sum of its parts by a rounding error. Presentation only.
/// </summary>
public static class WrapLayout
{
    /// <summary>
    /// How far a line may run past the available width before its last item wraps. Sizes arrive as single-precision values on a
    /// fractional pixel grid, so a row that exactly fits can add up to a hair more than the width it was given.
    /// </summary>
    public const double Tolerance = 0.5;

    /// <summary>The natural size of one visible item.</summary>
    public readonly record struct Item(double Width, double Height);

    /// <summary>Where one item goes.</summary>
    public readonly record struct Slot(double X, double Y, double Width, double Height);

    /// <summary>The measured row: its size, and the line each item is on (the same order as the items).</summary>
    public sealed record Measured(double Width, double Height, IReadOnlyList<int> Lines);

    /// <summary>Breaks <paramref name="items"/> into lines no wider than <paramref name="availableWidth"/> (which may be infinite).</summary>
    public static Measured Measure(IReadOnlyList<Item> items, double availableWidth, double horizontalSpacing, double verticalSpacing)
    {
        ArgumentNullException.ThrowIfNull(items);
        int[] lines = new int[items.Count];
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        int line = 0;
        for (int index = 0; index < items.Count; index++)
        {
            Item item = items[index];
            if (lineWidth > 0 && lineWidth + horizontalSpacing + item.Width > availableWidth + Tolerance)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + verticalSpacing;
                lineWidth = item.Width;
                lineHeight = item.Height;
                line++;
            }
            else
            {
                lineWidth = lineWidth > 0 ? lineWidth + horizontalSpacing + item.Width : item.Width;
                lineHeight = Math.Max(lineHeight, item.Height);
            }
            lines[index] = line;
        }
        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return new Measured(double.IsInfinity(availableWidth) ? width : Math.Min(width, availableWidth), height, lines);
    }

    /// <summary>
    /// Places <paramref name="items"/> on the lines <see cref="Measure"/> chose. <paramref name="finalWidth"/> only caps an item
    /// that is wider than the whole row; it never moves an item to another line.
    /// </summary>
    public static IReadOnlyList<Slot> Arrange(IReadOnlyList<Item> items, IReadOnlyList<int> lines, double finalWidth, double horizontalSpacing, double verticalSpacing)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count != items.Count) throw new ArgumentException("Each item needs the line it was measured on.", nameof(lines));
        Slot[] slots = new Slot[items.Count];
        double x = 0, y = 0, lineHeight = 0;
        int line = 0;
        for (int index = 0; index < items.Count; index++)
        {
            Item item = items[index];
            if (lines[index] != line)
            {
                line = lines[index];
                x = 0;
                y += lineHeight + verticalSpacing;
                lineHeight = 0;
            }
            double itemWidth = Math.Min(item.Width, finalWidth);
            slots[index] = new Slot(x, y, itemWidth, item.Height);
            x += itemWidth + horizontalSpacing;
            lineHeight = Math.Max(lineHeight, item.Height);
        }
        return slots;
    }
}
