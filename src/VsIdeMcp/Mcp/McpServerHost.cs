using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.LanguageServices;
using VsIdeMcp.Services;

namespace VsIdeMcp.Mcp
{
    /// <summary>
    /// Hosts the MCP server with HTTP/SSE endpoints
    /// </summary>
    public class McpServerHost : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly VisualStudioWorkspace? _workspace;
        private readonly int _port;
        private IWebHost? _webHost;
        private McpServer? _mcpServer;

        public McpServerHost(DTE2 dte, VisualStudioWorkspace? workspace, int port)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _workspace = workspace; // Workspace can be null - services will handle it gracefully
            _port = port;
        }

        /// <summary>
        /// Starts the MCP server
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_webHost != null)
            {
                throw new InvalidOperationException("Server is already running");
            }

            // Initialize services
            var solutionService = new SolutionService(_dte, _workspace);
            var symbolService = new SymbolService(_workspace);
            var diagnosticService = new DiagnosticService(_workspace);
            var documentService = new DocumentService(_dte, _workspace);

            // Create MCP server
            _mcpServer = new McpServer(solutionService, symbolService, diagnosticService, documentService);

            // Build web host
            _webHost = new WebHostBuilder()
                .UseKestrel()
                .UseUrls($"http://localhost:{_port}")
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_mcpServer);
                })
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        var path = context.Request.Path.Value;
                        var method = context.Request.Method;

                        System.Diagnostics.Debug.WriteLine($"[MCP HTTP] Request: {method} {path}");
                        System.Diagnostics.Debug.WriteLine($"[MCP HTTP] Headers: {string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}"))}");

                        if (path == "/sse" && method == "GET")
                        {
                            System.Diagnostics.Debug.WriteLine("[MCP HTTP] Handling SSE connection request");
                            context.Response.Headers.Add("Content-Type", "text/event-stream");
                            context.Response.Headers.Add("Cache-Control", "no-cache");
                            context.Response.Headers.Add("Connection", "keep-alive");
                            await _mcpServer.HandleSseConnectionAsync(context);
                        }
                        else if (path == "/message" && method == "POST")
                        {
                            System.Diagnostics.Debug.WriteLine("[MCP HTTP] Handling message request");
                            await _mcpServer.HandleMessageAsync(context);
                        }
                        else if (path == "/health" && method == "GET")
                        {
                            System.Diagnostics.Debug.WriteLine("[MCP HTTP] Handling health check");
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"status\":\"healthy\",\"server\":\"vs-ide-mcp\",\"version\":\"1.0.0\"}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[MCP HTTP] 404 Not Found: {method} {path}");
                            context.Response.StatusCode = 404;
                            await context.Response.WriteAsync("Not Found");
                        }
                    });
                })
                .Build();

            await _webHost.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Stops the MCP server
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_webHost != null)
            {
                await _webHost.StopAsync(cancellationToken);
                _webHost.Dispose();
                _webHost = null;
            }

            _mcpServer = null;
        }

        public void Dispose()
        {
            StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
