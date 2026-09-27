namespace MobGrinder;

public enum DiagnosticSeverity
{
    Debug,
    Information,
    Warning,
    Error,
}

public sealed record DiagnosticEntry(DateTime Timestamp, DiagnosticSeverity Severity, string Message);
