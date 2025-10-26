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
                name = "roslyn:load_solution",
                description = "Load a .NET solution for analysis",
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
                description = "Get detailed semantic information about a symbol at a specific position",
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
                name = "roslyn:find_references",
                description = "Find all references to a symbol across the entire solution",
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
                name = "roslyn:find_implementations",
                description = "Find all implementations of an interface or abstract class",
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
                name = "roslyn:get_type_hierarchy",
                description = "Get the inheritance hierarchy (base types and derived types) for a type",
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
                name = "roslyn:search_symbols",
                description = "Search for types, methods, properties, etc. by name across the solution",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search query (supports wildcards)" },
                        kind = new { type = "string", description = "Optional: filter by symbol kind (Class, Method, Property, etc.)" },
                        maxResults = new { type = "integer", description = "Maximum number of results (default: 50)" }
                    },
                    required = new[] { "query" }
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
                name = "roslyn:get_project_structure",
                description = "Get solution/project structure including projects, references, and compilation settings",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeReferences = new { type = "boolean", description = "Include package references (default: true)" },
                        includeDocuments = new { type = "boolean", description = "Include document lists (default: false)" }
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
                "roslyn:load_solution" => await _roslynService.LoadSolutionAsync(
                    arguments?["solutionPath"]?.GetValue<string>() ?? throw new Exception("solutionPath required")),

                "roslyn:get_symbol_info" => await _roslynService.GetSymbolInfoAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:find_references" => await _roslynService.FindReferencesAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:find_implementations" => await _roslynService.FindImplementationsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:get_type_hierarchy" => await _roslynService.GetTypeHierarchyAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:search_symbols" => await _roslynService.SearchSymbolsAsync(
                    arguments?["query"]?.GetValue<string>() ?? throw new Exception("query required"),
                    arguments?["kind"]?.GetValue<string>(),
                    arguments?["maxResults"]?.GetValue<int>() ?? 50),

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

                "roslyn:get_project_structure" => await _roslynService.GetProjectStructureAsync(
                    arguments?["includeReferences"]?.GetValue<bool>() ?? true,
                    arguments?["includeDocuments"]?.GetValue<bool>() ?? false),

                "roslyn:organize_usings" => await _roslynService.OrganizeUsingsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required")),

                "roslyn:get_method_overloads" => await _roslynService.GetMethodOverloadsAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                "roslyn:get_containing_member" => await _roslynService.GetContainingMemberAsync(
                    arguments?["filePath"]?.GetValue<string>() ?? throw new Exception("filePath required"),
                    arguments?["line"]?.GetValue<int>() ?? throw new Exception("line required"),
                    arguments?["column"]?.GetValue<int>() ?? throw new Exception("column required")),

                _ => throw new Exception($"Unknown tool: {name}")
            };

            return CreateSuccessResponse(id, result);
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
