using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VsIdeMcp.Services;

namespace VsIdeMcp.Mcp
{
    /// <summary>
    /// MCP protocol server implementation
    /// </summary>
    public class McpServer
    {
        private readonly SolutionService _solutionService;
        private readonly SymbolService _symbolService;
        private readonly DiagnosticService _diagnosticService;
        private readonly DocumentService _documentService;
        private readonly ConcurrentDictionary<string, SseConnection> _connections = new();
        private readonly McpToolHandlers _toolHandlers;

        public McpServer(
            SolutionService solutionService,
            SymbolService symbolService,
            DiagnosticService diagnosticService,
            DocumentService documentService)
        {
            _solutionService = solutionService ?? throw new ArgumentNullException(nameof(solutionService));
            _symbolService = symbolService ?? throw new ArgumentNullException(nameof(symbolService));
            _diagnosticService = diagnosticService ?? throw new ArgumentNullException(nameof(diagnosticService));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));

            _toolHandlers = new McpToolHandlers(solutionService, symbolService, diagnosticService, documentService);
        }

        /// <summary>
        /// Handles SSE connection
        /// </summary>
        public async Task HandleSseConnectionAsync(HttpContext context)
        {
            var connectionId = Guid.NewGuid().ToString();

            System.Diagnostics.Debug.WriteLine($"[MCP SSE] New connection request from {context.Connection.RemoteIpAddress}");
            System.Diagnostics.Debug.WriteLine($"[MCP SSE] Connection ID: {connectionId}");

            var connection = new SseConnection(context);
            _connections[connectionId] = connection;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Sending endpoint event to connection {connectionId}");

                // Send initial connection event with session ID
                // Format per MCP spec: /message?session_id=<uuid>
                var endpoint = $"/message?session_id={connectionId}";
                await connection.SendEventAsync("endpoint", endpoint);

                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Endpoint event sent successfully: {endpoint}");
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Keeping connection {connectionId} alive...");

                // Keep connection alive
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    await Task.Delay(1000, context.RequestAborted);
                }

                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Connection {connectionId} closed by client");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Connection {connectionId} cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] ERROR in connection {connectionId}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                System.Diagnostics.Debug.WriteLine($"[MCP SSE] Connection {connectionId} cleaned up. Active connections: {_connections.Count}");
            }
        }

        /// <summary>
        /// Handles incoming messages from client
        /// </summary>
        public async Task HandleMessageAsync(HttpContext context)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Received message request from {context.Connection.RemoteIpAddress}");
                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Query string: {context.Request.QueryString}");

                using var reader = new StreamReader(context.Request.Body);
                var requestBody = await reader.ReadToEndAsync();

                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Request body: {requestBody}");

                var jsonRequest = JsonDocument.Parse(requestBody);
                var root = jsonRequest.RootElement;

                // Parse the JSONRPC request
                var method = root.GetProperty("method").GetString();
                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Method: {method}, ID: {id}");

                object? result = null;
                McpError? error = null;

                try
                {
                    result = method switch
                    {
                        "initialize" => await HandleInitializeAsync(root),
                        "tools/list" => await HandleToolsListAsync(),
                        "tools/call" => await HandleToolCallAsync(root),
                        _ => throw new InvalidOperationException($"Unknown method: {method}")
                    };
                }
                catch (Exception ex)
                {
                    error = new McpError
                    {
                        Code = -32603,
                        Message = ex.Message
                    };
                }

                // Send response
                var response = new
                {
                    jsonrpc = "2.0",
                    id,
                    result,
                    error
                };

                context.Response.ContentType = "application/json";
                var jsonResponse = JsonSerializer.Serialize(response);

                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Sending response: {jsonResponse}");

                await context.Response.WriteAsync(jsonResponse, context.RequestAborted);

                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Response sent successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCP MSG] ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MCP MSG] Stack: {ex.StackTrace}");

                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error processing request: {ex.Message}", context.RequestAborted);
            }
        }

        /// <summary>
        /// Handles initialize request
        /// </summary>
        private Task<object> HandleInitializeAsync(JsonElement root)
        {
            var response = new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new
                {
                    name = "visual-studio-ide",
                    version = "1.0.0"
                },
                capabilities = new
                {
                    tools = new { }
                }
            };

            return Task.FromResult<object>(response);
        }

        /// <summary>
        /// Handles tools/list request
        /// </summary>
        private Task<object> HandleToolsListAsync()
        {
            var tools = new object[]
            {
                new
                {
                    name = "get_solution_info",
                    description = "Get comprehensive information about the loaded Visual Studio solution including projects, configurations, and dependencies",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "get_diagnostics",
                    description = "Get all diagnostics (errors, warnings, suggestions) for the solution or specific file",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new
                            {
                                type = "string",
                                description = "Optional: filter diagnostics to specific file"
                            },
                            projectName = new
                            {
                                type = "string",
                                description = "Optional: filter diagnostics to specific project"
                            },
                            minSeverity = new
                            {
                                type = "string",
                                @enum = new[] { "Error", "Warning", "Info", "Hidden" },
                                description = "Minimum severity level to include"
                            }
                        },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "search_symbols",
                    description = "Search for symbols (types, methods, properties, etc.) across the solution",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new
                            {
                                type = "string",
                                description = "Symbol name or pattern to search for"
                            },
                            kind = new
                            {
                                type = "string",
                                @enum = new[] { "Class", "Interface", "Method", "Property", "Field", "Event", "Namespace", "All" },
                                description = "Type of symbol to search for"
                            },
                            projectName = new
                            {
                                type = "string",
                                description = "Optional: limit search to specific project"
                            }
                        },
                        required = new[] { "query" }
                    }
                },
                new
                {
                    name = "read_document",
                    description = "Read the contents of a document",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new
                            {
                                type = "string",
                                description = "Absolute path to the file"
                            },
                            startLine = new
                            {
                                type = "integer",
                                description = "Optional: start line (1-based)"
                            },
                            endLine = new
                            {
                                type = "integer",
                                description = "Optional: end line (1-based)"
                            }
                        },
                        required = new[] { "filePath" }
                    }
                },
                new
                {
                    name = "get_document_outline",
                    description = "Get structural outline of a document (classes, methods, properties)",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new
                            {
                                type = "string",
                                description = "Absolute path to the file"
                            }
                        },
                        required = new[] { "filePath" }
                    }
                }
            };

            return Task.FromResult<object>(new { tools });
        }

        /// <summary>
        /// Handles tools/call request
        /// </summary>
        private async Task<object> HandleToolCallAsync(JsonElement root)
        {
            var paramsElement = root.GetProperty("params");
            var toolName = paramsElement.GetProperty("name").GetString();
            var arguments = paramsElement.TryGetProperty("arguments", out var args) ? args : default;

            var result = await _toolHandlers.HandleToolCallAsync(toolName!, arguments);

            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = result
                    }
                }
            };
        }

        private class SseConnection
        {
            private readonly HttpContext _context;

            public SseConnection(HttpContext context)
            {
                _context = context;
            }

            public async Task SendEventAsync(string eventType, string data)
            {
                // CORRECT SSE format: event MUST come before data per SSE spec
                var message = $"event: {eventType}\ndata: {data}\n\n";
                var bytes = Encoding.UTF8.GetBytes(message);
                await _context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                await _context.Response.Body.FlushAsync();
            }
        }

        private class McpError
        {
            public int Code { get; set; }
            public string Message { get; set; } = string.Empty;
        }
    }
}
