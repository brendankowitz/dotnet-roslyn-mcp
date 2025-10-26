using System.Collections.Generic;

namespace VsIdeMcp.Models
{
    /// <summary>
    /// Information about a document
    /// </summary>
    public class DocumentInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public bool IsOpen { get; set; }
        public bool IsDirty { get; set; }
        public int CursorLine { get; set; }
        public int CursorColumn { get; set; }
    }

    /// <summary>
    /// Document content with optional line range
    /// </summary>
    public class DocumentContent
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int TotalLines { get; set; }
    }

    /// <summary>
    /// Outline node representing a code element
    /// </summary>
    public class OutlineNode
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public List<OutlineNode> Children { get; set; } = new();
    }

    /// <summary>
    /// Document outline (structure)
    /// </summary>
    public class DocumentOutline
    {
        public string FilePath { get; set; } = string.Empty;
        public List<OutlineNode> Nodes { get; set; } = new();
    }
}
