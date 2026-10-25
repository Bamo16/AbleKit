namespace AbleKit.Analysis;

/// <summary>
/// A clip's color: a cell of Live's palette of 70, in 5 rows of 14, numbered row by row from 0 at the
/// top left. <see cref="Random"/>, index −1, is a clip not yet saved, which Live gives a random color.
/// </summary>
public readonly record struct ClipColor
{
    private const int Rows = 5;
    private const int Columns = 14;

    // Stored one up, so that default(ClipColor) is Random, as Clip.Color's default is.
    private readonly int _indexPlusOne;

    private ClipColor(int index) => _indexPlusOne = index + 1;

    /// <summary>Not yet saved: Live picks a color at random.</summary>
    public static ClipColor Random => default;

    /// <summary>The number Live stores: 0 to 69 for a palette cell, −1 for <see cref="Random"/>.</summary>
    public int Index => _indexPlusOne - 1;

    /// <summary>The palette cell at <paramref name="row"/> and <paramref name="column"/>, each counted from 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The cell is outside the palette.</exception>
    public static ClipColor At(int row, int column) =>
        row is < 1 or > Rows
            ? throw new ArgumentOutOfRangeException(nameof(row), row, $"Rows run from 1 to {Rows}.")
        : column is < 1 or > Columns
            ? throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                $"Columns run from 1 to {Columns}."
            )
        : new((row - 1) * Columns + column - 1);

    /// <summary>
    /// The color Live stored as <paramref name="index"/>, whatever it is, so that a file Live wrote
    /// always reads, even with an index outside the palette.
    /// </summary>
    public static ClipColor FromIndex(int index) => new(index);
}
