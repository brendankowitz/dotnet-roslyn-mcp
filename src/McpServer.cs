using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcp;

public class McpServer
{
    private readonly RoslynService _roslynService;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer()
    {
        _roslynService = new RoslynService();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task RunAsync()
    {
        await LogAsync("Information", "Roslyn MCP Server starting...");

        // Auto-load solution from environment variable
        var solutionPath = Environment.GetEnvironmentVariable("DOTNET_SOLUTION_PATH");
        if (!string.IsNullOrEmpty(solutionPath))
        {
            try
            {
                // If it's a directory, try to find a .sln file
                if (Directory.Exists(solutionPath))
                {
                    var slnFiles = Directory.GetFiles(solutionPath, "*.sln");
                    if (slnFiles.Length > 0)
                    {
                        solutionPath = slnFiles[0];
                    }
                }

                if (File.Exists(solutionPath))
                {
                    await LogAsync("Information", $"Auto-loading solution: {solutionPath}");
                    await _roslynService.LoadSolutionAsync(solutionPath);
                }
            }
            catch (Exception ex)
            {
                await LogAsync("Warning", $"Failed to auto-load solution: {ex.Message}");
            }
        }

        // Main event loop - read from stdin, write to stdout
        using var reader = Console.In;
        using var writer = Console.Out;

        while (true)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    await LogAsync("Information", "Received EOF on stdin, shutting down");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                await LogAsync("Debug", $"Received request: {line}");

                var response = await HandleRequestAsync(line);

                var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                await writer.WriteLineAsync(responseJson);
                await writer.FlushAsync();

                await LogAsync("Debug", $"Sent response: {responseJson}");
            }
            catch (Exception ex)
            {
                await LogAsync("Error", $"Error in main loop: {ex}");
            }
        }
    }

    private async Task<object> HandleRequestAsync(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<JsonObject>(requestJson);
            if (request == null)
            {
                return CreateErrorResponse(null, -32700, "Parse error");
            }

            var id = request["id"]?.GetValue<int>();
            var method = request["method"]?.GetValue<string>();
            var paramsNode = request["params"];

            if (string.IsNullOrEmpty(method))
            {
                return CreateErrorResponse(id, -32600, "Invalid Request: missing method");
            }

            return method switch
            {
                "initialize" => await HandleInitializeAsync(id),
                "tools/list" => await HandleListToolsAsync(id),
                "tools/call" => await HandleToolCallAsync(id, paramsNode?.AsObject()),
                _ => CreateErrorResponse(id, -32601, $"Method not found: {method}")
            };
        }
        catch (Exception ex)
        {
            await LogAsync("Error", $"Error handling request: {ex}");
            return CreateErrorResponse(null, -32603, $"Internal error: {ex.Message}");
        }
    }

    private Task<object> HandleInitializeAsync(int? id)
    {
        var response = CreateSuccessResponse(id, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "Roslyn MCP Server",
                version = "1.0.0"
            }
        });
        return Task.FromResult(response);
    }

    private Task<object> HandleListToolsAsync(int? id)
    {
        var tools = new List<object>
        {
            (object)new
            {
                name = "roslyn:health_check",
                description = "Check the health and status of the Roslyn MCP server and workspace",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            (object)new
            {
                name = "roslyn:load_solution",
                description = "Load a .NET solution for analysis. Returns success=true with projectCount and documentCount.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new { type = "string", description = "Absolute path to .sln file" }
                    },
                    required = new[] { "solutionPath" }
                }
            },
            (object)new
            {
                name = "roslyn:get_symbol_info",
                description = "Get detailed semantic information about a symbol at a specific position. IMPORTANT: Uses ZERO-BASED coordinates. If your editor shows 'Line 14, Column 5', pass line=13, column=4. Returns symbol kind, type, namespace, documentation, and location.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number (Visual Studio line 14 = line 13 here)" },
                        column = new { type = "integer", description = "Zero-based column number (Visual Studio col 5 = col 4 here)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:go_to_definition",
                description = "Fast navigation to symbol definition. Returns the definition location without finding all references. IMPORTANT: Uses ZERO-BASED coordinates (editor line 10 = pass line 9).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:find_references",
                description = "Find all references to a symbol across the entire solution. Returns file paths, line numbers, and code context for each reference. IMPORTANT: Uses ZERO-BASED coordinates (editor line 10 = pass line 9).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        maxResults = new { type = "integer", description = "Maximum number of references to return (default: 100). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:find_implementations",
                description = "Find all implementations of an interface or abstract class",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        maxResults = new { type = "integer", description = "Maximum number of implementations to return (default: 50). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:get_type_hierarchy",
                description = "Get the inheritance hierarchy (base types and derived types) for a type",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        maxDerivedTypes = new { type = "integer", description = "Maximum number of derived types to return (default: 50). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:search_symbols",
                description = "Search for types, methods, properties, etc. by name across the solution. Supports glob patterns (e.g., '*Handler' finds classes ending with 'Handler', 'Get*' finds symbols starting with 'Get'). Use ? for single character wildcard. PAGINATION: Returns totalCount and hasMore. Use offset to paginate through results.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search query - supports wildcards: * (any characters), ? (single character). Examples: 'Handler', '*Handler', 'Get*', 'I?Service'. Case-insensitive." },
                        kind = new { type = "string", description = "Optional: filter by symbol kind. For types use: Class, Interface, Struct, Enum, Delegate. For members use: Method, Property, Field, Event. Other: Namespace. Case-insensitive." },
                        maxResults = new { type = "integer", description = "Maximum number of results per page (default: 50)" },
                        namespaceFilter = new { type = "string", description = "Optional: filter by namespace (supports wildcards). Examples: 'MyApp.Core.*', '*.Services', 'MyApp.*.Handlers'. Case-insensitive." },
                        offset = new { type = "integer", description = "Offset for pagination (default: 0). Use pagination.nextOffset from previous response to get next page." }
                    },
                    required = new[] { "query" }
                }
            },
            (object)new
            {
                name = "roslyn:semantic_query",
                description = "Advanced semantic code query with multiple filters. Find symbols based on their semantic properties: async methods without CancellationToken, classes with specific attributes, fields/properties of specific types, etc. Returns detailed symbol information with statistics.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        kinds = new { type = "array", items = new { type = "string" }, description = "Optional: filter by symbol kinds (can specify multiple). For types: Class, Interface, Struct, Enum, Delegate. For members: Method, Property, Field, Event. Example: ['Class', 'Interface']" },
                        isAsync = new { type = "boolean", description = "Optional: filter methods by async/await (true for async methods, false for sync methods)" },
                        namespaceFilter = new { type = "string", description = "Optional: filter by namespace (supports wildcards). Examples: 'MyApp.Core.*', '*.Services'" },
                        accessibility = new { type = "string", description = "Optional: filter by accessibility. Values: Public, Private, Internal, Protected, ProtectedInternal, PrivateProtected" },
                        isStatic = new { type = "boolean", description = "Optional: filter by static modifier (true for static, false for instance)" },
                        type = new { type = "string", description = "Optional: filter fields/properties by their type. Partial match. Example: 'ILogger' finds all ILogger fields/properties" },
                        returnType = new { type = "string", description = "Optional: filter methods by return type. Partial match. Example: 'Task' finds all methods returning Task" },
                        attributes = new { type = "array", items = new { type = "string" }, description = "Optional: filter by attributes (must have ALL specified). Example: ['ObsoleteAttribute', 'EditorBrowsableAttribute']" },
                        parameterIncludes = new { type = "array", items = new { type = "string" }, description = "Optional: filter methods that MUST have these parameter types (partial match). Example: ['CancellationToken']" },
                        parameterExcludes = new { type = "array", items = new { type = "string" }, description = "Optional: filter methods that must NOT have these parameter types (partial match). Example: ['CancellationToken']" },
                        maxResults = new { type = "integer", description = "Maximum number of results (default: 100)" }
                    },
                    required = new string[] { }
                }
            },
            (object)new
            {
                name = "roslyn:get_diagnostics",
                description = "Get compiler errors, warnings, and info messages for a file or entire project",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Optional: path to specific file, omit for all files" },
                        projectPath = new { type = "string", description = "Optional: path to specific project" },
                        severity = new { type = "string", description = "Optional: filter by severity (Error, Warning, Info)" },
                        includeHidden = new { type = "boolean", description = "Include hidden diagnostics (default: false)" }
                    }
                }
            },
            (object)new
            {
                name = "roslyn:get_code_fixes",
                description = "Get available code fixes for a specific diagnostic",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        diagnosticId = new { type = "string", description = "Diagnostic ID (e.g., CS0246)" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "diagnosticId", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:apply_code_fix",
                description = "Apply automated code fix for a diagnostic. WORKFLOW: (1) Call with no fixIndex to list available fixes, (2) Call with fixIndex and preview=true to preview changes, (3) Call with preview=false to apply. IMPORTANT: Uses ZERO-BASED coordinates.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        diagnosticId = new { type = "string", description = "Diagnostic ID (e.g., CS0168, CS1998, CS4012)" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        fixIndex = new { type = "integer", description = "Index of fix to apply (omit to list available fixes). Call without this parameter first to see available fixes." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new[] { "filePath", "diagnosticId", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:get_project_structure",
                description = "Get solution/project structure. IMPORTANT: For large solutions (100+ projects), use summaryOnly=true or projectNamePattern to avoid token limit errors. Maximum output is limited to 25,000 tokens.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeReferences = new { type = "boolean", description = "Include package references (default: true, limited to 100 per project)" },
                        includeDocuments = new { type = "boolean", description = "Include document lists (default: false, limited to 500 per project)" },
                        projectNamePattern = new { type = "string", description = "Filter projects by name pattern (supports * and ? wildcards, e.g., '*.Application' or 'MyApp.*')" },
                        maxProjects = new { type = "integer", description = "Maximum number of projects to return (e.g., 10 for large solutions)" },
                        summaryOnly = new { type = "boolean", description = "Return only project names and counts (default: false, recommended for large solutions)" }
                    }
                }
            },
            (object)new
            {
                name = "roslyn:organize_usings",
                description = "Sort and remove unused using directives in a file",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" }
                    },
                    required = new[] { "filePath" }
                }
            },
            (object)new
            {
                name = "roslyn:organize_usings_batch",
                description = "Organize using directives for multiple files in a project. Supports file pattern filtering (e.g., '*.cs', 'Services/*.cs'). PREVIEW mode by default - set preview=false to apply changes.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: Project name to process. If omitted, processes all projects in solution." },
                        filePattern = new { type = "string", description = "Optional: Glob pattern to filter files (e.g., '*.cs', 'Services/*.cs', '*Repository.cs'). Matches against file names, not full paths." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new string[] { }
                }
            },
            (object)new
            {
                name = "roslyn:format_document_batch",
                description = "Format multiple documents in a project using Roslyn's NormalizeWhitespace. Ensures consistent indentation, spacing, and line breaks. PREVIEW mode by default - set preview=false to apply changes.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: Project name to format. If omitted, formats all projects in solution." },
                        includeTests = new { type = "boolean", description = "Include test projects (default: true). Set to false to skip projects with 'Test' in the name." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new string[] { }
                }
            },
            (object)new
            {
                name = "roslyn:get_method_overloads",
                description = "Get all overloads of a method",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:get_containing_member",
                description = "Get information about the containing method/property/class at a position",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:find_callers",
                description = "Find all methods/properties that call or reference a specific symbol (inverse of find_references). Essential for impact analysis: 'If I change this method, what code will be affected?' IMPORTANT: Uses ZERO-BASED coordinates.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        maxResults = new { type = "integer", description = "Maximum number of call sites to return (default: 100). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            (object)new
            {
                name = "roslyn:find_unused_code",
                description = "Find unused types, methods, properties, and fields in a project or entire solution. Returns symbols with zero references (excluding their declaration). Default limit: 50 results. Use maxResults to see more.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: analyze specific project by name, omit to analyze entire solution" },
                        includePrivate = new { type = "boolean", description = "Include private members (default: true)" },
                        includeInternal = new { type = "boolean", description = "Include internal members (default: false - usually want to keep internal APIs)" },
                        symbolKindFilter = new { type = "string", description = "Optional: filter by symbol kind (Class, Method, Property, Field)" },
                        maxResults = new { type = "integer", description = "Maximum results to return (default: 50, helps manage large outputs)" }
                    }
                }
            },
            (object)new
            {
                name = "roslyn:rename_symbol",
                description = "Safely rename a symbol (type, method, property, etc.) across the entire solution. Uses Roslyn's semantic analysis to ensure all references are updated. SUPPORTS PREVIEW MODE - always preview first! IMPORTANT: Uses ZERO-BASED coordinates. Default shows first 20 files with summary verbosity.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        newName = new { type = "string", description = "New name for the symbol" },
                        preview = new { type = "boolean", description = "Preview changes without applying (default: true). ALWAYS preview first!" },
                        maxFiles = new { type = "integer", description = "Max files to show in preview (default: 20, prevents large outputs)" },
                        verbosity = new { type = "string", description = "Output detail level: 'summary' (default, file paths + counts only ~200 tokens/file), 'compact' (add locations ~500 tokens/file), 'full' (include old/new text ~3000+ tokens/file)" }
                    },
                    required = new[] { "filePath", "line", "column", "newName" }
                }
            },
            (object)new
            {
                name = "roslyn:extract_interface",
                description = "Generate an interface from a class or struct. Extracts all public instance members (methods, properties, events). Useful for dependency injection and testability. IMPORTANT: Uses ZERO-BASED coordinates.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the class" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        interfaceName = new { type = "string", description = "Name for the new interface (e.g., 'IMyService')" },
                        includeMemberNames = new { type = "array", items = new { type = "string" }, description = "Optional: specific member names to include (omit to include all public members)" }
                    },
                    required = new[] { "filePath", "line", "column", "interfaceName" }
                }
            },
            (object)new
            {
                name = "roslyn:dependency_graph",
                description = "Visualize project dependencies as a graph. Shows which projects reference which, detects circular dependencies. Can output as Mermaid diagram for visualization.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        format = new { type = "string", description = "Output format: 'json' (default) returns structured data, 'mermaid' returns Mermaid diagram syntax" }
                    }
                }
            }
        };

        return Task.FromResult(CreateSuccessResponse(id, new { tools }));
    }

    private async Task<object> HandleToolCallAsync(int? id, JsonObject? paramsNode)
    {
        try
        {
            var name = paramsNode?["name"]?.GetValue<string>();
            var arguments = paramsNode?["arguments"]?.AsObject();

            if (string.IsNullOrEmpty(name))
            {
                return CreateErrorResponse(id, -32602, "Invalid params: missing tool name");
            }

            var result = name switch
            {
                "roslyn:health_check" => await _roslynService.GetHealthCheckAsync(),

                "roslyn:load_solution" => await _roslynService.LoadSolutionAsync(
                    arguments?["solutionPath"]?.GetValue<string>() ?? throw new Exception("solutionPath required")),

                "roslyn:get_symbol_info" => await _roslynService.GetSymbolInfoAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:go_to_definition" => await _roslynService.GoToDefinitionAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:find_references" => await _roslynService.FindReferencesAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["maxResults"]?.GetValue<int>()),

                "roslyn:find_implementations" => await _roslynService.FindImplementationsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["maxResults"]?.GetValue<int>()),

                "roslyn:get_type_hierarchy" => await _roslynService.GetTypeHierarchyAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["maxDerivedTypes"]?.GetValue<int>()),

                "roslyn:search_symbols" => await _roslynService.SearchSymbolsAsync(
                    arguments?["query"]?.GetValue<string>() ?? throw new Exception("query required"),
                    arguments?["kind"]?.GetValue<string>(),
                    arguments?["maxResults"]?.GetValue<int>() ?? 50,
                    arguments?["namespaceFilter"]?.GetValue<string>(),
                    arguments?["offset"]?.GetValue<int>() ?? 0),

                "roslyn:semantic_query" => await _roslynService.SemanticQueryAsync(
                    arguments?["kinds"]?.AsArray()?.Select(e => e?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    arguments?["isAsync"]?.GetValue<bool?>(),
                    arguments?["namespaceFilter"]?.GetValue<string>(),
                    arguments?["accessibility"]?.GetValue<string>(),
                    arguments?["isStatic"]?.GetValue<bool?>(),
                    arguments?["type"]?.GetValue<string>(),
                    arguments?["returnType"]?.GetValue<string>(),
                    arguments?["attributes"]?.AsArray()?.Select(e => e?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    arguments?["parameterIncludes"]?.AsArray()?.Select(e => e?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    arguments?["parameterExcludes"]?.AsArray()?.Select(e => e?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    arguments?["maxResults"]?.GetValue<int>()),

                "roslyn:get_diagnostics" => await _roslynService.GetDiagnosticsAsync(
                    arguments?["filePath"]?.GetValue<string>(),
                    arguments?["projectPath"]?.GetValue<string>(),
                    arguments?["severity"]?.GetValue<string>(),
                    arguments?["includeHidden"]?.GetValue<bool>() ?? false),

                "roslyn:get_code_fixes" => await _roslynService.GetCodeFixesAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["diagnosticId"]?.GetValue<string>() ?? throw new Exception("diagnosticId required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:apply_code_fix" => await _roslynService.ApplyCodeFixAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["diagnosticId"]?.GetValue<string>() ?? throw new Exception("diagnosticId required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["fixIndex"]?.GetValue<int?>(),
                    arguments?["preview"]?.GetValue<bool>() ?? true),

                "roslyn:get_project_structure" => await _roslynService.GetProjectStructureAsync(
                    arguments?["includeReferences"]?.GetValue<bool>() ?? true,
                    arguments?["includeDocuments"]?.GetValue<bool>() ?? false,
                    arguments?["projectNamePattern"]?.GetValue<string>(),
                    arguments?["maxProjects"]?.GetValue<int>(),
                    arguments?["summaryOnly"]?.GetValue<bool>() ?? false),

                "roslyn:organize_usings" => await _roslynService.OrganizeUsingsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required")),

                "roslyn:organize_usings_batch" => await _roslynService.OrganizeUsingsBatchAsync(
                    arguments?["projectName"]?.GetValue<string>(),
                    arguments?["filePattern"]?.GetValue<string>(),
                    arguments?["preview"]?.GetValue<bool>() ?? true),

                "roslyn:format_document_batch" => await _roslynService.FormatDocumentBatchAsync(
                    arguments?["projectName"]?.GetValue<string>(),
                    arguments?["includeTests"]?.GetValue<bool>() ?? true,
                    arguments?["preview"]?.GetValue<bool>() ?? true),

                "roslyn:get_method_overloads" => await _roslynService.GetMethodOverloadsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:get_containing_member" => await _roslynService.GetContainingMemberAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:find_callers" => await _roslynService.FindCallersAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["maxResults"]?.GetValue<int>()),

                "roslyn:find_unused_code" => await _roslynService.FindUnusedCodeAsync(
                    arguments?["projectName"]?.GetValue<string>(),
                    arguments?["includePrivate"]?.GetValue<bool>() ?? true,
                    arguments?["includeInternal"]?.GetValue<bool>() ?? false,
                    arguments?["symbolKindFilter"]?.GetValue<string>(),
                    arguments?["maxResults"]?.GetValue<int>()),

                "roslyn:rename_symbol" => await _roslynService.RenameSymbolAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["newName"]?.GetValue<string>() ?? throw new Exception("newName required"),
                    arguments?["preview"]?.GetValue<bool>() ?? true,
                    arguments?["maxFiles"]?.GetValue<int>(),
                    arguments?["verbosity"]?.GetValue<string>()),

                "roslyn:extract_interface" => await _roslynService.ExtractInterfaceAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required"),
                    arguments?["interfaceName"]?.GetValue<string>() ?? throw new Exception("interfaceName required"),
                    arguments?["includeMemberNames"]?.AsArray()?.Select(n => n?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()),

                "roslyn:dependency_graph" => await _roslynService.GetDependencyGraphAsync(
                    arguments?["format"]?.GetValue<string>()),

                _ => throw new Exception($"Unknown tool: {name}")
            };

            // Wrap result in MCP content format
            var mpcResult = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, _jsonOptions)
                    }
                }
            };

            return CreateSuccessResponse(id, mpcResult);
        }
        catch (FileNotFoundException ex)
        {
            await LogAsync("Error", $"File not found: {ex.Message}");
            return CreateErrorResponse(id, -32602, $"File not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            await LogAsync("Error", $"Error executing tool: {ex}");
            return CreateErrorResponse(id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private object CreateSuccessResponse(int? id, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result
        };
    }

    private object CreateErrorResponse(int? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message
            }
        };
    }

    private async Task LogAsync(string level, string message)
    {
        var logLevel = Environment.GetEnvironmentVariable("ROSLYN_LOG_LEVEL") ?? "Information";
        if (ShouldLog(level, logLevel))
        {
            await Console.Error.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
        }
    }

    private bool ShouldLog(string messageLevel, string configuredLevel)
    {
        var levels = new[] { "Debug", "Information", "Warning", "Error" };
        var messageIndex = Array.IndexOf(levels, messageLevel);
        var configuredIndex = Array.IndexOf(levels, configuredLevel);

        return messageIndex >= configuredIndex;
    }
}
