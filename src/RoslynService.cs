using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp;

public class RoslynService
{
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private readonly Dictionary<string, Document> _documentCache = new();
    private readonly int _maxDiagnostics;
    private readonly int _timeoutSeconds;

    private DateTime? _solutionLoadedAt;

    public RoslynService()
    {
        _maxDiagnostics = int.TryParse(Environment.GetEnvironmentVariable("ROSLYN_MAX_DIAGNOSTICS"), out var maxDiag)
            ? maxDiag : 100;
        _timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("ROSLYN_TIMEOUT_SECONDS"), out var timeout)
            ? timeout : 30;
    }

    // Helper method for glob pattern matching (supports * and ? wildcards)
    private static bool MatchesGlobPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        // Convert glob pattern to regex
        // Escape regex special chars except * and ?
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")  // * matches any characters
            .Replace("\\?", ".")   // ? matches single character
            + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public async Task<object> LoadSolutionAsync(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        // Dispose existing workspace
        _workspace?.Dispose();
        _documentCache.Clear();

        _workspace = MSBuildWorkspace.Create();
        _workspace.WorkspaceFailed += (sender, args) =>
        {
            Console.Error.WriteLine($"[Warning] Workspace: {args.Diagnostic.Message}");
        };

        _solution = await _workspace.OpenSolutionAsync(solutionPath);
        _solutionLoadedAt = DateTime.UtcNow;

        var projectCount = _solution.ProjectIds.Count;
        var documentCount = _solution.Projects.Sum(p => p.DocumentIds.Count);

        return new
        {
            success = true,
            solutionPath,
            projectCount,
            documentCount
        };
    }

    public async Task<object> GetHealthCheckAsync()
    {
        if (_solution == null || _workspace == null)
        {
            return new
            {
                status = "Not Ready",
                message = "No solution loaded. Call roslyn:load_solution first or set DOTNET_SOLUTION_PATH environment variable.",
                solution = (object?)null,
                workspace = (object?)null
            };
        }

        // Get diagnostic summary
        var errorCount = 0;
        var warningCount = 0;

        try
        {
            foreach (var project in _solution.Projects.Take(5)) // Sample first 5 projects for quick health check
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    var diagnostics = compilation.GetDiagnostics();
                    errorCount += diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                    warningCount += diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
                }
            }
        }
        catch
        {
            // Ignore errors during health check diagnostics
        }

        var projectCount = _solution.ProjectIds.Count;
        var documentCount = _solution.Projects.Sum(p => p.DocumentIds.Count);

        return new
        {
            status = "Ready",
            message = "Roslyn MCP Server is operational",
            solution = new
            {
                loaded = true,
                path = _solution.FilePath,
                projects = projectCount,
                documents = documentCount,
                loadedAt = _solutionLoadedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                errors = errorCount,
                warnings = warningCount
            },
            workspace = new
            {
                indexed = true,
                cacheSize = _documentCache.Count
            },
            capabilities = new
            {
                findReferences = true,
                findImplementations = true,
                codeFixProvider = true,
                symbolSearch = true,
                diagnostics = true
            },
            configuration = new
            {
                maxDiagnostics = _maxDiagnostics,
                timeoutSeconds = _timeoutSeconds,
                semanticCacheEnabled = Environment.GetEnvironmentVariable("ROSLYN_ENABLE_SEMANTIC_CACHE") != "false"
            }
        };
    }

    public async Task<object> GetSymbolInfoAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            return new { message = "No symbol found at position" };

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol;

        if (symbol == null)
        {
            // Try getting the declared symbol if we're on a declaration
            symbol = semanticModel.GetDeclaredSymbol(node);
        }

        if (symbol == null)
            return new { message = "No symbol found at position" };

        return await FormatSymbolInfoAsync(symbol);
    }

    public async Task<object> FindReferencesAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 100; // Default to 100

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
            throw new Exception("No symbol found at position");

        var references = await SymbolFinder.FindReferencesAsync(symbol, _solution!);
        var allLocations = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .ToList();

        var totalReferences = allLocations.Count;
        var referenceList = new List<object>();

        foreach (var loc in allLocations)
        {
            if (referenceList.Count >= maxResultsToReturn)
                break; // Stop at limit

            var refDocument = _solution!.GetDocument(loc.Document.Id);
            if (refDocument == null) continue;

            var refTree = await refDocument.GetSyntaxTreeAsync();
            if (refTree == null) continue;

            var refSpan = loc.Location.SourceSpan;
            var lineSpan = refTree.GetLineSpan(refSpan);
            var text = refTree.GetText();
            var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

            referenceList.Add(new
            {
                filePath = refDocument.FilePath,
                line = lineSpan.StartLinePosition.Line,
                column = lineSpan.StartLinePosition.Character,
                lineText,
                kind = "read" // TODO: Detect write vs read references
            });
        }

        return new
        {
            symbolName = symbol.Name,
            symbolKind = symbol.Kind.ToString(),
            totalReferences,
            referencesShown = referenceList.Count,
            truncated = totalReferences > referenceList.Count,
            references = referenceList,
            hint = totalReferences > referenceList.Count
                ? $"Showing first {referenceList.Count} of {totalReferences} references. Use maxResults parameter to see more."
                : null
        };
    }

    public async Task<object> GoToDefinitionAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
        {
            return new
            {
                error = "No symbol found at position",
                line,
                column,
                hint = "Ensure cursor is on a symbol name (class, method, variable, etc.)"
            };
        }

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol;

        // If not a reference, try getting declared symbol
        if (symbol == null)
        {
            symbol = semanticModel.GetDeclaredSymbol(node);
        }

        if (symbol == null)
        {
            return new
            {
                error = "No symbol found at position",
                line,
                column,
                tokenText = token.Text,
                nodeKind = node.Kind().ToString(),
                hint = "Position may be on whitespace or non-symbol token. Try positioning on an identifier."
            };
        }

        // Get the definition location (first location in source)
        var definitionLocation = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);

        if (definitionLocation == null)
        {
            return new
            {
                error = "Symbol definition not in source",
                symbolName = symbol.Name,
                symbolKind = symbol.Kind.ToString(),
                fullyQualifiedName = symbol.ToDisplayString(),
                hint = "Symbol is defined in metadata (external library/NuGet package). Use decompiler to view."
            };
        }

        var defLineSpan = definitionLocation.GetLineSpan();

        return new
        {
            symbol = new
            {
                name = symbol.Name,
                kind = symbol.Kind.ToString(),
                fullyQualifiedName = symbol.ToDisplayString(),
                containingType = symbol.ContainingType?.ToDisplayString(),
                containingNamespace = symbol.ContainingNamespace?.ToDisplayString()
            },
            definition = new
            {
                filePath = defLineSpan.Path,
                line = defLineSpan.StartLinePosition.Line,
                column = defLineSpan.StartLinePosition.Character,
                endLine = defLineSpan.EndLinePosition.Line,
                endColumn = defLineSpan.EndLinePosition.Character
            },
            hint = "This is the definition location. Use roslyn:find_references to see all usages."
        };
    }

    public async Task<object> FindImplementationsAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 50; // Default to 50

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
            throw new Exception("No symbol found at position");

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return new
            {
                error = "Not a type symbol",
                actualKind = symbol.Kind.ToString(),
                symbolName = symbol.Name,
                hint = "This tool requires a type symbol (interface, class, or abstract class). Place cursor on a type declaration."
            };
        }

        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, _solution!);
        var allImplementations = implementations.ToList();
        var totalImplementations = allImplementations.Count;

        var implementationList = new List<object>();
        foreach (var impl in allImplementations)
        {
            if (implementationList.Count >= maxResultsToReturn)
                break; // Stop at limit

            var locations = impl.Locations
                .Where(loc => loc.IsInSource)
                .Select(loc =>
                {
                    var lineSpan = loc.GetLineSpan();
                    return new
                    {
                        filePath = lineSpan.Path,
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    };
                })
                .ToList();

            implementationList.Add(new
            {
                name = impl.ToDisplayString(),
                kind = impl.TypeKind.ToString(),
                containingNamespace = impl.ContainingNamespace?.ToDisplayString(),
                locations
            });
        }

        return new
        {
            baseType = typeSymbol.ToDisplayString(),
            totalImplementations,
            implementationsShown = implementationList.Count,
            truncated = totalImplementations > implementationList.Count,
            implementations = implementationList,
            hint = totalImplementations > implementationList.Count
                ? $"Showing first {implementationList.Count} of {totalImplementations} implementations. Use maxResults parameter to see more."
                : null
        };
    }

    public async Task<object> GetTypeHierarchyAsync(string filePath, int line, int column, int? maxDerivedTypes = null)
    {
        EnsureSolutionLoaded();

        var maxDerivedToReturn = maxDerivedTypes ?? 50; // Default to 50

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
            throw new Exception("No symbol found at position");

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return new
            {
                error = "Not a type symbol",
                actualKind = symbol.Kind.ToString(),
                symbolName = symbol.Name,
                hint = "This tool requires a type symbol (class, struct, interface). Place cursor on a type declaration."
            };
        }

        // Get base types
        var baseTypes = new List<object>();
        var currentBase = typeSymbol.BaseType;
        while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(FormatTypeInfo(currentBase));
            currentBase = currentBase.BaseType;
        }

        // Get interfaces
        var interfaces = typeSymbol.AllInterfaces
            .Select(i => FormatTypeInfo(i))
            .ToList();

        // Get derived types
        var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, _solution!, transitive: false);
        var allDerived = derivedTypes.ToList();
        var totalDerived = allDerived.Count;

        var derivedList = allDerived
            .Take(maxDerivedToReturn)
            .Select(d => FormatTypeInfo(d))
            .ToList();

        return new
        {
            typeName = typeSymbol.ToDisplayString(),
            baseTypes,
            interfaces,
            totalDerivedTypes = totalDerived,
            derivedTypesShown = derivedList.Count,
            truncated = totalDerived > derivedList.Count,
            derivedTypes = derivedList,
            hint = totalDerived > derivedList.Count
                ? $"Showing first {derivedList.Count} of {totalDerived} derived types. Use maxDerivedTypes parameter to see more."
                : null
        };
    }

    public async Task<object> SearchSymbolsAsync(string query, string? kind, int maxResults, string? namespaceFilter = null, int offset = 0)
    {
        EnsureSolutionLoaded();

        var allResults = new List<object>();

        // Check if query contains glob patterns
        bool isGlobPattern = query.Contains('*') || query.Contains('?');

        foreach (var project in _solution!.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var symbols = compilation.GetSymbolsWithName(
                name => isGlobPattern ? MatchesGlobPattern(name, query) : name.Contains(query, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.All);

            foreach (var symbol in symbols)
            {
                // Filter by kind
                if (!string.IsNullOrEmpty(kind))
                {
                    bool matches = false;

                    // For type symbols (Class, Interface, Struct, Enum), check TypeKind
                    if (symbol is INamedTypeSymbol namedType)
                    {
                        matches = namedType.TypeKind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // For other symbols (Method, Property, Field, etc.), check SymbolKind
                        matches = symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matches)
                        continue;
                }

                // Filter by namespace
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                    bool namespaceMatches = MatchesGlobPattern(symbolNamespace, namespaceFilter);

                    if (!namespaceMatches)
                        continue;
                }

                var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                if (location == null) continue;

                var lineSpan = location.GetLineSpan();

                allResults.Add(new
                {
                    name = symbol.Name,
                    fullyQualifiedName = symbol.ToDisplayString(),
                    kind = symbol.Kind.ToString(),
                    containingType = symbol.ContainingType?.ToDisplayString(),
                    containingNamespace = symbol.ContainingNamespace?.ToDisplayString(),
                    location = new
                    {
                        filePath = lineSpan.Path,
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    }
                });

                // Continue collecting until we have offset + maxResults (to handle pagination)
                if (allResults.Count >= offset + maxResults + 100) // +100 buffer for accurate totalCount estimation
                    break;
            }

            if (allResults.Count >= offset + maxResults + 100)
                break;
        }

        // Apply pagination
        var totalCount = allResults.Count;
        var paginatedResults = allResults.Skip(offset).Take(maxResults).ToList();
        var hasMore = offset + paginatedResults.Count < totalCount;

        return new
        {
            query,
            totalCount,
            offset,
            count = paginatedResults.Count,
            hasMore,
            results = paginatedResults,
            pagination = new
            {
                nextOffset = hasMore ? offset + paginatedResults.Count : (int?)null,
                hint = hasMore ? $"Use offset={offset + paginatedResults.Count} to get next page" : "No more results"
            }
        };
    }

    public async Task<object> GetDiagnosticsAsync(string? filePath, string? projectPath, string? severity, bool includeHidden)
    {
        EnsureSolutionLoaded();

        var allDiagnostics = new List<Diagnostic>();

        if (!string.IsNullOrEmpty(filePath))
        {
            // Get diagnostics for specific file
            var document = await GetDocumentAsync(filePath);
            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel != null)
            {
                allDiagnostics.AddRange(semanticModel.GetDiagnostics());
            }
        }
        else if (!string.IsNullOrEmpty(projectPath))
        {
            // Get diagnostics for specific project
            var project = _solution!.Projects.FirstOrDefault(p => p.FilePath == projectPath);
            if (project != null)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    allDiagnostics.AddRange(compilation.GetDiagnostics());
                }
            }
        }
        else
        {
            // Get diagnostics for entire solution
            foreach (var project in _solution!.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    allDiagnostics.AddRange(compilation.GetDiagnostics());
                }
            }
        }

        // Filter by severity
        if (!string.IsNullOrEmpty(severity))
        {
            var severityEnum = Enum.Parse<DiagnosticSeverity>(severity, ignoreCase: true);
            allDiagnostics = allDiagnostics.Where(d => d.Severity == severityEnum).ToList();
        }

        // Filter hidden
        if (!includeHidden)
        {
            allDiagnostics = allDiagnostics.Where(d => d.Severity != DiagnosticSeverity.Hidden).ToList();
        }

        // Limit results
        allDiagnostics = allDiagnostics.Take(_maxDiagnostics).ToList();

        var diagnosticList = allDiagnostics.Select(d =>
        {
            var lineSpan = d.Location.GetLineSpan();
            return new
            {
                id = d.Id,
                severity = d.Severity.ToString(),
                message = d.GetMessage(),
                filePath = lineSpan.Path,
                line = lineSpan.StartLinePosition.Line,
                column = lineSpan.StartLinePosition.Character,
                endLine = lineSpan.EndLinePosition.Line,
                endColumn = lineSpan.EndLinePosition.Character
            };
        }).ToList();

        return new
        {
            totalCount = diagnosticList.Count,
            errorCount = diagnosticList.Count(d => d.severity == "Error"),
            warningCount = diagnosticList.Count(d => d.severity == "Warning"),
            diagnostics = diagnosticList
        };
    }

    public async Task<object> GetCodeFixesAsync(string filePath, string diagnosticId, int line, int column)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        // Get all diagnostics for the document (semantic + syntax)
        var allDiagnostics = semanticModel.GetDiagnostics().ToList();
        var syntaxDiagnostics = syntaxTree.GetDiagnostics().ToList();
        allDiagnostics.AddRange(syntaxDiagnostics);

        var position = GetPosition(syntaxTree, line, column);

        // Strategy 1: Try exact ID match with position contained in span
        var diagnostic = allDiagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            d.Location.SourceSpan.Contains(position));

        // Strategy 2: Try exact ID match with nearby position (within 50 chars)
        if (diagnostic == null)
        {
            diagnostic = allDiagnostics.FirstOrDefault(d =>
                d.Id == diagnosticId &&
                Math.Abs(d.Location.SourceSpan.Start - position) < 50);
        }

        // Strategy 3: Try exact ID match anywhere in the file
        if (diagnostic == null)
        {
            diagnostic = allDiagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        }

        // Find all diagnostics at or near the position for error message
        var diagnosticsAtPosition = allDiagnostics
            .Where(d => d.Location.SourceSpan.Contains(position) || Math.Abs(d.Location.SourceSpan.Start - position) < 50)
            .Take(10)
            .Select(d => new
            {
                id = d.Id,
                message = d.GetMessage(),
                severity = d.Severity.ToString(),
                span = new
                {
                    start = d.Location.SourceSpan.Start,
                    end = d.Location.SourceSpan.End,
                    length = d.Location.SourceSpan.Length
                }
            })
            .ToList();

        if (diagnostic == null)
        {
            return new
            {
                error = $"No diagnostic with ID '{diagnosticId}' found",
                line,
                column,
                position,
                diagnosticsNearby = diagnosticsAtPosition,
                hint = diagnosticsAtPosition.Count > 0
                    ? $"Found {diagnosticsAtPosition.Count} other diagnostic(s) near this position. Try using one of their IDs."
                    : "No diagnostics found at this position. Run roslyn:get_diagnostics to see all available diagnostics."
            };
        }

        var lineSpan = diagnostic.Location.GetLineSpan();

        // Note: Actual code fix provider infrastructure would require CodeFixProvider registration
        // For now, we return diagnostic info and common fix suggestions based on diagnostic ID
        var suggestedFixes = GetCommonFixSuggestions(diagnostic.Id, diagnostic.GetMessage());

        return new
        {
            diagnosticId = diagnostic.Id,
            message = diagnostic.GetMessage(),
            severity = diagnostic.Severity.ToString(),
            location = new
            {
                filePath = lineSpan.Path,
                startLine = lineSpan.StartLinePosition.Line,
                startColumn = lineSpan.StartLinePosition.Character,
                endLine = lineSpan.EndLinePosition.Line,
                endColumn = lineSpan.EndLinePosition.Character
            },
            suggestedFixes,
            note = "Code fix application requires IDE integration. Suggestions are based on common fixes for this diagnostic."
        };
    }

    private List<string> GetCommonFixSuggestions(string diagnosticId, string message)
    {
        // Common fix suggestions for well-known diagnostic IDs
        return diagnosticId switch
        {
            "CS0168" => new List<string> { "Remove unused variable", "Use the variable", "Prefix with underscore to indicate intentionally unused" },
            "CS0219" => new List<string> { "Remove unused variable", "Use the variable in an expression" },
            "CS1998" => new List<string> { "Add await keyword to async operation", "Remove async modifier if method doesn't need to be async", "Return Task.CompletedTask or Task.FromResult()" },
            "CS0162" => new List<string> { "Remove unreachable code", "Fix control flow logic" },
            "CS0649" => new List<string> { "Initialize the field", "Remove unused field", "Mark as obsolete if legacy code" },
            "CS8019" => new List<string> { "Remove unnecessary using directive", "Run 'Organize Usings'" },
            "CS0246" => new List<string> { "Add missing using directive", "Check type name spelling", "Add assembly reference" },
            "CS0103" => new List<string> { "Add missing using directive", "Check name spelling", "Declare the variable or method" },
            "CS4012" => new List<string> { "Move Utf8JsonReader to non-async context", "Use synchronous JSON parsing", "Wrap in Task.Run() for async operation" },
            "CS1503" => new List<string> { "Cast argument to expected type", "Change parameter type", "Fix argument expression" },
            _ => new List<string> { "Review diagnostic message for fix guidance", "Consult C# documentation for " + diagnosticId }
        };
    }

    public async Task<object> ApplyCodeFixAsync(
        string filePath,
        string diagnosticId,
        int line,
        int column,
        int? fixIndex = null,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        // Get all diagnostics (semantic + syntax)
        var allDiagnostics = semanticModel.GetDiagnostics().ToList();
        var syntaxDiagnostics = syntaxTree.GetDiagnostics().ToList();
        allDiagnostics.AddRange(syntaxDiagnostics);

        var position = GetPosition(syntaxTree, line, column);

        // Find the diagnostic using the same strategy as GetCodeFixesAsync
        var diagnostic = allDiagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            d.Location.SourceSpan.Contains(position));

        if (diagnostic == null)
        {
            diagnostic = allDiagnostics.FirstOrDefault(d =>
                d.Id == diagnosticId &&
                Math.Abs(d.Location.SourceSpan.Start - position) < 50);
        }

        if (diagnostic == null)
        {
            diagnostic = allDiagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        }

        if (diagnostic == null)
        {
            return new
            {
                error = $"No diagnostic with ID '{diagnosticId}' found at line {line}, column {column}",
                hint = "Run roslyn:get_code_fixes first to verify the diagnostic exists at this location."
            };
        }

        // Get code actions from built-in code fix providers
        var codeActions = await GetCodeActionsForDiagnosticAsync(document, diagnostic);

        if (codeActions.Count == 0)
        {
            return new
            {
                error = $"No code fixes available for diagnostic '{diagnosticId}'",
                diagnosticMessage = diagnostic.GetMessage(),
                hint = "This diagnostic may not have automated code fixes available. Try the suggestions from roslyn:get_code_fixes.",
                suggestedFixes = GetCommonFixSuggestions(diagnosticId, diagnostic.GetMessage())
            };
        }

        // If no fixIndex specified, return available fixes
        if (fixIndex == null)
        {
            var availableFixes = codeActions.Select((action, index) => new
            {
                index,
                title = action.Title,
                equivalenceKey = action.EquivalenceKey
            }).ToList();

            return new
            {
                diagnosticId = diagnostic.Id,
                message = diagnostic.GetMessage(),
                availableFixes,
                hint = $"Found {availableFixes.Count} available fix(es). Call again with fixIndex parameter to apply a specific fix (preview mode recommended first)."
            };
        }

        // Validate fixIndex
        if (fixIndex < 0 || fixIndex >= codeActions.Count)
        {
            return new
            {
                error = $"Invalid fixIndex {fixIndex}. Available range: 0 to {codeActions.Count - 1}",
                availableFixCount = codeActions.Count
            };
        }

        var selectedAction = codeActions[fixIndex.Value];

        // Apply the code action
        var operations = await selectedAction.GetOperationsAsync(CancellationToken.None);
        var changedSolution = _solution;

        foreach (var operation in operations)
        {
            if (operation is ApplyChangesOperation applyChangesOp)
            {
                changedSolution = applyChangesOp.ChangedSolution;
                break;
            }
        }

        if (changedSolution == _solution)
        {
            return new
            {
                error = "Code fix did not produce any changes",
                fixTitle = selectedAction.Title,
                hint = "The selected fix may not be applicable in this context."
            };
        }

        // Collect all changed documents
        var changedDocuments = new List<object>();
        var solutionChanges = changedSolution.GetChanges(_solution!);

        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            // Check for added documents
            foreach (var addedDocId in projectChanges.GetAddedDocuments())
            {
                var addedDoc = changedSolution.GetDocument(addedDocId);
                if (addedDoc == null) continue;

                var text = await addedDoc.GetTextAsync();
                changedDocuments.Add(new
                {
                    filePath = addedDoc.FilePath ?? $"NewFile_{addedDoc.Name}",
                    fileName = addedDoc.Name,
                    isNewFile = true,
                    newText = preview ? text.ToString() : null,
                    changeType = "Added"
                });

                // Write to disk if not preview
                if (!preview && addedDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(addedDoc.FilePath, text.ToString());
                }
            }

            // Check for changed documents
            foreach (var changedDocId in projectChanges.GetChangedDocuments())
            {
                var oldDoc = _solution!.GetDocument(changedDocId);
                var newDoc = changedSolution.GetDocument(changedDocId);
                if (oldDoc == null || newDoc == null) continue;

                var oldText = await oldDoc.GetTextAsync();
                var newText = await newDoc.GetTextAsync();

                var changes = newText.GetTextChanges(oldText).ToList();

                changedDocuments.Add(new
                {
                    filePath = newDoc.FilePath,
                    fileName = newDoc.Name,
                    isNewFile = false,
                    changeCount = changes.Count,
                    newText = preview ? newText.ToString() : null,
                    changes = preview ? changes.Select(c => new
                    {
                        span = new { start = c.Span.Start, end = c.Span.End, length = c.Span.Length },
                        oldText = oldText.ToString(c.Span),
                        newText = c.NewText
                    }).ToList() : null,
                    changeType = "Modified"
                });

                // Write to disk if not preview
                if (!preview && newDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(newDoc.FilePath, newText.ToString());

                    // Update solution with changes for subsequent operations
                    _solution = changedSolution;
                }
            }

            // Check for removed documents
            foreach (var removedDocId in projectChanges.GetRemovedDocuments())
            {
                var removedDoc = _solution!.GetDocument(removedDocId);
                if (removedDoc == null) continue;

                changedDocuments.Add(new
                {
                    filePath = removedDoc.FilePath,
                    fileName = removedDoc.Name,
                    isNewFile = false,
                    changeType = "Removed"
                });

                // Delete file if not preview
                if (!preview && removedDoc.FilePath != null && File.Exists(removedDoc.FilePath))
                {
                    File.Delete(removedDoc.FilePath);
                }
            }
        }

        return new
        {
            applied = !preview,
            diagnosticId = diagnostic.Id,
            fixTitle = selectedAction.Title,
            fixIndex = fixIndex.Value,
            changedFiles = changedDocuments,
            preview,
            hint = preview
                ? "This is a preview. Set preview=false to apply changes to disk."
                : "Changes have been applied to disk and the solution has been updated."
        };
    }

    private async Task<List<CodeAction>> GetCodeActionsForDiagnosticAsync(Document document, Diagnostic diagnostic)
    {
        var codeActions = new List<CodeAction>();
        var codeFixProviders = GetBuiltInCodeFixProviders();

        foreach (var provider in codeFixProviders)
        {
            if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                continue;

            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => codeActions.Add(action),
                CancellationToken.None);

            try
            {
                await provider.RegisterCodeFixesAsync(context);
            }
            catch
            {
                // Some providers may throw if they can't handle the diagnostic
                // Silently continue to next provider
            }
        }

        return codeActions;
    }

    private List<CodeFixProvider> GetBuiltInCodeFixProviders()
    {
        // Get built-in C# code fix providers from Roslyn
        var codeFixProviderType = typeof(CodeFixProvider);
        var assembly = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly;

        var providers = assembly.GetTypes()
            .Where(t => !t.IsAbstract && codeFixProviderType.IsAssignableFrom(t))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 0)) // Has parameterless constructor
            .Select(t =>
            {
                try
                {
                    return Activator.CreateInstance(t) as CodeFixProvider;
                }
                catch
                {
                    return null;
                }
            })
            .Where(p => p != null)
            .Cast<CodeFixProvider>()
            .ToList();

        return providers;
    }

    public Task<object> GetProjectStructureAsync(
        bool includeReferences,
        bool includeDocuments,
        string? projectNamePattern = null,
        int? maxProjects = null,
        bool summaryOnly = false)
    {
        EnsureSolutionLoaded();

        // Filter projects by name pattern
        var filteredProjects = _solution!.Projects.AsEnumerable();

        if (!string.IsNullOrEmpty(projectNamePattern))
        {
            // Support wildcards: * and ?
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(projectNamePattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            filteredProjects = filteredProjects.Where(p => regex.IsMatch(p.Name));
        }

        // Apply max projects limit
        if (maxProjects.HasValue && maxProjects.Value > 0)
        {
            filteredProjects = filteredProjects.Take(maxProjects.Value);
        }

        var projectsList = filteredProjects.ToList();

        // Summary mode - just names and counts
        if (summaryOnly)
        {
            var summary = projectsList.Select(p => new
            {
                name = p.Name,
                documentCount = p.DocumentIds.Count,
                projectReferenceCount = p.ProjectReferences.Count(),
                language = p.Language
            }).ToList();

            return Task.FromResult<object>(new
            {
                solutionPath = _solution!.FilePath,
                totalProjects = _solution!.Projects.Count(),
                filteredProjects = summary.Count,
                truncated = maxProjects.HasValue && _solution!.Projects.Count() > maxProjects.Value,
                projects = summary,
                hint = "Use projectNamePattern to filter, maxProjects to limit, or summaryOnly=false for full details"
            });
        }

        // Full mode - detailed info
        var projects = new List<object>();

        foreach (var project in projectsList)
        {
            var references = includeReferences
                ? project.MetadataReferences
                    .Take(100) // Limit references to prevent huge output
                    .Select(r => r.Display ?? "Unknown")
                    .ToList()
                : null;

            var projectReferences = project.ProjectReferences
                .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name ?? "Unknown")
                .ToList();

            var documents = includeDocuments
                ? project.Documents
                    .Take(500) // Limit documents to prevent huge output
                    .Select(d => new
                    {
                        name = d.Name,
                        filePath = d.FilePath,
                        folders = d.Folders.ToList()
                    })
                    .ToList()
                : null;

            var referenceCount = project.MetadataReferences.Count();
            var documentCount = project.DocumentIds.Count;

            projects.Add(new
            {
                name = project.Name,
                filePath = project.FilePath,
                language = project.Language,
                outputPath = project.OutputFilePath,
                targetFramework = project.CompilationOptions?.Platform.ToString(),
                documentCount,
                referenceCount,
                references = includeReferences ? (referenceCount > 100 ? references!.Concat(new[] { $"... and {referenceCount - 100} more" }).ToList() : references) : null,
                projectReferences,
                documents = includeDocuments ? (documentCount > 500 ? documents!.Concat(new[] { new { name = $"... and {documentCount - 500} more documents", filePath = (string?)null, folders = new List<string>() } }).ToList() : documents) : null
            });
        }

        return Task.FromResult<object>(new
        {
            solutionPath = _solution!.FilePath,
            totalProjects = _solution!.Projects.Count(),
            filteredProjects = projects.Count,
            truncated = maxProjects.HasValue && filteredProjects.Count() > maxProjects.Value,
            projects,
            hint = projectsList.Count > 10 ? "Large solution detected. Consider using summaryOnly=true, projectNamePattern, or maxProjects to reduce output size." : null
        });
    }

    public async Task<object> OrganizeUsingsAsync(string filePath)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var root = await syntaxTree.GetRootAsync();
        if (root is not CompilationUnitSyntax compilationUnit)
            throw new Exception("Not a valid C# file");

        // Get all usings
        var usings = compilationUnit.Usings;

        // Sort them (System namespaces first, then alphabetically)
        var sortedUsings = usings
            .OrderBy(u => u.Name?.ToString().StartsWith("System") == true ? 0 : 1)
            .ThenBy(u => u.Name?.ToString())
            .ToList();

        // Create new compilation unit with sorted usings
        var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(sortedUsings));

        return new
        {
            success = true,
            message = "Usings organized",
            organizedText = newRoot.ToFullString()
        };
    }

    public async Task<object> OrganizeUsingsBatchAsync(string? projectName, string? filePattern, bool preview = true)
    {
        EnsureSolutionLoaded();

        var projectsToProcess = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        var processedFiles = new List<object>();
        var totalFiles = 0;
        var filesWithChanges = 0;

        foreach (var project in projectsToProcess)
        {
            var documents = project.Documents;

            // Apply file pattern filter if specified
            if (!string.IsNullOrEmpty(filePattern))
            {
                documents = documents.Where(d =>
                    d.FilePath != null && MatchesGlobPattern(Path.GetFileName(d.FilePath), filePattern));
            }

            foreach (var document in documents)
            {
                if (document.FilePath == null) continue;
                totalFiles++;

                try
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync();
                    if (root is not CompilationUnitSyntax compilationUnit) continue;

                    var usings = compilationUnit.Usings;
                    if (usings.Count == 0) continue;

                    // Sort usings
                    var sortedUsings = usings
                        .OrderBy(u => u.Name?.ToString().StartsWith("System") == true ? 0 : 1)
                        .ThenBy(u => u.Name?.ToString())
                        .ToList();

                    // Check if anything changed
                    var hasChanges = !usings.SequenceEqual(sortedUsings);
                    if (!hasChanges) continue;

                    filesWithChanges++;

                    // Create new compilation unit with sorted usings
                    var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(sortedUsings));
                    var newText = newRoot.ToFullString();

                    processedFiles.Add(new
                    {
                        filePath = document.FilePath,
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        usingCount = usings.Count,
                        preview
                    });

                    // Write to disk if not preview
                    if (!preview)
                    {
                        await File.WriteAllTextAsync(document.FilePath, newText);
                    }
                }
                catch (Exception ex)
                {
                    processedFiles.Add(new
                    {
                        filePath = document.FilePath,
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        error = ex.Message
                    });
                }
            }
        }

        return new
        {
            totalFilesScanned = totalFiles,
            filesWithChanges,
            filesProcessed = processedFiles.Count,
            preview,
            files = processedFiles,
            hint = preview
                ? $"Preview mode. Found {filesWithChanges} file(s) with changes. Set preview=false to apply."
                : $"Applied changes to {filesWithChanges} file(s)."
        };
    }

    public async Task<object> FormatDocumentBatchAsync(string? projectName, bool includeTests = true, bool preview = true)
    {
        EnsureSolutionLoaded();

        var projectsToProcess = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        var processedFiles = new List<object>();
        var totalFiles = 0;
        var filesFormatted = 0;

        foreach (var project in projectsToProcess)
        {
            // Filter out test projects if includeTests is false
            if (!includeTests && project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;
                totalFiles++;

                try
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync();

                    // Format the document using Roslyn's formatter
                    var formattedRoot = root.NormalizeWhitespace();
                    var formattedText = formattedRoot.ToFullString();

                    // Check if anything changed
                    var originalText = root.ToFullString();
                    var hasChanges = originalText != formattedText;

                    if (!hasChanges) continue;

                    filesFormatted++;

                    processedFiles.Add(new
                    {
                        filePath = document.FilePath,
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        preview
                    });

                    // Write to disk if not preview
                    if (!preview)
                    {
                        await File.WriteAllTextAsync(document.FilePath, formattedText);
                    }
                }
                catch (Exception ex)
                {
                    processedFiles.Add(new
                    {
                        filePath = document.FilePath,
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        error = ex.Message
                    });
                }
            }
        }

        return new
        {
            totalFilesScanned = totalFiles,
            filesFormatted,
            filesProcessed = processedFiles.Count,
            preview,
            files = processedFiles,
            hint = preview
                ? $"Preview mode. Found {filesFormatted} file(s) needing formatting. Set preview=false to apply."
                : $"Formatted {filesFormatted} file(s)."
        };
    }

    public async Task<object> GetMethodOverloadsAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
            throw new Exception("No symbol found at position");

        if (symbol is not IMethodSymbol methodSymbol)
        {
            return new
            {
                error = "Not a method symbol",
                actualKind = symbol.Kind.ToString(),
                symbolName = symbol.Name,
                hint = "This tool requires a method symbol. Place cursor on a method declaration or method name."
            };
        }

        // Get all members of the containing type with the same name
        var containingType = methodSymbol.ContainingType;
        var overloads = containingType.GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var overloadList = overloads.Select(m =>
        {
            var location = m.Locations.FirstOrDefault(loc => loc.IsInSource);
            var lineSpan = location?.GetLineSpan();

            return new
            {
                signature = m.ToDisplayString(),
                parameters = m.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.Type.ToDisplayString(),
                    isOptional = p.IsOptional,
                    defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
                }).ToList(),
                returnType = m.ReturnType.ToDisplayString(),
                isAsync = m.IsAsync,
                isStatic = m.IsStatic,
                location = lineSpan != null ? new
                {
                    filePath = lineSpan.Value.Path,
                    line = lineSpan.Value.StartLinePosition.Line,
                    column = lineSpan.Value.StartLinePosition.Character
                } : null
            };
        }).ToList();

        return new
        {
            methodName = methodSymbol.Name,
            overloadCount = overloadList.Count,
            overloads = overloadList
        };
    }

    public async Task<object> GetContainingMemberAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);

        // Walk up the syntax tree to find the containing member
        var memberNode = token.Parent?.AncestorsAndSelf().FirstOrDefault(n =>
            n is MethodDeclarationSyntax or
            PropertyDeclarationSyntax or
            ConstructorDeclarationSyntax or
            ClassDeclarationSyntax or
            StructDeclarationSyntax or
            InterfaceDeclarationSyntax);

        if (memberNode == null)
            return new { message = "No containing member found" };

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var symbol = semanticModel.GetDeclaredSymbol(memberNode);
        if (symbol == null)
            return new { message = "Could not resolve symbol for containing member" };

        var span = memberNode.Span;
        var lineSpan = syntaxTree.GetLineSpan(span);

        return new
        {
            memberName = symbol.Name,
            memberKind = symbol.Kind.ToString(),
            containingType = symbol.ContainingType?.ToDisplayString(),
            signature = symbol.ToDisplayString(),
            span = new
            {
                startLine = lineSpan.StartLinePosition.Line,
                startColumn = lineSpan.StartLinePosition.Character,
                endLine = lineSpan.EndLinePosition.Line,
                endColumn = lineSpan.EndLinePosition.Character
            }
        };
    }

    public async Task<object> FindCallersAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 100; // Default to 100

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
            throw new Exception("No symbol found at position");

        // Find callers works best for methods, properties, and constructors
        if (symbol is not (IMethodSymbol or IPropertySymbol))
        {
            return new
            {
                error = "Not a callable symbol",
                actualKind = symbol.Kind.ToString(),
                symbolName = symbol.Name,
                hint = "This tool works for methods, properties, and constructors. Place cursor on a method or property."
            };
        }

        var callers = await SymbolFinder.FindCallersAsync(symbol, _solution!);

        // First count total
        var totalCallers = 0;
        foreach (var caller in callers)
        {
            totalCallers += caller.Locations.Count(loc => loc.IsInSource);
        }

        var callerList = new List<object>();
        foreach (var caller in callers)
        {
            var callingSymbol = caller.CallingSymbol;
            var locations = caller.Locations;

            foreach (var location in locations.Where(loc => loc.IsInSource))
            {
                if (callerList.Count >= maxResultsToReturn)
                    break; // Stop at limit

                if (location.SourceTree == null) continue;

                var callerDocument = _solution!.GetDocument(location.SourceTree);
                if (callerDocument == null) continue;

                var lineSpan = location.GetLineSpan();
                var text = location.SourceTree.GetText();
                var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

                callerList.Add(new
                {
                    callingSymbol = new
                    {
                        name = callingSymbol.Name,
                        kind = callingSymbol.Kind.ToString(),
                        containingType = callingSymbol.ContainingType?.ToDisplayString(),
                        signature = callingSymbol.ToDisplayString()
                    },
                    location = new
                    {
                        filePath = callerDocument.FilePath,
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character,
                        lineText
                    }
                });
            }

            if (callerList.Count >= maxResultsToReturn)
                break; // Stop outer loop too
        }

        return new
        {
            symbolName = symbol.Name,
            symbolKind = symbol.Kind.ToString(),
            symbolSignature = symbol.ToDisplayString(),
            totalCallers,
            callersShown = callerList.Count,
            truncated = totalCallers > callerList.Count,
            callers = callerList,
            hint = totalCallers > callerList.Count
                ? $"Showing first {callerList.Count} of {totalCallers} call sites. Use maxResults parameter to see more."
                : null
        };
    }

    public async Task<object> FindUnusedCodeAsync(
        string? projectName,
        bool includePrivate,
        bool includeInternal,
        string? symbolKindFilter = null,
        int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var unusedSymbols = new List<object>();
        var maxResultsToReturn = maxResults ?? 50; // Default to 50 to prevent huge outputs

        var projectsToAnalyze = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        // Track counts by kind for summary
        var countByKind = new Dictionary<string, int>();

        foreach (var project in projectsToAnalyze)
        {
            if (unusedSymbols.Count >= maxResultsToReturn)
                break; // Stop analyzing if we hit the limit

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // Check if we should analyze types
            var shouldAnalyzeTypes = string.IsNullOrEmpty(symbolKindFilter) ||
                                     symbolKindFilter.Equals("Class", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Interface", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Struct", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Type", StringComparison.OrdinalIgnoreCase);

            if (shouldAnalyzeTypes)
            {
                // Get all named type symbols (classes, interfaces, structs, enums)
                var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>();

                foreach (var typeSymbol in allTypes)
                {
                    if (unusedSymbols.Count >= maxResultsToReturn)
                        break;

                    // Skip compiler-generated, extern, and types not in source
                    if (typeSymbol.IsImplicitlyDeclared ||
                        typeSymbol.IsExtern ||
                        !typeSymbol.Locations.Any(loc => loc.IsInSource))
                        continue;

                    // Filter by accessibility
                    if (!includePrivate && typeSymbol.DeclaredAccessibility == Accessibility.Private)
                        continue;
                    if (!includeInternal && typeSymbol.DeclaredAccessibility == Accessibility.Internal)
                        continue;

                    // Skip classes that implement framework interfaces (likely used via DI or framework)
                    if (ImplementsFrameworkInterface(typeSymbol))
                        continue;

                    // Skip classes with framework attributes (controllers, hosted services, etc.)
                    if (HasFrameworkAttribute(typeSymbol))
                        continue;

                    // Find references to this type
                    var references = await SymbolFinder.FindReferencesAsync(typeSymbol, _solution!);
                    var referenceCount = references.SelectMany(r => r.Locations).Count();

                    // For types, also check if any members are referenced
                    // This handles static classes where the class itself isn't referenced
                    // but its static methods/properties are called
                    var hasReferencedMembers = false;
                    if (referenceCount <= 1) // Type itself has no references
                    {
                        // Check if any public/internal members are referenced
                        foreach (var member in typeSymbol.GetMembers())
                        {
                            // Skip constructors, compiler-generated, and special members
                            if (member.IsImplicitlyDeclared ||
                                member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
                                continue;

                            var memberRefs = await SymbolFinder.FindReferencesAsync(member, _solution!);
                            var memberRefCount = memberRefs.SelectMany(r => r.Locations).Count();

                            if (memberRefCount > 1) // Member is referenced (beyond its declaration)
                            {
                                hasReferencedMembers = true;
                                break; // No need to check other members
                            }
                        }
                    }

                    // If no references to type AND no references to any members, it's unused
                    if (referenceCount <= 1 && !hasReferencedMembers) // 1 = just the declaration
                    {
                        var location = typeSymbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                        if (location != null)
                        {
                            var lineSpan = location.GetLineSpan();
                            var kind = typeSymbol.TypeKind.ToString();

                            countByKind[kind] = countByKind.GetValueOrDefault(kind) + 1;

                            unusedSymbols.Add(new
                            {
                                name = typeSymbol.Name,
                                fullyQualifiedName = typeSymbol.ToDisplayString(),
                                kind,
                                accessibility = typeSymbol.DeclaredAccessibility.ToString(),
                                filePath = lineSpan.Path,
                                line = lineSpan.StartLinePosition.Line,
                                column = lineSpan.StartLinePosition.Character
                            });
                        }
                    }
                }
            }

            // Check if we should analyze members
            var shouldAnalyzeMembers = string.IsNullOrEmpty(symbolKindFilter) ||
                                       symbolKindFilter.Equals("Method", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Property", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Field", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Member", StringComparison.OrdinalIgnoreCase);

            if (shouldAnalyzeMembers && unusedSymbols.Count < maxResultsToReturn)
            {
                // Also check methods, properties, and fields
                var allMembers = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Member);

                foreach (var member in allMembers)
                {
                    if (unusedSymbols.Count >= maxResultsToReturn)
                        break;

                    if (member is not (IMethodSymbol or IPropertySymbol or IFieldSymbol))
                        continue;

                    // Skip compiler-generated, extern, and symbols not in source
                    if (member.IsImplicitlyDeclared ||
                        member.IsExtern ||
                        !member.Locations.Any(loc => loc.IsInSource))
                        continue;

                    // Skip special methods (constructors, operators, etc.)
                    if (member is IMethodSymbol method &&
                        (method.MethodKind != MethodKind.Ordinary || method.IsOverride || method.IsVirtual))
                        continue;

                    // Filter by accessibility
                    if (!includePrivate && member.DeclaredAccessibility == Accessibility.Private)
                        continue;
                    if (!includeInternal && member.DeclaredAccessibility == Accessibility.Internal)
                        continue;

                    // Find references
                    var references = await SymbolFinder.FindReferencesAsync(member, _solution!);
                    var referenceCount = references.SelectMany(r => r.Locations).Count();

                    if (referenceCount <= 1)
                    {
                        var location = member.Locations.FirstOrDefault(loc => loc.IsInSource);
                        if (location != null)
                        {
                            var lineSpan = location.GetLineSpan();
                            var kind = member.Kind.ToString();

                            countByKind[kind] = countByKind.GetValueOrDefault(kind) + 1;

                            unusedSymbols.Add(new
                            {
                                name = member.Name,
                                fullyQualifiedName = member.ToDisplayString(),
                                kind,
                                accessibility = member.DeclaredAccessibility.ToString(),
                                containingType = member.ContainingType?.ToDisplayString(),
                                filePath = lineSpan.Path,
                                line = lineSpan.StartLinePosition.Line,
                                column = lineSpan.StartLinePosition.Character
                            });
                        }
                    }
                }
            }
        }

        var truncated = unusedSymbols.Count >= maxResultsToReturn;

        return new
        {
            projectName = projectName ?? "All projects",
            totalUnused = unusedSymbols.Count,
            unusedSymbols = unusedSymbols.ToList(),
            truncated,
            maxResults = maxResultsToReturn,
            countByKind,
            hint = truncated ? $"Results limited to {maxResultsToReturn}. Use maxResults parameter to show more, or symbolKindFilter to focus on specific types (Class, Method, Property, Field)." : null
        };
    }

    public async Task<object> RenameSymbolAsync(
        string filePath,
        int line,
        int column,
        string newName,
        bool preview,
        int? maxFiles = null,
        string? verbosity = null)
    {
        EnsureSolutionLoaded();

        var maxFilesToShow = maxFiles ?? 20; // Default to 20 files to prevent huge outputs
        var verbosityLevel = verbosity?.ToLower() ?? "summary"; // Default to summary to prevent token explosions

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);

        // Try to find symbol with improved logic and tolerance
        var (symbol, debugInfo) = TryFindSymbolForRename(syntaxTree, semanticModel, position, line, column);

        if (symbol == null)
        {
            return new
            {
                error = "No symbol found at position",
                line,
                column,
                debug = debugInfo,
                hint = "Ensure cursor is on a symbol name (class, method, variable, etc.). Try adjusting the column position by ±1."
            };
        }

        // Validate new name
        if (string.IsNullOrWhiteSpace(newName))
            throw new Exception("New name cannot be empty");

        // Check if symbol can be renamed (not extern, not from metadata)
        if (symbol.Locations.All(loc => !loc.IsInSource))
        {
            return new
            {
                error = "Cannot rename symbol",
                reason = "Symbol is defined in metadata (external library), not in source code",
                symbolName = symbol.Name
            };
        }

        // Perform rename
        var renameOptions = new Microsoft.CodeAnalysis.Rename.SymbolRenameOptions();
        var newSolution = await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(
            _solution!,
            symbol,
            renameOptions,
            newName);

        // Get all changes
        var changes = new List<object>();
        var solutionChanges = newSolution.GetChanges(_solution!);

        var totalFiles = 0;
        var totalChanges = 0;

        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            foreach (var changedDocumentId in projectChanges.GetChangedDocuments())
            {
                totalFiles++;

                var oldDocument = _solution!.GetDocument(changedDocumentId);
                var newDocument = newSolution.GetDocument(changedDocumentId);

                if (oldDocument == null || newDocument == null)
                    continue;

                var oldText = await oldDocument.GetTextAsync();
                var newText = await newDocument.GetTextAsync();

                var textChanges = newText.GetTextChanges(oldText);
                totalChanges += textChanges.Count();

                // Only include detailed changes for first N files
                if (changes.Count < maxFilesToShow)
                {
                    if (verbosityLevel == "summary")
                    {
                        // Summary: Just file path and count
                        changes.Add(new
                        {
                            filePath = oldDocument.FilePath,
                            changeCount = textChanges.Count()
                        });
                    }
                    else if (verbosityLevel == "compact")
                    {
                        // Compact: Include change locations but no text
                        var documentChanges = new List<object>();
                        foreach (var textChange in textChanges.Take(20))
                        {
                            var lineSpan = oldText.Lines.GetLinePositionSpan(textChange.Span);
                            documentChanges.Add(new
                            {
                                line = lineSpan.Start.Line,
                                column = lineSpan.Start.Character
                            });
                        }

                        changes.Add(new
                        {
                            filePath = oldDocument.FilePath,
                            changeCount = textChanges.Count(),
                            changes = documentChanges,
                            truncated = textChanges.Count() > 20
                        });
                    }
                    else // "full" or any other value
                    {
                        // Full: Include old/new text for each change
                        var documentChanges = new List<object>();
                        foreach (var textChange in textChanges.Take(20))
                        {
                            var lineSpan = oldText.Lines.GetLinePositionSpan(textChange.Span);
                            documentChanges.Add(new
                            {
                                startLine = lineSpan.Start.Line,
                                startColumn = lineSpan.Start.Character,
                                endLine = lineSpan.End.Line,
                                endColumn = lineSpan.End.Character,
                                oldText = textChange.Span.Length > 0 ? oldText.ToString(textChange.Span) : "",
                                newText = textChange.NewText
                            });
                        }

                        changes.Add(new
                        {
                            filePath = oldDocument.FilePath,
                            changeCount = textChanges.Count(),
                            changes = documentChanges,
                            truncated = textChanges.Count() > 20
                        });
                    }
                }
            }
        }

        var filesShown = changes.Count;
        var filesHidden = totalFiles - filesShown;

        // If preview mode, just return the changes
        if (preview)
        {
            var verbosityHint = verbosityLevel == "summary"
                ? "Using verbosity='summary' (file paths + counts only). Use verbosity='compact' for locations or verbosity='full' for detailed text changes."
                : verbosityLevel == "compact"
                    ? "Using verbosity='compact' (locations only). Use verbosity='full' to see old/new text for each change."
                    : null;

            var hints = new List<string>();
            if (filesHidden > 0)
                hints.Add($"Showing first {maxFilesToShow} files. {filesHidden} more files will be changed. Use maxFiles parameter to see more.");
            if (verbosityHint != null)
                hints.Add(verbosityHint);
            if (hints.Count == 0)
                hints.Add("Set preview=false to apply these changes.");

            return new
            {
                symbolName = symbol.Name,
                symbolKind = symbol.Kind.ToString(),
                newName,
                totalFiles,
                totalChanges,
                filesShown,
                filesHidden,
                verbosity = verbosityLevel,
                changes,
                preview = true,
                applied = false,
                truncated = filesHidden > 0,
                hint = string.Join(" ", hints)
            };
        }

        // Apply changes by updating the solution
        _solution = newSolution;

        // Write changes to disk
        var workspace = _workspace!;
        if (workspace.TryApplyChanges(newSolution))
        {
            return new
            {
                symbolName = symbol.Name,
                symbolKind = symbol.Kind.ToString(),
                newName,
                totalFiles,
                totalChanges,
                filesShown,
                filesHidden,
                changes,
                preview = false,
                applied = true,
                message = $"Rename applied successfully. {totalChanges} changes written to {totalFiles} files."
            };
        }
        else
        {
            return new
            {
                error = "Failed to apply changes",
                reason = "Workspace.TryApplyChanges returned false. Changes may conflict with current workspace state.",
                symbolName = symbol.Name,
                newName,
                totalFiles,
                totalChanges,
                applied = false
            };
        }
    }

    public Task<object> GetDependencyGraphAsync(string? format)
    {
        EnsureSolutionLoaded();

        var projectGraph = new Dictionary<string, List<string>>();
        var allProjects = _solution!.Projects.ToList();

        // Build dependency graph
        foreach (var project in allProjects)
        {
            var dependencies = project.ProjectReferences
                .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name)
                .Where(name => name != null)
                .Cast<string>()
                .ToList();

            projectGraph[project.Name] = dependencies;
        }

        // Detect cycles
        var cycles = DetectCycles(projectGraph);

        // Generate output based on format
        if (format?.ToLower() == "mermaid")
        {
            var mermaid = GenerateMermaidGraph(projectGraph);
            return Task.FromResult<object>(new
            {
                format = "mermaid",
                graph = mermaid,
                projectCount = allProjects.Count,
                hasCycles = cycles.Count > 0,
                cycles
            });
        }

        // Default: return structured data
        return Task.FromResult<object>(new
        {
            projectCount = allProjects.Count,
            dependencies = projectGraph,
            hasCycles = cycles.Count > 0,
            cycles
        });
    }

    public async Task<object> ExtractInterfaceAsync(string filePath, int line, int column, string interfaceName, List<string>? includeMemberNames)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            throw new Exception("Could not get semantic model");

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
            throw new Exception("No symbol found at position");

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return new
            {
                error = "Not a type symbol",
                actualKind = symbol?.Kind.ToString() ?? "Unknown",
                hint = "Place cursor on a class or struct to extract an interface"
            };
        }

        // Get public members
        var members = typeSymbol.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            .Where(m => m is IMethodSymbol or IPropertySymbol or IEventSymbol)
            .Where(m => !m.IsStatic)
            .Where(m => m is not IMethodSymbol method || method.MethodKind == MethodKind.Ordinary)
            .ToList();

        // Filter by included names if specified
        if (includeMemberNames != null && includeMemberNames.Count > 0)
        {
            members = members.Where(m => includeMemberNames.Contains(m.Name)).ToList();
        }

        // Generate interface code
        var interfaceCode = GenerateInterfaceCode(interfaceName, members, typeSymbol.ContainingNamespace);

        return new
        {
            className = typeSymbol.Name,
            interfaceName,
            memberCount = members.Count,
            members = members.Select(m => new
            {
                name = m.Name,
                kind = m.Kind.ToString(),
                signature = m.ToDisplayString()
            }).ToList(),
            interfaceCode,
            suggestedFileName = $"{interfaceName}.cs"
        };
    }

    // Helper methods

    private List<List<string>> DetectCycles(Dictionary<string, List<string>> graph)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                DetectCyclesHelper(node, graph, visited, recursionStack, new List<string>(), cycles);
            }
        }

        return cycles;
    }

    private void DetectCyclesHelper(string node, Dictionary<string, List<string>> graph,
        HashSet<string> visited, HashSet<string> recursionStack, List<string> currentPath, List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        currentPath.Add(node);

        if (graph.ContainsKey(node))
        {
            foreach (var neighbor in graph[node])
            {
                if (!visited.Contains(neighbor))
                {
                    DetectCyclesHelper(neighbor, graph, visited, recursionStack, currentPath, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStart = currentPath.IndexOf(neighbor);
                    var cycle = currentPath.Skip(cycleStart).ToList();
                    cycle.Add(neighbor); // Complete the cycle
                    cycles.Add(cycle);
                }
            }
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        recursionStack.Remove(node);
    }

    private string GenerateMermaidGraph(Dictionary<string, List<string>> graph)
    {
        var lines = new List<string> { "graph TD" };

        foreach (var kvp in graph)
        {
            var project = kvp.Key;
            var dependencies = kvp.Value;

            if (dependencies.Count == 0)
            {
                // Standalone project
                lines.Add($"  {SanitizeMermaidId(project)}[\"{project}\"]");
            }
            else
            {
                foreach (var dependency in dependencies)
                {
                    lines.Add($"  {SanitizeMermaidId(project)}[\"{project}\"] --> {SanitizeMermaidId(dependency)}[\"{dependency}\"]");
                }
            }
        }

        return string.Join("\n", lines);
    }

    private string SanitizeMermaidId(string name)
    {
        // Replace characters that aren't valid in Mermaid IDs
        return name.Replace(".", "_").Replace("-", "_").Replace(" ", "_");
    }

    private string GenerateInterfaceCode(string interfaceName, List<ISymbol> members, INamespaceSymbol? containingNamespace)
    {
        var sb = new System.Text.StringBuilder();

        // Add namespace
        if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine($"namespace {containingNamespace.ToDisplayString()}");
            sb.AppendLine("{");
        }

        // Add interface declaration
        var indent = containingNamespace != null && !containingNamespace.IsGlobalNamespace ? "    " : "";
        sb.AppendLine($"{indent}public interface {interfaceName}");
        sb.AppendLine($"{indent}{{");

        // Add members
        foreach (var member in members)
        {
            if (member is IMethodSymbol method)
            {
                var returnType = method.ReturnType.ToDisplayString();
                var parameters = string.Join(", ", method.Parameters.Select(p =>
                    $"{p.Type.ToDisplayString()} {p.Name}"));
                sb.AppendLine($"{indent}    {returnType} {method.Name}({parameters});");
            }
            else if (member is IPropertySymbol property)
            {
                var propertyType = property.Type.ToDisplayString();
                var accessors = new List<string>();
                if (property.GetMethod != null) accessors.Add("get;");
                if (property.SetMethod != null) accessors.Add("set;");
                sb.AppendLine($"{indent}    {propertyType} {property.Name} {{ {string.Join(" ", accessors)} }}");
            }
            else if (member is IEventSymbol eventSymbol)
            {
                var eventType = eventSymbol.Type.ToDisplayString();
                sb.AppendLine($"{indent}    event {eventType} {eventSymbol.Name};");
            }
        }

        sb.AppendLine($"{indent}}}");

        if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private void EnsureSolutionLoaded()
    {
        if (_solution == null)
        {
            throw new Exception("No solution loaded. Call roslyn:load_solution first or set DOTNET_SOLUTION_PATH environment variable.");
        }
    }

    private Task<Document> GetDocumentAsync(string filePath)
    {
        // Check cache
        if (_documentCache.TryGetValue(filePath, out var cached))
            return Task.FromResult(cached);

        // Find document in solution
        var document = _solution!.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath != null &&
                Path.GetFullPath(d.FilePath) == Path.GetFullPath(filePath));

        if (document == null)
            throw new FileNotFoundException($"Document not found in solution: {filePath}");

        // Cache it
        var enableCache = Environment.GetEnvironmentVariable("ROSLYN_ENABLE_SEMANTIC_CACHE") != "false";
        if (enableCache)
        {
            _documentCache[filePath] = document;
        }

        return Task.FromResult(document);
    }

    private int GetPosition(SyntaxTree syntaxTree, int line, int column)
    {
        var text = syntaxTree.GetText();
        var linePosition = new Microsoft.CodeAnalysis.Text.LinePosition(line, column);
        return text.Lines.GetPosition(linePosition);
    }

    private (ISymbol? symbol, object debugInfo) TryFindSymbolForRename(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int position,
        int line,
        int column)
    {
        var token = syntaxTree.GetRoot().FindToken(position);
        var debugInfo = new Dictionary<string, object>
        {
            ["requestedPosition"] = new { line, column },
            ["tokenFound"] = token.Text,
            ["tokenKind"] = token.Kind().ToString(),
            ["tokenSpan"] = new { start = token.SpanStart, end = token.Span.End }
        };

        // Strategy 1: Try current token's parent node
        var node = token.Parent;
        if (node != null)
        {
            debugInfo["nodeKind"] = node.Kind().ToString();
            debugInfo["nodeText"] = node.ToString().Length > 50 ? node.ToString().Substring(0, 50) + "..." : node.ToString();

            // Try GetDeclaredSymbol first (for declarations)
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                debugInfo["foundVia"] = "GetDeclaredSymbol on immediate node";
                return (symbol, debugInfo);
            }

            // Try GetSymbolInfo (for references)
            var symbolInfo = semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                debugInfo["foundVia"] = "GetSymbolInfo on immediate node";
                return (symbolInfo.Symbol, debugInfo);
            }
        }

        // Strategy 2: Walk up the tree to find a declaration or identifier node
        var currentNode = node;
        int walkCount = 0;
        while (currentNode != null && walkCount < 5)
        {
            walkCount++;

            // Check if this is a named declaration
            var declaredSymbol = semanticModel.GetDeclaredSymbol(currentNode);
            if (declaredSymbol != null)
            {
                debugInfo["foundVia"] = $"GetDeclaredSymbol after walking up {walkCount} levels";
                debugInfo["foundNodeKind"] = currentNode.Kind().ToString();
                return (declaredSymbol, debugInfo);
            }

            // Check symbol info
            var symbolInfo = semanticModel.GetSymbolInfo(currentNode);
            if (symbolInfo.Symbol != null)
            {
                debugInfo["foundVia"] = $"GetSymbolInfo after walking up {walkCount} levels";
                debugInfo["foundNodeKind"] = currentNode.Kind().ToString();
                return (symbolInfo.Symbol, debugInfo);
            }

            currentNode = currentNode.Parent;
        }

        // Strategy 3: Try positions ±1 character
        var text = syntaxTree.GetText();
        var positions = new[] { position - 1, position + 1 };

        foreach (var tryPos in positions)
        {
            if (tryPos < 0 || tryPos >= text.Length)
                continue;

            var tryToken = syntaxTree.GetRoot().FindToken(tryPos);
            var tryNode = tryToken.Parent;

            if (tryNode == null)
                continue;

            var symbol = semanticModel.GetDeclaredSymbol(tryNode) ??
                         semanticModel.GetSymbolInfo(tryNode).Symbol;

            if (symbol != null)
            {
                debugInfo["foundVia"] = $"Trying adjacent position {tryPos}";
                debugInfo["suggestedColumn"] = column + (tryPos - position);
                return (symbol, debugInfo);
            }
        }

        // No symbol found
        debugInfo["attemptedStrategies"] = new[]
        {
            "GetDeclaredSymbol on token.Parent",
            "GetSymbolInfo on token.Parent",
            "Walk up syntax tree (5 levels)",
            "Try positions ±1 character"
        };

        debugInfo["suggestion"] = token.Text.Length > 0
            ? $"Try positioning cursor at the start of '{token.Text}' (column {column - token.Span.Start})"
            : "Try positioning cursor on an identifier";

        return (null, debugInfo);
    }

    private Task<object> FormatSymbolInfoAsync(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
        var lineSpan = location?.GetLineSpan();

        var result = new Dictionary<string, object?>
        {
            ["name"] = symbol.Name,
            ["kind"] = symbol.Kind.ToString(),
            ["fullyQualifiedName"] = symbol.ToDisplayString(),
            ["containingType"] = symbol.ContainingType?.ToDisplayString(),
            ["containingNamespace"] = symbol.ContainingNamespace?.ToDisplayString(),
            ["assembly"] = symbol.ContainingAssembly?.Name,
            ["isStatic"] = symbol.IsStatic,
            ["isAbstract"] = symbol.IsAbstract,
            ["isVirtual"] = symbol.IsVirtual,
            ["accessibility"] = symbol.DeclaredAccessibility.ToString(),
            ["documentation"] = symbol.GetDocumentationCommentXml(),
        };

        if (lineSpan.HasValue)
        {
            result["location"] = new
            {
                filePath = lineSpan.Value.Path,
                line = lineSpan.Value.StartLinePosition.Line,
                column = lineSpan.Value.StartLinePosition.Character
            };
        }

        // Type-specific properties
        if (symbol is INamedTypeSymbol typeSymbol)
        {
            result["typeKind"] = typeSymbol.TypeKind.ToString();
            result["isGenericType"] = typeSymbol.IsGenericType;
            result["baseType"] = typeSymbol.BaseType?.Name;
            result["interfaces"] = typeSymbol.Interfaces.Select(i => i.Name).ToList();
        }
        else if (symbol is IMethodSymbol methodSymbol)
        {
            result["returnType"] = methodSymbol.ReturnType.ToDisplayString();
            result["parameters"] = methodSymbol.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type.ToDisplayString()
            }).ToList();
            result["isAsync"] = methodSymbol.IsAsync;
            result["isExtensionMethod"] = methodSymbol.IsExtensionMethod;
        }
        else if (symbol is IPropertySymbol propertySymbol)
        {
            result["propertyType"] = propertySymbol.Type.ToDisplayString();
            result["isReadOnly"] = propertySymbol.IsReadOnly;
            result["isWriteOnly"] = propertySymbol.IsWriteOnly;
        }
        else if (symbol is IFieldSymbol fieldSymbol)
        {
            result["fieldType"] = fieldSymbol.Type.ToDisplayString();
            result["isConst"] = fieldSymbol.IsConst;
            result["isReadOnly"] = fieldSymbol.IsReadOnly;
        }

        return Task.FromResult<object>(result);
    }

    public async Task<object> SemanticQueryAsync(
        List<string>? kinds,
        bool? isAsync,
        string? namespaceFilter,
        string? accessibility,
        bool? isStatic,
        string? type,
        string? returnType,
        List<string>? attributes,
        List<string>? parameterIncludes,
        List<string>? parameterExcludes,
        int? maxResults)
    {
        EnsureSolutionLoaded();

        var results = new List<object>();
        var maxResultsToReturn = maxResults ?? 100;

        // Statistics for summary
        var countByKind = new Dictionary<string, int>();

        foreach (var project in _solution!.Projects)
        {
            if (results.Count >= maxResultsToReturn)
                break;

            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // Get all symbols in the project
            var allSymbols = compilation.GetSymbolsWithName(_ => true, SymbolFilter.All);

            foreach (var symbol in allSymbols)
            {
                if (results.Count >= maxResultsToReturn)
                    break;

                // Skip compiler-generated
                if (symbol.IsImplicitlyDeclared || !symbol.Locations.Any(loc => loc.IsInSource))
                    continue;

                // Filter by kind
                if (kinds != null && kinds.Count > 0)
                {
                    bool kindMatches = false;

                    if (symbol is INamedTypeSymbol namedType)
                    {
                        // For type symbols, check TypeKind
                        kindMatches = kinds.Any(k => namedType.TypeKind.ToString().Equals(k, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        // For other symbols, check SymbolKind
                        kindMatches = kinds.Any(k => symbol.Kind.ToString().Equals(k, StringComparison.OrdinalIgnoreCase));
                    }

                    if (!kindMatches)
                        continue;
                }

                // Filter by namespace
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                    if (!MatchesGlobPattern(symbolNamespace, namespaceFilter))
                        continue;
                }

                // Filter by accessibility
                if (!string.IsNullOrEmpty(accessibility))
                {
                    if (!symbol.DeclaredAccessibility.ToString().Equals(accessibility, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Filter by static
                if (isStatic.HasValue && symbol.IsStatic != isStatic.Value)
                    continue;

                // Filter by attributes
                if (attributes != null && attributes.Count > 0)
                {
                    var symbolAttributes = symbol.GetAttributes();
                    bool hasAllAttributes = attributes.All(attrName =>
                        symbolAttributes.Any(attr =>
                            attr.AttributeClass?.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase) == true ||
                            attr.AttributeClass?.ToDisplayString().Equals(attrName, StringComparison.OrdinalIgnoreCase) == true));

                    if (!hasAllAttributes)
                        continue;
                }

                // Symbol-specific filtering
                bool matches = true;

                if (symbol is IMethodSymbol methodSymbol)
                {
                    // Filter by async
                    if (isAsync.HasValue && methodSymbol.IsAsync != isAsync.Value)
                        matches = false;

                    // Filter by return type
                    if (!string.IsNullOrEmpty(returnType) && !methodSymbol.ReturnType.ToDisplayString().Contains(returnType, StringComparison.OrdinalIgnoreCase))
                        matches = false;

                    // Filter by parameters
                    if (parameterIncludes != null && parameterIncludes.Count > 0)
                    {
                        var paramTypes = methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()).ToList();
                        bool hasAllIncludes = parameterIncludes.All(include =>
                            paramTypes.Any(pt => pt.Contains(include, StringComparison.OrdinalIgnoreCase)));

                        if (!hasAllIncludes)
                            matches = false;
                    }

                    if (parameterExcludes != null && parameterExcludes.Count > 0)
                    {
                        var paramTypes = methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()).ToList();
                        bool hasAnyExclude = parameterExcludes.Any(exclude =>
                            paramTypes.Any(pt => pt.Contains(exclude, StringComparison.OrdinalIgnoreCase)));

                        if (hasAnyExclude)
                            matches = false;
                    }
                }
                else if (symbol is IPropertySymbol propertySymbol)
                {
                    // Filter by type
                    if (!string.IsNullOrEmpty(type) && !propertySymbol.Type.ToDisplayString().Contains(type, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                }
                else if (symbol is IFieldSymbol fieldSymbol)
                {
                    // Filter by type
                    if (!string.IsNullOrEmpty(type) && !fieldSymbol.Type.ToDisplayString().Contains(type, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                }

                if (!matches)
                    continue;

                // Add to results
                var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                if (location == null) continue;

                var lineSpan = location.GetLineSpan();
                var symbolKind = symbol is INamedTypeSymbol nt ? nt.TypeKind.ToString() : symbol.Kind.ToString();

                countByKind[symbolKind] = countByKind.GetValueOrDefault(symbolKind) + 1;

                var result = new Dictionary<string, object>
                {
                    ["name"] = symbol.Name,
                    ["fullyQualifiedName"] = symbol.ToDisplayString(),
                    ["kind"] = symbolKind,
                    ["accessibility"] = symbol.DeclaredAccessibility.ToString(),
                    ["isStatic"] = symbol.IsStatic,
                    ["containingType"] = symbol.ContainingType?.ToDisplayString() ?? "",
                    ["containingNamespace"] = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                    ["location"] = new
                    {
                        filePath = lineSpan.Path,
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    }
                };

                // Add symbol-specific details
                if (symbol is IMethodSymbol ms)
                {
                    result["isAsync"] = ms.IsAsync;
                    result["returnType"] = ms.ReturnType.ToDisplayString();
                    result["parameters"] = ms.Parameters.Select(p => new
                    {
                        name = p.Name,
                        type = p.Type.ToDisplayString()
                    }).ToList();
                }
                else if (symbol is IPropertySymbol ps)
                {
                    result["propertyType"] = ps.Type.ToDisplayString();
                    result["isReadOnly"] = ps.IsReadOnly;
                }
                else if (symbol is IFieldSymbol fs)
                {
                    result["fieldType"] = fs.Type.ToDisplayString();
                    result["isConst"] = fs.IsConst;
                    result["isReadOnly"] = fs.IsReadOnly;
                }

                results.Add(result);
            }
        }

        return new
        {
            totalFound = results.Count,
            truncated = results.Count >= maxResultsToReturn,
            countByKind,
            results,
            hint = results.Count >= maxResultsToReturn
                ? $"Results truncated at {maxResultsToReturn}. Use maxResults parameter to see more."
                : null
        };
    }

    private object FormatTypeInfo(INamedTypeSymbol typeSymbol)
    {
        var location = typeSymbol.Locations.FirstOrDefault(loc => loc.IsInSource);
        var lineSpan = location?.GetLineSpan();

        return new
        {
            name = typeSymbol.ToDisplayString(),
            kind = typeSymbol.TypeKind.ToString(),
            isAbstract = typeSymbol.IsAbstract,
            location = lineSpan.HasValue ? new
            {
                filePath = lineSpan.Value.Path,
                line = lineSpan.Value.StartLinePosition.Line,
                column = lineSpan.Value.StartLinePosition.Character
            } : null
        };
    }

    /// <summary>
    /// Checks if a type implements common framework interfaces that indicate it's used by the framework
    /// (e.g., via Dependency Injection, hosted services, middleware, etc.)
    /// </summary>
    private bool ImplementsFrameworkInterface(INamedTypeSymbol typeSymbol)
    {
        // Common framework interfaces that indicate a class is used via DI or framework mechanisms
        var frameworkInterfaces = new[]
        {
            // ASP.NET Core / .NET Core
            "Microsoft.Extensions.Hosting.IHostedService",
            "Microsoft.Extensions.Hosting.BackgroundService",
            "Microsoft.AspNetCore.Mvc.Filters.IActionFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAsyncAuthorizationFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IResourceFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAsyncResourceFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAsyncExceptionFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IResultFilter",
            "Microsoft.AspNetCore.Mvc.Filters.IAsyncResultFilter",
            "Microsoft.AspNetCore.Builder.IMiddleware",
            "Microsoft.AspNetCore.Mvc.IActionResult",
            "Microsoft.AspNetCore.Mvc.IUrlHelper",
            "Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder",
            "Microsoft.AspNetCore.Mvc.ModelBinding.IValueProvider",

            // Entity Framework
            "Microsoft.EntityFrameworkCore.DbContext",
            "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration",

            // MediatR
            "MediatR.IRequestHandler",
            "MediatR.INotificationHandler",
            "MediatR.IPipelineBehavior",

            // FluentValidation
            "FluentValidation.IValidator",

            // AutoMapper
            "AutoMapper.Profile",

            // Generic patterns
            "System.IDisposable",
            "System.IAsyncDisposable"
        };

        // Check if type implements any of these interfaces
        var allInterfaces = typeSymbol.AllInterfaces;
        foreach (var iface in allInterfaces)
        {
            var fullName = iface.ToDisplayString();
            foreach (var frameworkInterface in frameworkInterfaces)
            {
                if (fullName.Contains(frameworkInterface, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Check base types (for abstract base classes like BackgroundService)
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            var baseTypeName = baseType.ToDisplayString();
            foreach (var frameworkInterface in frameworkInterfaces)
            {
                if (baseTypeName.Contains(frameworkInterface, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            baseType = baseType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Checks if a type has common framework attributes that indicate it's discovered/used by the framework
    /// </summary>
    private bool HasFrameworkAttribute(INamedTypeSymbol typeSymbol)
    {
        // Common framework attributes that indicate a class is discovered by the framework
        var frameworkAttributes = new[]
        {
            // ASP.NET Core
            "ApiController",
            "Controller",
            "Route",
            "Authorize",
            "ApiExplorerSettings",
            "ServiceFilter",
            "TypeFilter",

            // Testing frameworks
            "TestClass",
            "TestFixture",
            "Collection",
            "Trait",

            // Serialization
            "DataContract",
            "JsonConverter",
            "XmlRoot",

            // MEF / Composition
            "Export",
            "Import",
            "PartCreationPolicy"
        };

        var attributes = typeSymbol.GetAttributes();
        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeClass?.Name ?? "";
            foreach (var frameworkAttr in frameworkAttributes)
            {
                if (attributeName.Contains(frameworkAttr, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
