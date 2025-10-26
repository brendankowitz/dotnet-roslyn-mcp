using System.Collections.Generic;

namespace VsIdeMcp.Models
{
    /// <summary>
    /// Container for diagnostic results
    /// </summary>
    public class DiagnosticsResult
    {
        public List<DiagnosticInfo> Diagnostics { get; set; } = new();
        public DiagnosticSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// Information about a single diagnostic (error, warning, etc.)
    /// </summary>
    public class DiagnosticInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public bool HasCodeFix { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary of diagnostics by severity
    /// </summary>
    public class DiagnosticSummary
    {
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int InfoCount { get; set; }
        public int SuggestionCount { get; set; }
    }
}
