# Roslyn MCP Server

A Model Context Protocol (MCP) stdio server that exposes Microsoft Roslyn SDK capabilities to AI coding assistants like Claude Code. Provides **enterprise-grade** semantic code analysis, navigation, refactoring, and diagnostics for .NET/C# codebases.

## 🎉 New: All Wishlist Features Complete!

**19 powerful tools** including impact analysis, safe refactoring, dead code detection, interface extraction, and dependency visualization!

## Quick Start

```bash
# Build and install
dotnet pack -c Release
dotnet tool install --global --add-source ./src/bin/Release RoslynMcp

# Add to Claude Code
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  -- dotnet-roslyn-mcp

# Start using with Claude Code!
```

## Features

- **Semantic Analysis**: 100% compiler-accurate code understanding
- **Cross-Solution Navigation**: Find references, implementations, callers, and type hierarchies
- **Impact Analysis**: See what code calls your methods before refactoring
- **Safe Refactoring**: Rename symbols across solution with preview mode
- **Dead Code Detection**: Find unused types, methods, and fields
- **Real-time Diagnostics**: Get compilation errors and warnings
- **Symbol Search**: Search for types, methods, properties across the solution
- **Interface Extraction**: Generate interfaces from classes for DI/testability
- **Dependency Visualization**: Graph project dependencies and detect cycles
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
dotnet-roslyn-mcp --version
```

## Configuration

### Using Claude Code CLI (Recommended)

After installing the global tool, add it to Claude Code using the CLI:

```bash
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  -- dotnet-roslyn-mcp
```

Or with additional environment variables:

```bash
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  --env ROSLYN_LOG_LEVEL="Information" \
  --env ROSLYN_MAX_DIAGNOSTICS="100" \
  -- dotnet-roslyn-mcp
```

**Windows:**
```bash
claude mcp add --transport stdio roslyn ^
  --env DOTNET_SOLUTION_PATH="C:\path\to\your\solution.sln" ^
  --env ROSLYN_LOG_LEVEL="Information" ^
  --env ROSLYN_MAX_DIAGNOSTICS="100" ^
  -- dotnet-roslyn-mcp
```

### Running from Source (Development)

If you want to run the MCP server directly from source without installing it globally:

**Linux/macOS:**
```bash
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/your/solution.sln" \
  -- dotnet run --project /path/to/vs-ide-mcp/src/RoslynMcp.csproj
```

**Windows:**
```bash
claude mcp add --transport stdio roslyn ^
  --env DOTNET_SOLUTION_PATH="C:\path\to\your\solution.sln" ^
  -- cmd /c dotnet run --project C:\path\to\vs-ide-mcp\src\RoslynMcp.csproj
```

### Manual Configuration

Alternatively, create a `.claude/mcp-spec.json` file in your solution root:

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet-roslyn-mcp",
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
| `ROSLYN_TIMEOUT_SECONDS` | 30 | Operation timeout (increase for large solutions) |

### Large Solutions

For solutions with 100+ projects, see **[docs/LARGE-SOLUTIONS.md](docs/LARGE-SOLUTIONS.md)** for:
- Token limit handling strategies
- Performance optimization tips
- Recommended workflows
- Tool-specific guidance

## Available Tools (19 Total)

### Core & Health
1. **roslyn:health_check** ⭐ - Check server health and workspace status
2. **roslyn:load_solution** - Load a .NET solution for analysis
3. **roslyn:get_symbol_info** - Get detailed semantic information about a symbol

### Navigation
4. **roslyn:find_references** - Find all references to a symbol
5. **roslyn:find_implementations** - Find all implementations of an interface/abstract class
6. **roslyn:find_callers** ⭐ - Find all methods that call a specific method (impact analysis)

### Analysis & Discovery
7. **roslyn:get_type_hierarchy** - Get inheritance hierarchy for a type
8. **roslyn:search_symbols** - Search for symbols by name across solution
9. **roslyn:get_diagnostics** - Get compiler errors and warnings
10. **roslyn:find_unused_code** ⭐ - Find dead code (unused types, methods, fields)
11. **roslyn:dependency_graph** ⭐ - Visualize project dependencies and detect cycles

### Refactoring
12. **roslyn:rename_symbol** ⭐ - Safely rename symbol across solution with preview
13. **roslyn:extract_interface** ⭐ - Generate interface from class for DI/testability
14. **roslyn:organize_usings** - Sort and remove unused using directives

### Code Fixes & Structure
15. **roslyn:get_code_fixes** - Get available code fixes for diagnostics
16. **roslyn:get_project_structure** - Get solution/project structure
17. **roslyn:get_method_overloads** - Get all overloads of a method
18. **roslyn:get_containing_member** - Get containing method/property/class info

⭐ = New tools (6 added in latest release!)

## Usage with Claude Code

Once installed and configured, Claude Code will automatically use the Roslyn MCP server for:

- Finding accurate references and callers across your entire solution
- Understanding type hierarchies and implementations
- Impact analysis before refactoring ("what will break?")
- Safe symbol renaming with preview mode
- Detecting and removing dead code
- Generating interfaces from classes
- Visualizing project dependencies
- Navigating complex codebases
- Getting real-time compilation diagnostics

## Example Prompts

Here are some example prompts you can use with Claude Code once the MCP server is configured:

**Impact Analysis (NEW):**
```
"Find all callers of the ProcessPayment method"
"What code will break if I change this method signature?"
"Who uses the CustomerRepository?"
```

**Safe Refactoring (NEW):**
```
"Preview renaming ICustomerRepository to IUserRepository"
"Safely rename ProcessPayment to HandlePayment across the solution"
```

**Dead Code Detection (NEW):**
```
"Find all unused code in the Application project"
"What private methods are never called?"
"Show me unused classes in the Domain layer"
```

**Interface Extraction (NEW):**
```
"Extract an interface from PaymentService class"
"Generate IPaymentService interface with ProcessPayment and RefundPayment methods"
```

**Dependency Visualization (NEW):**
```
"Show me the project dependency graph"
"Are there any circular dependencies?"
"Generate a Mermaid diagram of project dependencies"
```

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
