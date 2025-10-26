using Microsoft.CodeAnalysis;
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

    public RoslynService()
    {
        _maxDiagnostics = int.TryParse(Environment.GetEnvironmentVariable("ROSLYN_MAX_DIAGNOSTICS"), out var maxDiag)
            ? maxDiag : 100;
        _timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("ROSLYN_TIMEOUT_SECONDS"), out var timeout)
            ? timeout : 30;
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

    public async Task<object> FindReferencesAsync(string filePath, int line, int column)
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

        var references = await SymbolFinder.FindReferencesAsync(symbol, _solution!);
        var allLocations = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .ToList();

        var referenceList = new List<object>();
        foreach (var loc in allLocations)
        {
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
            totalReferences = referenceList.Count,
            references = referenceList
        };
    }

    public async Task<object> FindImplementationsAsync(string filePath, int line, int column)
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
            throw new Exception("Symbol is not a type");

        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, _solution!);

        var implementationList = new List<object>();
        foreach (var impl in implementations)
        {
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
            implementationCount = implementationList.Count,
            implementations = implementationList
        };
    }

    public async Task<object> GetTypeHierarchyAsync(string filePath, int line, int column)
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
            throw new Exception("Symbol is not a type");

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
        var derivedList = derivedTypes
            .Select(d => FormatTypeInfo(d))
            .ToList();

        return new
        {
            typeName = typeSymbol.ToDisplayString(),
            baseTypes,
            interfaces,
            derivedTypes = derivedList
        };
    }

    public async Task<object> SearchSymbolsAsync(string query, string? kind, int maxResults)
    {
        EnsureSolutionLoaded();

        var results = new List<object>();

        foreach (var project in _solution!.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var symbols = compilation.GetSymbolsWithName(
                name => name.Contains(query, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.All);

            foreach (var symbol in symbols)
            {
                if (!string.IsNullOrEmpty(kind) && symbol.Kind.ToString() != kind)
                    continue;

                var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                if (location == null) continue;

                var lineSpan = location.GetLineSpan();

                results.Add(new
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

                if (results.Count >= maxResults)
                    break;
            }

            if (results.Count >= maxResults)
                break;
        }

        return new
        {
            query,
            totalFound = results.Count,
            results
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

        var diagnostics = semanticModel.GetDiagnostics();
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            throw new Exception("Could not get syntax tree");

        var position = GetPosition(syntaxTree, line, column);
        var diagnostic = diagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            d.Location.SourceSpan.Contains(position));

        if (diagnostic == null)
        {
            return new
            {
                message = $"No diagnostic with ID {diagnosticId} found at position"
            };
        }

        return new
        {
            diagnosticId = diagnostic.Id,
            message = diagnostic.GetMessage(),
            severity = diagnostic.Severity.ToString(),
            availableFixes = new[] { "Code fix application requires additional infrastructure" }
        };
    }

    public async Task<object> GetProjectStructureAsync(bool includeReferences, bool includeDocuments)
    {
        EnsureSolutionLoaded();

        var projects = new List<object>();

        foreach (var project in _solution!.Projects)
        {
            var references = includeReferences
                ? project.MetadataReferences
                    .Select(r => r.Display ?? "Unknown")
                    .ToList()
                : null;

            var projectReferences = project.ProjectReferences
                .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name ?? "Unknown")
                .ToList();

            var documents = includeDocuments
                ? project.Documents
                    .Select(d => new
                    {
                        name = d.Name,
                        filePath = d.FilePath,
                        folders = d.Folders.ToList()
                    })
                    .ToList()
                : null;

            projects.Add(new
            {
                name = project.Name,
                filePath = project.FilePath,
                language = project.Language,
                outputPath = project.OutputFilePath,
                targetFramework = project.CompilationOptions?.Platform.ToString(),
                documentCount = project.DocumentIds.Count,
                references,
                projectReferences,
                documents
            });
        }

        return new
        {
            solutionPath = _solution!.FilePath,
            projectCount = projects.Count,
            projects
        };
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

        if (symbol is not IMethodSymbol methodSymbol)
            throw new Exception("Symbol is not a method");

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

    // Helper methods

    private void EnsureSolutionLoaded()
    {
        if (_solution == null)
        {
            throw new Exception("No solution loaded. Call roslyn:load_solution first or set DOTNET_SOLUTION_PATH environment variable.");
        }
    }

    private async Task<Document> GetDocumentAsync(string filePath)
    {
        // Check cache
        if (_documentCache.TryGetValue(filePath, out var cached))
            return cached;

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

        return document;
    }

    private int GetPosition(SyntaxTree syntaxTree, int line, int column)
    {
        var text = syntaxTree.GetText();
        var linePosition = new Microsoft.CodeAnalysis.Text.LinePosition(line, column);
        return text.Lines.GetPosition(linePosition);
    }

    private async Task<object> FormatSymbolInfoAsync(ISymbol symbol)
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

        return result;
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
}
