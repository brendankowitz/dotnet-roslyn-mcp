using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.LanguageServices;
using VsIdeMcp.Models;

namespace VsIdeMcp.Services
{
    /// <summary>
    /// Service for searching and analyzing code symbols
    /// </summary>
    public class SymbolService
    {
        private readonly VisualStudioWorkspace? _workspace;

        public SymbolService(VisualStudioWorkspace? workspace)
        {
            _workspace = workspace; // Workspace can be null - will return empty results
        }

        /// <summary>
        /// Searches for symbols matching a query
        /// </summary>
        public async Task<SymbolSearchResult> SearchSymbolsAsync(
            string query,
            SymbolKind? kind = null,
            string? projectName = null)
        {
            var solution = _workspace.CurrentSolution;
            var results = new List<Models.SymbolInfo>();

            var projects = string.IsNullOrEmpty(projectName)
                ? solution.Projects
                : solution.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            foreach (var project in projects)
            {
                try
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    // Search through all symbols in the compilation
                    var symbols = await GetMatchingSymbolsAsync(compilation, query, kind);

                    foreach (var symbol in symbols.Take(100)) // Limit results
                    {
                        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                        if (location?.SourceTree == null) continue;

                        var lineSpan = location.GetLineSpan();

                        results.Add(new Models.SymbolInfo
                        {
                            Name = symbol.Name,
                            Kind = symbol.Kind.ToString(),
                            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString(),
                            ContainingType = symbol.ContainingType?.Name,
                            FilePath = location.SourceTree.FilePath,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Accessibility = symbol.DeclaredAccessibility.ToString(),
                            Modifiers = GetModifiers(symbol),
                            ProjectName = project.Name
                        });
                    }
                }
                catch
                {
                    // Skip projects that fail
                }
            }

            return new SymbolSearchResult
            {
                Symbols = results,
                TotalCount = results.Count
            };
        }

        /// <summary>
        /// Gets symbols matching the query and kind filter
        /// </summary>
        private async Task<IEnumerable<ISymbol>> GetMatchingSymbolsAsync(
            Compilation compilation,
            string query,
            SymbolKind? kind)
        {
            var allSymbols = new List<ISymbol>();

            // Get all named types (classes, interfaces, etc.)
            void VisitNamespace(INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNs)
                    {
                        VisitNamespace(childNs);
                    }
                    else if (member is INamedTypeSymbol type)
                    {
                        // Add the type itself
                        if (MatchesQuery(type, query) && MatchesKind(type, kind))
                        {
                            allSymbols.Add(type);
                        }

                        // Add type members (methods, properties, etc.)
                        foreach (var typeMember in type.GetMembers())
                        {
                            if (MatchesQuery(typeMember, query) && MatchesKind(typeMember, kind))
                            {
                                allSymbols.Add(typeMember);
                            }
                        }
                    }
                }
            }

            VisitNamespace(compilation.GlobalNamespace);

            return allSymbols;
        }

        /// <summary>
        /// Checks if a symbol matches the search query
        /// </summary>
        private bool MatchesQuery(ISymbol symbol, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return symbol.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   symbol.ToDisplayString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Checks if a symbol matches the kind filter
        /// </summary>
        private bool MatchesKind(ISymbol symbol, SymbolKind? kind)
        {
            if (!kind.HasValue)
            {
                return true;
            }

            return symbol.Kind == kind.Value;
        }

        /// <summary>
        /// Gets modifiers for a symbol
        /// </summary>
        private List<string> GetModifiers(ISymbol symbol)
        {
            var modifiers = new List<string>();

            if (symbol.IsStatic)
            {
                modifiers.Add("static");
            }

            if (symbol.IsAbstract)
            {
                modifiers.Add("abstract");
            }

            if (symbol.IsVirtual)
            {
                modifiers.Add("virtual");
            }

            if (symbol.IsSealed)
            {
                modifiers.Add("sealed");
            }

            if (symbol.IsOverride)
            {
                modifiers.Add("override");
            }

            if (symbol is IMethodSymbol method)
            {
                if (method.IsAsync)
                {
                    modifiers.Add("async");
                }
            }

            return modifiers;
        }
    }
}
