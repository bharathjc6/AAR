// =============================================================================
// AAR.Domain - ValueObjects/LineRange.cs
// Represents a range of lines in a source file
// =============================================================================

namespace AAR.Domain.ValueObjects;

/// <summary>
/// Represents a range of lines in a source file
/// </summary>
public record LineRange
{
    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public int Start { get; init; }
    
    /// <summary>
    /// Ending line number (1-based, inclusive)
    /// </summary>
    public int End { get; init; }

    public LineRange() { }
    
    public LineRange(int start, int end)
    {
        if (start < 1)
            throw new ArgumentOutOfRangeException(nameof(start), "Line number must be positive");
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "End line must be >= start line");
            
        Start = start;
        End = end;
    }

    /// <summary>
    /// Creates a LineRange for a single line
    /// </summary>
    public static LineRange SingleLine(int line) => new(line, line);

    /// <summary>
    /// Number of lines in the range
    /// </summary>
    public int LineCount => End - Start + 1;

    public override string ToString() => Start == End ? $"Line {Start}" : $"Lines {Start}-{End}";
}
