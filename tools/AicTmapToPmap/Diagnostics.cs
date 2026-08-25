namespace AicTmapToPmap;

internal enum DiagnosticSeverity { Info, Warning, Error }

internal sealed record ConversionDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Source,
    long Offset,
    string Message)
{
    public override string ToString()
    {
        string where = Offset >= 0 ? $" @0x{Offset:X}" : "";
        return $"{Source}{where}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
    }
}

internal sealed class DiagnosticSink
{
    private readonly List<ConversionDiagnostic> _items = new();
    internal IReadOnlyList<ConversionDiagnostic> Items => _items;
    internal bool HasErrors => _items.Any(x => x.Severity == DiagnosticSeverity.Error);
    internal bool HasWarnings => _items.Any(x => x.Severity == DiagnosticSeverity.Warning);

    internal void Warning(string code, string source, long offset, string message)
        => _items.Add(new ConversionDiagnostic(DiagnosticSeverity.Warning, code, source, offset, message));

    internal void Error(string code, string source, long offset, string message)
        => _items.Add(new ConversionDiagnostic(DiagnosticSeverity.Error, code, source, offset, message));
}
