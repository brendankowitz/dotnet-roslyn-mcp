using System.Collections.Generic;

namespace VsIdeMcp.Models
{
    /// <summary>
    /// Information about a code symbol
    /// </summary>
    public class SymbolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string ContainingNamespace { get; set; } = string.Empty;
        public string? ContainingType { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public string Accessibility { get; set; } = string.Empty;
        public List<string> Modifiers { get; set; } = new();
        public string ProjectName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Search results for symbols
    /// </summary>
    public class SymbolSearchResult
    {
        public List<SymbolInfo> Symbols { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
