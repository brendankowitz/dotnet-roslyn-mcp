# Visual Studio IDE MCP Server

A Model Context Protocol (MCP) server that exposes Visual Studio IDE capabilities to AI assistants like Claude Code, enabling seamless AI-assisted pair programming for .NET/C# development.

## Overview

This Visual Studio extension (VSPackage) implements an MCP server that runs inside Visual Studio 2022 and exposes IDE capabilities through HTTP/SSE endpoints. It allows AI assistants to:

- Navigate and understand complex .NET/C# codebases
- Access real-time diagnostics and analyzer information
- Search for symbols (types, methods, properties, etc.)
- Read and analyze document structure
- Query project and solution metadata

## Features

### Core Capabilities

- **Solution & Project Information**: Get comprehensive details about loaded solutions and projects
- **Symbol Search**: Find types, methods, properties, and other symbols across the solution
- **Diagnostics**: Access errors, warnings, and suggestions from Roslyn analyzers
- **Document Operations**: Read file contents and get structural outlines
- **Roslyn Integration**: Deep semantic understanding of C# code

### MCP Tools

The server exposes the following MCP tools:

1. `get_solution_info` - Get solution structure, projects, and configurations
2. `get_diagnostics` - Retrieve all diagnostics with filtering options
3. `search_symbols` - Search for code symbols by name and kind
4. `read_document` - Read file contents with optional line ranges
5. `get_document_outline` - Get structural outline of a document

## Installation

### Prerequisites

- **Visual Studio 2022** (Community, Professional, or Enterprise)
- **Visual Studio extension development** workload installed
- **.NET Framework 4.8 Developer Pack**

### Building from Source

**Easiest Method - Use Visual Studio:**

1. Clone the repository:
   ```bash
   git clone https://github.com/your-org/vs-ide-mcp.git
   cd vs-ide-mcp
   ```

2. Open `VsIdeMcp.sln` in Visual Studio 2022

3. Press **Ctrl+Shift+B** (Build → Rebuild Solution)

4. Find the VSIX installer: `src/VsIdeMcp/bin/Debug/VsIdeMcp.vsix`

5. Double-click the `.vsix` file to install the extension

**Alternative - Developer Command Prompt:**

1. Run `open-dev-prompt.bat` from the project root
2. Execute: `msbuild src\VsIdeMcp\VsIdeMcp.csproj /t:Rebuild /p:Configuration=Release`

See [BUILD-INSTRUCTIONS.md](BUILD-INSTRUCTIONS.md) for detailed build guide.

### Installing the Extension

1. Double-click the `.vsix` file
2. Follow the installation wizard
3. Restart Visual Studio
4. Navigate to **Tools → Options → VS IDE MCP Server → General**
5. Enable the MCP server and configure the port (default: 5678)

## Configuration

### Visual Studio Options

Open **Tools → Options → VS IDE MCP Server → General**:

- **Enable MCP Server**: Toggle to enable/disable the server
- **Server Port**: Port number for HTTP/SSE connections (default: 5678)

### Claude Code Configuration

**Method 1: Using Claude CLI (Recommended)**

Add the MCP server using the Claude CLI command:

```bash
# SSE transport (current implementation)
claude mcp add --transport sse visual-studio http://localhost:5678

# Alternative: HTTP transport (if server is updated)
claude mcp add --transport http visual-studio http://localhost:5678
```

After adding, restart Claude Code for changes to take effect.

**Method 2: Manual Configuration**

Alternatively, add the following to your Claude Code MCP configuration file (`.claude/mcp.json`):

```json
{
  "mcpServers": {
    "visual-studio": {
      "url": "http://localhost:5678",
      "transport": "sse"
    }
  }
}
```

**Other Useful Commands:**

```bash
# List all configured MCP servers
claude mcp list

# Remove the server
claude mcp remove visual-studio

# Test the server connection
claude mcp get visual-studio
```

## Usage

### Starting the Server

1. Open Visual Studio 2022
2. Open a solution (.sln file)
3. The MCP server will start automatically if enabled in options
4. Check the "VS IDE MCP Server" output pane for status messages

