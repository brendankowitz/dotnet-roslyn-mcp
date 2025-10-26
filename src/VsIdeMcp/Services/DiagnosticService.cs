using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices;
using VsIdeMcp.Models;

namespace VsIdeMcp.Services
{
    /// <summary>
    /// Service for accessing diagnostic information (errors, warnings, etc.)
    /// </summary>
    public class DiagnosticService
    {
        private readonly VisualStudioWorkspace? _workspace;

        public DiagnosticService(VisualStudioWorkspace? workspace)
        {
            _workspace = workspace; // Workspace can be null - will return empty results
        }

        /// <summary>
        /// Gets diagnostics for the solution or filtered by file/project
        /// </summary>
        public async Task<DiagnosticsResult> GetDiagnosticsAsync(
            string? filePath = null,
            string? projectName = null,
            DiagnosticSeverity? minSeverity = null)
        {
            var solution = _workspace.CurrentSolution;
            var allDiagnostics = new System.Collections.Generic.List<DiagnosticInfo>();

            var projects = string.IsNullOrEmpty(projectName)
                ? solution.Projects
                : solution.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            foreach (var project in projects)
            {
                try
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    // Get compiler diagnostics
                    var diagnostics = compilation.GetDiagnostics();

                    // Also get analyzer diagnostics if available
                    if (project.AnalyzerReferences.Any())
                    {
                        try
                        {
                            var analyzers = project.AnalyzerReferences
                                .SelectMany(r => r.GetAnalyzers(project.Language))
                                .ToImmutableArray();

                            if (analyzers.Any())
                            {
                                var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
                                var analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();
                                diagnostics = diagnostics.AddRange(analyzerDiagnostics);
                            }
                        }
                        catch
                        {
                            // Ignore analyzer errors
                        }
                    }

                    // Filter and convert diagnostics
                    foreach (var diagnostic in diagnostics)
                    {
                        // Apply severity filter
                        if (minSeverity.HasValue && diagnostic.Severity < minSeverity.Value)
                        {
                            continue;
                        }

                        // Apply file path filter
                        var diagFilePath = diagnostic.Location.SourceTree?.FilePath;
                        if (!string.IsNullOrEmpty(filePath) &&
                            !string.Equals(diagFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Skip diagnostics without source location
                        if (diagFilePath == null)
                        {
                            continue;
                        }

                        var lineSpan = diagnostic.Location.GetLineSpan();

                        allDiagnostics.Add(new DiagnosticInfo
                        {
                            Id = diagnostic.Id,
                            Severity = diagnostic.Severity.ToString(),
                            Message = diagnostic.GetMessage(),
                            FilePath = diagFilePath,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            EndLine = lineSpan.EndLinePosition.Line + 1,
                            EndColumn = lineSpan.EndLinePosition.Character + 1,
                            ProjectName = project.Name,
                            HasCodeFix = false, // TODO: Implement code fix detection
                            Category = diagnostic.Descriptor.Category
                        });
                    }
                }
                catch
                {
                    // Skip projects that fail
                }
            }

            return new DiagnosticsResult
            {
                Diagnostics = allDiagnostics,
                Summary = new DiagnosticSummary
                {
                    ErrorCount = allDiagnostics.Count(d => d.Severity == "Error"),
                    WarningCount = allDiagnostics.Count(d => d.Severity == "Warning"),
                    InfoCount = allDiagnostics.Count(d => d.Severity == "Info"),
                    SuggestionCount = allDiagnostics.Count(d => d.Severity == "Hidden")
                }
            };
        }
    }
}
