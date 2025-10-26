using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VsIdeMcp.Services;

namespace VsIdeMcp.Mcp
{
    /// <summary>
    /// Handles MCP tool calls and dispatches to appropriate services
    /// </summary>
    public class McpToolHandlers
    {
        private readonly SolutionService _solutionService;
        private readonly SymbolService _symbolService;
        private readonly DiagnosticService _diagnosticService;
        private readonly DocumentService _documentService;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public McpToolHandlers(
            SolutionService solutionService,
            SymbolService symbolService,
            DiagnosticService diagnosticService,
            DocumentService documentService)
        {
            _solutionService = solutionService;
            _symbolService = symbolService;
            _diagnosticService = diagnosticService;
            _documentService = documentService;
        }

        /// <summary>
        /// Handles a tool call and returns the result as JSON string
        /// </summary>
        public async Task<string> HandleToolCallAsync(string toolName, JsonElement arguments)
        {
            try
            {
                return toolName switch
                {
                    "get_solution_info" => await HandleGetSolutionInfoAsync(),
                    "get_diagnostics" => await HandleGetDiagnosticsAsync(arguments),
                    "search_symbols" => await HandleSearchSymbolsAsync(arguments),
                    "read_document" => await HandleReadDocumentAsync(arguments),
                    "get_document_outline" => await HandleGetDocumentOutlineAsync(arguments),
                    _ => throw new InvalidOperationException($"Unknown tool: {toolName}")
                };
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                }, _jsonOptions);
            }
        }

        private async Task<string> HandleGetSolutionInfoAsync()
        {
            var solutionInfo = await _solutionService.GetSolutionInfoAsync();
            return JsonSerializer.Serialize(solutionInfo, _jsonOptions);
        }

        private async Task<string> HandleGetDiagnosticsAsync(JsonElement arguments)
        {
            string? filePath = null;
            string? projectName = null;
            DiagnosticSeverity? minSeverity = null;

            if (arguments.ValueKind != JsonValueKind.Undefined)
            {
                if (arguments.TryGetProperty("filePath", out var filePathProp))
                {
                    filePath = filePathProp.GetString();
                }

                if (arguments.TryGetProperty("projectName", out var projectNameProp))
                {
                    projectName = projectNameProp.GetString();
                }

                if (arguments.TryGetProperty("minSeverity", out var severityProp))
                {
                    var severityStr = severityProp.GetString();
                    minSeverity = severityStr switch
                    {
                        "Error" => DiagnosticSeverity.Error,
                        "Warning" => DiagnosticSeverity.Warning,
                        "Info" => DiagnosticSeverity.Info,
                        "Hidden" => DiagnosticSeverity.Hidden,
                        _ => null
                    };
                }
            }

            var diagnostics = await _diagnosticService.GetDiagnosticsAsync(filePath, projectName, minSeverity);
            return JsonSerializer.Serialize(diagnostics, _jsonOptions);
        }

        private async Task<string> HandleSearchSymbolsAsync(JsonElement arguments)
        {
            var query = arguments.GetProperty("query").GetString() ?? string.Empty;

            SymbolKind? kind = null;
            if (arguments.TryGetProperty("kind", out var kindProp))
            {
                var kindStr = kindProp.GetString();
                if (kindStr != "All" && !string.IsNullOrEmpty(kindStr))
                {
                    Enum.TryParse<SymbolKind>(kindStr, out var parsedKind);
                    kind = parsedKind;
                }
            }

            string? projectName = null;
            if (arguments.TryGetProperty("projectName", out var projectNameProp))
            {
                projectName = projectNameProp.GetString();
            }

            var symbols = await _symbolService.SearchSymbolsAsync(query, kind, projectName);
            return JsonSerializer.Serialize(symbols, _jsonOptions);
        }

        private async Task<string> HandleReadDocumentAsync(JsonElement arguments)
        {
            var filePath = arguments.GetProperty("filePath").GetString() ?? string.Empty;

            int? startLine = null;
            if (arguments.TryGetProperty("startLine", out var startLineProp))
            {
                startLine = startLineProp.GetInt32();
            }

            int? endLine = null;
            if (arguments.TryGetProperty("endLine", out var endLineProp))
            {
                endLine = endLineProp.GetInt32();
            }

            var content = await _documentService.ReadDocumentAsync(filePath, startLine, endLine);
            return JsonSerializer.Serialize(content, _jsonOptions);
        }

        private async Task<string> HandleGetDocumentOutlineAsync(JsonElement arguments)
        {
            var filePath = arguments.GetProperty("filePath").GetString() ?? string.Empty;
            var outline = await _documentService.GetDocumentOutlineAsync(filePath);
            return JsonSerializer.Serialize(outline, _jsonOptions);
        }
    }
}