### Using with Claude Code

Once configured, Claude Code can automatically use the MCP server to:

```
User: "What errors are in my solution?"
Claude: <calls get_diagnostics tool>

User: "Find all classes named UserService"
Claude: <calls search_symbols tool with query="UserService", kind="Class">

User: "Show me the structure of UserController.cs"
Claude: <calls get_document_outline tool>
```

### Example Tool Calls

**Get Solution Info:**
```json
{
  "name": "get_solution_info",
  "arguments": {}
}
```

**Get Diagnostics:**
```json
{
  "name": "get_diagnostics",
  "arguments": {
    "minSeverity": "Error"
  }
}
```

**Search Symbols:**
```json
{
  "name": "search_symbols",
  "arguments": {
    "query": "UserService",
    "kind": "Class"
  }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Claude Code                          │
│                    (MCP Client)                          │
└────────────────────┬────────────────────────────────────┘
                     │ HTTP/SSE
                     ▼
┌─────────────────────────────────────────────────────────┐
│              VS IDE MCP Server                           │
│  ┌─────────────────────────────────────────────────┐   │
│  │  HTTP/SSE Transport (ASP.NET Core)              │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  MCP Protocol Handler                           │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Service Layer                                  │   │
│  │  - SolutionService                              │   │
│  │  - SymbolService                                │   │
│  │  - DiagnosticService                            │   │
│  │  - DocumentService                              │   │
│  └─────────────────────────────────────────────────┘   │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Visual Studio IDE                           │
│  - DTE2 Object Model                                    │
│  - Roslyn Workspace                                     │
│  - Code Analyzers                                       │
└─────────────────────────────────────────────────────────┘
```

## Development

### Project Structure

```
vs-ide-mcp/
├── src/
│   └── VsIdeMcp/
│       ├── Package/           # VSPackage entry point
│       ├── Mcp/              # MCP server and transport
│       ├── Services/         # Core services
│       ├── Models/           # Data models
│       └── UI/               # Options page
├── docs/                     # Documentation
└── VsIdeMcp.sln             # Solution file
```

### Building

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Create VSIX package
msbuild /t:CreateVsixContainer
```

### Debugging

1. Set `VsIdeMcp` as the startup project
2. Press F5 to start debugging
3. A new Visual Studio instance will launch (Experimental Instance)
4. Open a solution in the experimental instance
5. The MCP server will start automatically

## Troubleshooting

### Server Not Starting

- Check **View → Output** and select "VS IDE MCP Server" from the dropdown
- Verify that the port is not in use by another application
- Ensure the extension is enabled in **Tools → Options**

### Connection Refused

- Verify the server is running (check output window)
- Check firewall settings for localhost connections
- Ensure the correct port is configured in both VS and Claude Code

### Tools Not Working

- Ensure a solution is loaded in Visual Studio
- Check the output window for error messages
- Verify that the Roslyn workspace is available

## Limitations

- Only works with C# projects (VB.NET support planned)
- Requires Visual Studio 2022
- Read-only operations only (refactoring tools coming in future versions)
- Limited to localhost connections for security

## Future Enhancements

### Phase 2
- Refactoring operations (rename, extract method, etc.)
- Code fix application
- Find references and call hierarchy
- Test discovery and execution

### Phase 3
- Debugger integration
- Code generation capabilities
- Git integration
- Performance profiling

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- **Issues**: https://github.com/your-org/vs-ide-mcp/issues
- **Documentation**: https://github.com/your-org/vs-ide-mcp/wiki
- **Discussions**: https://github.com/your-org/vs-ide-mcp/discussions

## Credits

Built with:
- [Visual Studio SDK](https://docs.microsoft.com/visualstudio/extensibility/)
- [Roslyn](https://github.com/dotnet/roslyn)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [ASP.NET Core](https://docs.microsoft.com/aspnet/core/)

## Acknowledgments

Inspired by JetBrains Rider's MCP integration and designed to work seamlessly with Claude Code.
