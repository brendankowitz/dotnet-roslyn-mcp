using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsIdeMcp.Mcp;
using Task = System.Threading.Tasks.Task;

namespace VsIdeMcp.Package
{
    /// <summary>
    /// Main Visual Studio Package for the MCP Server
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(VsIdeMcpOptions), "VS IDE MCP Server", "General", 0, 0, true)]
    public sealed class VsIdeMcpPackage : AsyncPackage
    {
        /// <summary>
        /// Package GUID string
        /// </summary>
        public const string PackageGuidString = "e7c8f4a3-9d2b-4f5e-8a1c-3b9e7f2d4a6c";

        private DTE2? _dte;
        private VisualStudioWorkspace? _workspace;
        private McpServerHost? _mcpServerHost;
        private VsIdeMcpOptions? _options;

        /// <summary>
        /// Initializes the package asynchronously
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Show that InitializeAsync was called
            VsShellUtilities.ShowMessageBox(
                this,
                "InitializeAsync() called - starting initialization...",
                "VS IDE MCP - Initialize Starting",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            try
            {
                // Get DTE
                await LogAsync("Getting DTE service...");
                _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
                if (_dte == null)
                {
                    await LogAsync("Failed to get DTE service");
                    VsShellUtilities.ShowMessageBox(
                        this,
                        "FAILED: Could not get DTE service!\n\nThe extension cannot start without DTE.",
                        "VS IDE MCP - Initialization Failed",
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }
                await LogAsync("DTE service obtained successfully");

                // Get Roslyn workspace (may not be available at startup - we'll get it lazily later)
                await LogAsync("Getting VisualStudioWorkspace service...");
                _workspace = await GetServiceAsync(typeof(VisualStudioWorkspace)) as VisualStudioWorkspace;
                if (_workspace == null)
                {
                    await LogAsync("WARNING: VisualStudioWorkspace not available at startup (this is normal)");
                    await LogAsync("Workspace will be acquired when a solution is loaded");
                    // Don't fail - workspace can be acquired later when needed
                }
                else
                {
                    await LogAsync("VisualStudioWorkspace service obtained successfully");
                }

                // Get options
                await LogAsync("Getting options page...");
                _options = GetDialogPage(typeof(VsIdeMcpOptions)) as VsIdeMcpOptions;
                if (_options == null)
                {
                    await LogAsync("Failed to get options page");
                    VsShellUtilities.ShowMessageBox(
                        this,
                        "FAILED: Could not get options page!\n\nThis should never happen.",
                        "VS IDE MCP - Initialization Failed",
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }
                await LogAsync("Options page obtained successfully");

                // Subscribe to options changed event
                _options.OptionsChanged += OnOptionsChanged;

                // Start MCP server if enabled
                if (_options.EnableMcpServer)
                {
                    await StartMcpServerAsync(cancellationToken);
                }

                await LogAsync("VS IDE MCP Package initialized successfully");

                // Show initialization complete dialog
                VsShellUtilities.ShowMessageBox(
                    this,
                    $"VS IDE MCP Package loaded!\n\nMCP Server enabled: {_options.EnableMcpServer}\nPort: {_options.ServerPort}",
                    "VS IDE MCP - Package Loaded",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                await LogAsync($"Error initializing package: {ex.Message}");

                // Show initialization error
                VsShellUtilities.ShowMessageBox(
                    this,
                    $"VS IDE MCP Package failed to load!\n\nError: {ex.Message}",
                    "VS IDE MCP - Initialization Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// Starts the MCP server
        /// </summary>
        private async Task StartMcpServerAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_mcpServerHost != null)
                {
                    await LogAsync("MCP server already running");
                    return;
                }

                if (_dte == null || _options == null)
                {
                    await LogAsync("Cannot start MCP server: DTE or options not initialized");
                    VsShellUtilities.ShowMessageBox(
                        this,
                        "Cannot start MCP server: Required services not initialized",
                        "VS IDE MCP Server - Error",
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                if (_workspace == null)
                {
                    await LogAsync("WARNING: Starting MCP server without Roslyn workspace");
                    await LogAsync("Some features (symbol search, diagnostics) will be limited until a solution is loaded");
                }

                await LogAsync($"Starting MCP server on port {_options.ServerPort}...");

                _mcpServerHost = new McpServerHost(_dte, _workspace, _options.ServerPort);
                await _mcpServerHost.StartAsync(cancellationToken);

                await LogAsync($"MCP server started successfully on http://localhost:{_options.ServerPort}");

                // Show confirmation dialog
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                VsShellUtilities.ShowMessageBox(
                    this,
                    $"MCP Server started successfully!\n\nListening on: http://localhost:{_options.ServerPort}\n\nCheck the Output window (VS IDE MCP Server) for details.",
                    "VS IDE MCP Server",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                await LogAsync($"Error starting MCP server: {ex.Message}");

                // Show error dialog
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                VsShellUtilities.ShowMessageBox(
                    this,
                    $"Failed to start MCP Server!\n\nError: {ex.Message}\n\nCheck the Output window (VS IDE MCP Server) for details.",
                    "VS IDE MCP Server - Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// Stops the MCP server
        /// </summary>
        private async Task StopMcpServerAsync()
        {
            try
            {
                if (_mcpServerHost == null)
                {
                    return;
                }

                await LogAsync("Stopping MCP server...");
                await _mcpServerHost.StopAsync();
                _mcpServerHost = null;

                await LogAsync("MCP server stopped");
            }
            catch (Exception ex)
            {
                await LogAsync($"Error stopping MCP server: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles options changed event
        /// </summary>
        private async void OnOptionsChanged(object? sender, EventArgs e)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_options == null)
            {
                VsShellUtilities.ShowMessageBox(
                    this,
                    "OnOptionsChanged called but _options is null!",
                    "VS IDE MCP - Options Changed",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // Show diagnostic dialog
            VsShellUtilities.ShowMessageBox(
                this,
                $"Options changed!\n\nEnabled: {_options.EnableMcpServer}\nPort: {_options.ServerPort}\nServer host null: {_mcpServerHost == null}",
                "VS IDE MCP - Options Changed",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            if (_options.EnableMcpServer && _mcpServerHost == null)
            {
                await StartMcpServerAsync(DisposalToken);
            }
            else if (!_options.EnableMcpServer && _mcpServerHost != null)
            {
                await StopMcpServerAsync();
            }
        }

        /// <summary>
        /// Logs a message to the output window
        /// </summary>
        private async Task LogAsync(string message)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var outputWindow = await GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow == null)
                {
                    return;
                }

                var paneGuid = new Guid("E7C8F4A3-9D2B-4F5E-8A1C-3B9E7F2D4A6D");
                outputWindow.GetPane(ref paneGuid, out var pane);

                if (pane == null)
                {
                    outputWindow.CreatePane(ref paneGuid, "VS IDE MCP Server", 1, 1);
                    outputWindow.GetPane(ref paneGuid, out pane);
                }

                pane?.OutputStringThreadSafe($"[VS IDE MCP] {DateTime.Now:HH:mm:ss} - {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>
        /// Disposes the package
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_options != null)
                {
                    _options.OptionsChanged -= OnOptionsChanged;
                }

                _mcpServerHost?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                _mcpServerHost = null;
            }

            base.Dispose(disposing);
        }
    }
}
