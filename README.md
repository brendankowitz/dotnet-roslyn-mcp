# Roslyn MCP Server

A Model Context Protocol (MCP) stdio server that exposes Microsoft Roslyn SDK capabilities to AI coding assistants like Claude Code. Provides semantic code analysis, navigation, refactoring, and diagnostics for .NET/C# codebases.

## Quick Start

```bash
# Build and install
dotnet pack -c Release
dotnet tool install --global --add-source ./src/bin/Release RoslynMcp

# Add to Claude Code
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  -- dotnetroslyn-mcp

# Start using with Claude Code!
```

## Features

- **Semantic Analysis**: 100% compiler-accurate code understanding
- **Cross-Solution Navigation**: Find references, implementations, and type hierarchies
- **Real-time Diagnostics**: Get compilation errors and warnings
- **Symbol Search**: Search for types, methods, properties across the solution
- **Code Organization**: Organize usings, get method overloads, and more

## Installation

### Prerequisites

- .NET 8.0 SDK or Runtime
- MSBuild (Visual Studio Build Tools 2022 or Visual Studio 2022)

### Build and Install

```bash
# Build the project
dotnet build -c Release

# Pack as global tool
dotnet pack -c Release

# Install globally
dotnet tool install --global --add-source ./src/bin/Release RoslynMcp

# Verify installation
dotnetroslyn-mcp --version
```

## Configuration

### Using Claude Code CLI (Recommended)

After installing the global tool, add it to Claude Code using the CLI:

```bash
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  -- dotnetroslyn-mcp
```

Or with additional environment variables:

```bash
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  --env ROSLYN_LOG_LEVEL="Information" \
  --env ROSLYN_MAX_DIAGNOSTICS="100" \
  -- dotnetroslyn-mcp
```

### Manual Configuration

Alternatively, create a `.claude/mcp-spec.json` file in your solution root:

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnetroslyn-mcp",
      "env": {
        "DOTNET_SOLUTION_PATH": "${workspaceFolder}/YourSolution.sln"
      }
    }
  }
}
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOTNET_SOLUTION_PATH` | (Required) | Path to .sln file or directory containing it |
| `ROSLYN_LOG_LEVEL` | Information | Logging level (Debug, Information, Warning, Error) |
| `ROSLYN_ENABLE_SEMANTIC_CACHE` | true | Enable document caching |
| `ROSLYN_MAX_DIAGNOSTICS` | 100 | Maximum diagnostics to return |
| `ROSLYN_INCLUDE_HIDDEN_DIAGNOSTICS` | false | Include hidden severity diagnostics |
| `ROSLYN_PARALLEL_ANALYSIS` | true | Enable parallel project analysis |
| `ROSLYN_TIMEOUT_SECONDS` | 30 | Operation timeout |

## Available Tools

### Core Tools

1. **roslyn:load_solution** - Load a .NET solution for analysis
2. **roslyn:get_symbol_info** - Get detailed semantic information about a symbol
3. **roslyn:find_references** - Find all references to a symbol
4. **roslyn:find_implementations** - Find all implementations of an interface/abstract class
5. **roslyn:get_type_hierarchy** - Get inheritance hierarchy for a type
6. **roslyn:search_symbols** - Search for symbols by name
7. **roslyn:get_diagnostics** - Get compiler errors and warnings
8. **roslyn:get_code_fixes** - Get available code fixes
9. **roslyn:get_project_structure** - Get solution/project structure
10. **roslyn:organize_usings** - Sort and remove unused using directives
11. **roslyn:get_method_overloads** - Get all overloads of a method
12. **roslyn:get_containing_member** - Get containing method/property/class info

## Usage with Claude Code

Once installed and configured, Claude Code will automatically use the Roslyn MCP server for:

- Finding accurate references across your entire solution
- Understanding type hierarchies and implementations
- Navigating complex codebases
- Getting real-time compilation diagnostics
- Safe refactoring operations

## Example Prompts

Here are some example prompts you can use with Claude Code once the MCP server is configured:

**Find References:**
```
"Find all references to the ProcessPayment method"
"Where is CustomerService being used?"
```

**Explore Type Hierarchies:**
```
"What classes implement ICustomerRepository?"
"Show me the type hierarchy for CustomerService"
"What interfaces does PaymentProcessor implement?"
```

**Code Navigation:**
```
"Show me all the overloads of ProcessOrder"
"What's the containing class for this method?"
"Search for all methods containing 'Customer'"
```

**Diagnostics:**
```
"Get all compilation errors in the solution"
"Show me warnings in CustomerService.cs"
"What errors are in the Payment project?"
```

**Code Analysis:**
```
"Get symbol information at line 45, column 10 in CustomerService.cs"
"Show me the project structure"
"Organize the usings in this file"
```

## Architecture

```
┌─────────────────────────────────────┐
│     Claude Code (MCP Client)        │
└───────────────┬─────────────────────┘
                │ stdin/stdout (JSON-RPC 2.0)
┌───────────────▼─────────────────────┐
│          McpServer.cs                │
│  - Protocol handling                 │
│  - Tool registration                 │
│  - Request routing                   │
└───────────────┬─────────────────────┘
                │
┌───────────────▼─────────────────────┐
│       RoslynService.cs               │
│  - Solution management               │
│  - Semantic analysis                 │
│  - Symbol resolution                 │
└───────────────┬─────────────────────┘
                │
┌───────────────▼─────────────────────┐
│  Microsoft.CodeAnalysis (Roslyn)    │
│  - MSBuildWorkspace                  │
│  - SemanticModel                     │
│  - SymbolFinder                      │
└─────────────────────────────────────┘
```

## Development

### Build

```bash
dotnet build
```

### Test

```bash
dotnet build -c Release
dotnet run --project src/RoslynMcp.csproj
```

### Project Structure

```
.
├── src/
│   ├── RoslynMcp.csproj    # Project file
│   ├── Program.cs          # Entry point
│   ├── McpServer.cs        # MCP protocol handler
│   └── RoslynService.cs    # Roslyn integration
├── docs/
│   └── SPECIFICATION.md    # Complete specification
├── RoslynMcp.sln          # Solution file
└── README.md              # This file
```

## Documentation

- **[Build & Package Guide](BUILD.md)** - Building, packaging, and publishing instructions
- **[Quick Reference Guide](docs/QUICK_REFERENCE.md)** - Commands, environment variables, and common use cases
- **[Complete Specification](docs/SPECIFICATION.md)** - Full technical specification and implementation details

## License

MIT License - Copyright (c) 2025 Brendan Kowitz

See [LICENSE](LICENSE) file for details.

## Contributing

Contributions welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## Support

For issues and questions:
- GitHub Issues: [Repository Issues](https://github.com/yourusername/dotnet-roslyn-mcp/issues)
- Documentation: See [SPECIFICATION.md](docs/SPECIFICATION.md)

## Performance

- **Solution Load**: 5-75 seconds depending on size
- **Symbol Info**: ~50ms (cached)
- **Find References**: ~200ms (cached)
- **Memory**: 500MB-3.5GB depending on solution size

## Requirements

- .NET 8.0 Runtime/SDK
- MSBuild (Visual Studio Build Tools 2022+)
- Windows 10+, macOS 11+, or Linux (Ubuntu 20.04+)
