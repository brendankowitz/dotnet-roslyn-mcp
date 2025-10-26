# Roslyn MCP Server

[![NuGet Tool](https://img.shields.io/badge/.NET%20Tool-Install-blue?logo=nuget)](https://www.nuget.org/packages/dotnet-roslyn-mcp)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Model Context Protocol (MCP) stdio server that exposes Microsoft Roslyn SDK capabilities to AI coding assistants like Claude Code. Provides semantic code analysis, navigation, refactoring, and diagnostics for .NET/C# codebases.

**18+ powerful tools** including impact analysis, safe refactoring, dead code detection, automated code fixes, batch operations, and dependency visualization!

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

### Build and Install

```bash
# Build the project
dotnet build -c Release

# Pack as global tool
dotnet pack -c Release

# Install globally
dotnet tool install --global --add-source ./src/bin/Release dotnet-roslyn-mcp

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

## Available Tools (18 Total)

### Core & Health
1. **roslyn:health_check** - Check server health and workspace status
2. **roslyn:load_solution** - Load a .NET solution for analysis
3. **roslyn:get_symbol_info** - Get detailed semantic information about a symbol

### Navigation
4. **roslyn:find_references** - Find all references to a symbol
5. **roslyn:find_implementations** - Find all implementations of an interface/abstract class
6. **roslyn:find_callers* - Find all methods that call a specific method (impact analysis)

### Analysis & Discovery
7. **roslyn:get_type_hierarchy** - Get inheritance hierarchy for a type
8. **roslyn:search_symbols** - Search for symbols by name across solution
9. **roslyn:get_diagnostics** - Get compiler errors and warnings
10. **roslyn:find_unused_code** - Find dead code (unused types, methods, fields)
11. **roslyn:dependency_graph** - Visualize project dependencies and detect cycles

### Refactoring
12. **roslyn:rename_symbol** - Safely rename symbol across solution with preview
13. **roslyn:extract_interface** - Generate interface from class for DI/testability
14. **roslyn:organize_usings** - Sort and remove unused using directives

### Code Fixes & Structure
15. **roslyn:get_code_fixes** - Get available code fixes for diagnostics
16. **roslyn:get_project_structure** - Get solution/project structure
17. **roslyn:get_method_overloads** - Get all overloads of a method
18. **roslyn:get_containing_member** - Get containing method/property/class info

## Example Prompts

Here are some example prompts you can use with Claude Code once the MCP server is configured:

**Impact Analysis:**
```
"Find all callers of the ProcessPayment method"
"What code will break if I change this method signature?"
"Who uses the CustomerRepository?"
```

**Safe Refactoring:**
```
"Preview renaming ICustomerRepository to IUserRepository"
"Safely rename ProcessPayment to HandlePayment across the solution"
```

**Dead Code Detection :**
```
"Find all unused code in the Application project"
"What private methods are never called?"
"Show me unused classes in the Domain layer"
```

**Interface Extraction:**
```
"Extract an interface from PaymentService class"
"Generate IPaymentService interface with ProcessPayment and RefundPayment methods"
```

**Dependency Visualization:**
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

## License

MIT License - Copyright (c) 2025 Brendan Kowitz

See [LICENSE](LICENSE) file for details.
