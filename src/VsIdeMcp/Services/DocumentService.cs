using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using VsIdeMcp.Models;

namespace VsIdeMcp.Services
{
    /// <summary>
    /// Service for reading and analyzing documents
    /// </summary>
    public class DocumentService
    {
        private readonly DTE2 _dte;
        private readonly VisualStudioWorkspace? _workspace;

        public DocumentService(DTE2 dte, VisualStudioWorkspace? workspace)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _workspace = workspace; // Workspace can be null - will have limited functionality
        }

        /// <summary>
        /// Reads document content with optional line range
        /// </summary>
        public async Task<DocumentContent> ReadDocumentAsync(
            string filePath,
            int? startLine = null,
            int? endLine = null)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var allLines = File.ReadAllLines(filePath);
            var totalLines = allLines.Length;

            var actualStartLine = Math.Max(1, startLine ?? 1);
            var actualEndLine = Math.Min(totalLines, endLine ?? totalLines);

            var selectedLines = allLines
                .Skip(actualStartLine - 1)
                .Take(actualEndLine - actualStartLine + 1);

            return new DocumentContent
            {
                FilePath = filePath,
                Content = string.Join(Environment.NewLine, selectedLines),
                StartLine = actualStartLine,
                EndLine = actualEndLine,
                TotalLines = totalLines
            };
        }

        /// <summary>
        /// Gets the structural outline of a document
        /// </summary>
        public async Task<DocumentOutline> GetDocumentOutlineAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var outline = new DocumentOutline
            {
                FilePath = filePath,
                Nodes = new List<OutlineNode>()
            };

            // Find the document in the workspace
            var document = _workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);

            if (document == null)
            {
                // Document not in workspace, return empty outline
                return outline;
            }

            var syntaxRoot = await document.GetSyntaxRootAsync();
            if (syntaxRoot == null)
            {
                return outline;
            }

            // Build outline from syntax tree
            outline.Nodes = BuildOutlineNodes(syntaxRoot);

            return outline;
        }

        /// <summary>
        /// Builds outline nodes from syntax tree
        /// </summary>
        private List<OutlineNode> BuildOutlineNodes(SyntaxNode root)
        {
            var nodes = new List<OutlineNode>();

            if (root is CompilationUnitSyntax compilationUnit)
            {
                foreach (var member in compilationUnit.Members)
                {
                    var node = ProcessMember(member);
                    if (node != null)
                    {
                        nodes.Add(node);
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// Processes a member syntax node
        /// </summary>
        private OutlineNode? ProcessMember(MemberDeclarationSyntax member)
        {
            OutlineNode? node = null;

            switch (member)
            {
                case NamespaceDeclarationSyntax namespaceDecl:
                    node = new OutlineNode
                    {
                        Name = namespaceDecl.Name.ToString(),
                        Kind = "Namespace",
                        Line = namespaceDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = namespaceDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1,
                        Children = namespaceDecl.Members.Select(ProcessMember).Where(n => n != null).ToList()!
                    };
                    break;

                case FileScopedNamespaceDeclarationSyntax fileScopedNs:
                    node = new OutlineNode
                    {
                        Name = fileScopedNs.Name.ToString(),
                        Kind = "Namespace",
                        Line = fileScopedNs.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = fileScopedNs.GetLocation().GetLineSpan().StartLinePosition.Character + 1,
                        Children = fileScopedNs.Members.Select(ProcessMember).Where(n => n != null).ToList()!
                    };
                    break;

                case ClassDeclarationSyntax classDecl:
                    node = new OutlineNode
                    {
                        Name = classDecl.Identifier.Text,
                        Kind = "Class",
                        Line = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = classDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1,
                        Children = classDecl.Members.Select(ProcessMember).Where(n => n != null).ToList()!
                    };
                    break;

                case InterfaceDeclarationSyntax interfaceDecl:
                    node = new OutlineNode
                    {
                        Name = interfaceDecl.Identifier.Text,
                        Kind = "Interface",
                        Line = interfaceDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = interfaceDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1,
                        Children = interfaceDecl.Members.Select(ProcessMember).Where(n => n != null).ToList()!
                    };
                    break;

                case StructDeclarationSyntax structDecl:
                    node = new OutlineNode
                    {
                        Name = structDecl.Identifier.Text,
                        Kind = "Struct",
                        Line = structDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = structDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1,
                        Children = structDecl.Members.Select(ProcessMember).Where(n => n != null).ToList()!
                    };
                    break;

                case EnumDeclarationSyntax enumDecl:
                    node = new OutlineNode
                    {
                        Name = enumDecl.Identifier.Text,
                        Kind = "Enum",
                        Line = enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = enumDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1
                    };
                    break;

                case MethodDeclarationSyntax methodDecl:
                    node = new OutlineNode
                    {
                        Name = methodDecl.Identifier.Text,
                        Kind = "Method",
                        Line = methodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = methodDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1
                    };
                    break;

                case PropertyDeclarationSyntax propertyDecl:
                    node = new OutlineNode
                    {
                        Name = propertyDecl.Identifier.Text,
                        Kind = "Property",
                        Line = propertyDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = propertyDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1
                    };
                    break;

                case FieldDeclarationSyntax fieldDecl:
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        node = new OutlineNode
                        {
                            Name = variable.Identifier.Text,
                            Kind = "Field",
                            Line = fieldDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            Column = fieldDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1
                        };
                        break; // Only take the first variable
                    }
                    break;

                case EventDeclarationSyntax eventDecl:
                    node = new OutlineNode
                    {
                        Name = eventDecl.Identifier.Text,
                        Kind = "Event",
                        Line = eventDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Column = eventDecl.GetLocation().GetLineSpan().StartLinePosition.Character + 1
                    };
                    break;
            }

            return node;
        }
    }
}
