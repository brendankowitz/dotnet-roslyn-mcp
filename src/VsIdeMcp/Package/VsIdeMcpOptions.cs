using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SVsShell = Microsoft.VisualStudio.Shell.Interop.SVsShell;

namespace VsIdeMcp.Package
{
    /// <summary>
    /// Options page for VS IDE MCP Server
    /// </summary>
    [ComVisible(true)]
    public class VsIdeMcpOptions : DialogPage
    {
        private bool _enableMcpServer = true;
        private int _serverPort = 5678;

        /// <summary>
        /// Event fired when options are changed
        /// </summary>
        public event EventHandler? OptionsChanged;

        /// <summary>
        /// Enable or disable the MCP server
        /// </summary>
        [Category("Server")]
        [DisplayName("Enable MCP Server")]
        [Description("Enable or disable the Model Context Protocol server. When enabled, the server will start automatically and listen for connections from MCP clients like Claude Code.")]
        public bool EnableMcpServer
        {
            get => _enableMcpServer;
            set
            {
                if (_enableMcpServer != value)
                {
                    _enableMcpServer = value;
                    OnOptionsChanged();
                }
            }
        }

        /// <summary>
        /// Port number for the MCP server
        /// </summary>
        [Category("Server")]
        [DisplayName("Server Port")]
        [Description("The port number where the MCP server will listen for HTTP/SSE connections. Default is 5678.")]
        public int ServerPort
        {
            get => _serverPort;
            set
            {
                if (_serverPort != value && value > 0 && value <= 65535)
                {
                    _serverPort = value;
                    OnOptionsChanged();
                }
            }
        }

        /// <summary>
        /// Raises the OptionsChanged event
        /// </summary>
        private void OnOptionsChanged()
        {
            OptionsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Called when the options page is activated
        /// </summary>
        protected override void OnActivate(System.ComponentModel.CancelEventArgs e)
        {
            base.OnActivate(e);

            // Force the package to load
            try
            {
                var shell = Site.GetService(typeof(SVsShell)) as IVsShell;
                if (shell != null)
                {
                    Guid packageGuid = new Guid(VsIdeMcpPackage.PackageGuidString);
                    shell.IsPackageLoaded(ref packageGuid, out IVsPackage package);

                    if (package == null)
                    {
                        // Package not loaded, force load it
                        shell.LoadPackage(ref packageGuid, out package);

                        VsShellUtilities.ShowMessageBox(
                            Site,
                            $"Package was NOT loaded - forcing load now!\n\nCurrent settings:\nEnabled: {_enableMcpServer}\nPort: {_serverPort}",
                            "VS IDE MCP - Package Force Loaded",
                            OLEMSGICON.OLEMSGICON_WARNING,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    else
                    {
                        VsShellUtilities.ShowMessageBox(
                            Site,
                            $"Package already loaded!\n\nCurrent settings:\nEnabled: {_enableMcpServer}\nPort: {_serverPort}",
                            "VS IDE MCP - Options Page Activated",
                            OLEMSGICON.OLEMSGICON_INFO,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(
                    Site,
                    $"Error checking/loading package: {ex.Message}",
                    "VS IDE MCP - Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// Called when the dialog page is saved
        /// </summary>
        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                // Show diagnostic message
                VsShellUtilities.ShowMessageBox(
                    Site,
                    $"Options saved!\n\nNew settings:\nEnabled: {_enableMcpServer}\nPort: {_serverPort}\n\nFiring OptionsChanged event...",
                    "VS IDE MCP - Options Saved",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                OnOptionsChanged();
            }
        }
    }
}
