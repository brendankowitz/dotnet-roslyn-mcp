# Visual Studio IDE MCP Server Specification

## Executive Summary

This specification defines a Model Context Protocol (MCP) server that exposes Visual Studio IDE capabilities to Claude Code, enabling seamless AI-assisted pair programming. The server bridges the gap between Claude Code's agentic capabilities and Visual Studio's rich development environment, focusing on essential tools for code understanding, navigation, refactoring, and diagnostics.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Capabilities](#core-capabilities)
4. [MCP Primitives](#mcp-primitives)
5. [Implementation Details](#implementation-details)
6. [Security Considerations](#security-considerations)
7. [Usage Examples](#usage-examples)

---

## 1. Overview

### 1.1 Purpose

The Visual Studio IDE MCP Server enables Claude Code to:
- Navigate and understand complex .NET/C# codebases
- Access real-time diagnostics and analyzer information
- Perform safe refactoring operations
- Query project structure and metadata
- Access IntelliSense and symbol information
- Interact with build systems and test frameworks

### 1.2 Technology Stack

- **Language**: C# / .NET 8+
- **MCP SDK**: Microsoft.ModelContextProtocol (C# SDK)
- **Visual Studio APIs**: 
  - EnvDTE/EnvDTE80 (Automation API)
  - Microsoft.CodeAnalysis.* (Roslyn APIs)
  - Microsoft.VisualStudio.Shell.Interop
  - Microsoft.VisualStudio.LanguageServices
- **Transport**: stdio (for local development)
- **Extension Type**: VSPackage with MEF components

### 1.3 Design Principles

1. **Safety First**: All destructive operations require explicit confirmation
2. **Minimal Overhead**: Lightweight and performant
3. **Context-Aware**: Leverage active editor state
4. **Roslyn-Native**: Use Roslyn APIs for code analysis
5. **Stateless**: Each tool call is independent

---

## 2. Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────┐
│                     Claude Code                          │
│                    (MCP Client)                          │
└────────────────────┬────────────────────────────────────┘
                     │ stdio/JSON-RPC
                     ▼
┌─────────────────────────────────────────────────────────┐
│              VS IDE MCP Server                           │
│  ┌─────────────────────────────────────────────────┐   │
│  │  MCP Protocol Handler (C# SDK)                  │   │
│  │  - Tools Registration                           │   │
│  │  - Resources Registration                       │   │
│  │  - Prompts Registration                         │   │
│  └─────────────────────────────────────────────────┘   │
│                                                          │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Core Service Layer                             │   │
│  │  - Solution Service                             │   │
│  │  - Workspace Service (Roslyn)                   │   │
│  │  - Diagnostic Service                           │   │
│  │  - Refactoring Service                          │   │
│  │  - Symbol Service                               │   │
│  │  - Navigation Service                           │   │
│  └─────────────────────────────────────────────────┘   │
│                                                          │
│  ┌─────────────────────────────────────────────────┐   │
│  │  VS Integration Layer                           │   │
│  │  - DTE Automation Bridge                        │   │
│  │  - Roslyn Workspace Manager                     │   │
│  │  - Document Manager                             │   │
│  │  - Build Manager                                │   │
│  └─────────────────────────────────────────────────┘   │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Visual Studio IDE                           │
│  - DTE2 Object Model                                    │
│  - Roslyn Workspace                                     │
│  - Code Analyzers                                       │
│  - Build System                                         │
└─────────────────────────────────────────────────────────┘
```

### 2.2 Communication Flow

1. **Initialization**: Claude Code starts the MCP server via VSPackage
2. **Capability Exchange**: Server advertises available tools/resources/prompts
3. **Request/Response**: Claude Code invokes tools, server executes against VS IDE
4. **Context Updates**: Server monitors VS IDE state changes (optional)
5. **Shutdown**: Graceful cleanup when Claude Code disconnects

---

## 3. Core Capabilities

### 3.1 Project & Solution Information

**Purpose**: Understand project structure, configuration, and dependencies

**Key Features**:
- Solution structure enumeration
- Project type identification (SDK-style, legacy, test projects)
- Target framework information
- NuGet package dependencies
- Project references and build order
- Configuration and platform settings

### 3.2 Code Navigation & Symbols

**Purpose**: Navigate and understand code structure

**Key Features**:
- Symbol search (types, methods, properties, fields)
- Go-to-definition resolution
- Find all references
- Call hierarchy (callers/callees)
- Type hierarchy (base types/derived types)
- Member enumeration
- Namespace exploration

### 3.3 Diagnostics & Analyzers

**Purpose**: Access real-time code quality information

**Key Features**:
- Error list retrieval (errors, warnings, info, suggestions)
- Roslyn analyzer diagnostics
- Code fix availability detection
- Suppression state
- Diagnostic severity levels
- Source location mapping
- Diagnostic filtering by project/file/severity

### 3.4 Code Analysis & Insights

**Purpose**: Deep code understanding via Roslyn

**Key Features**:
- Syntax tree analysis
- Semantic model queries
- Symbol information (accessibility, modifiers, attributes)
- Type information
- Control flow analysis
- Data flow analysis
- Code metrics (complexity, maintainability)

### 3.5 Refactoring Operations

**Purpose**: Safe code transformations

**Key Features**:
- Rename symbol (with preview)
- Extract method
- Extract interface
- Inline variable/method
- Move type to file
- Change signature
- Introduce parameter
- Preview changes before applying

### 3.6 Document Operations

**Purpose**: File and content management

**Key Features**:
- Open/close documents
- Get active document
- Read document content (with line ranges)
- Get document diagnostics
- Document state (dirty, read-only)
- Line/column to position mapping

### 3.7 Build & Test Integration

**Purpose**: Compilation and testing workflow

**Key Features**:
- Build solution/project
- Clean solution/project
- Build output capture
- Build diagnostics
- Test discovery
- Test execution
- Test results retrieval

---

## 4. MCP Primitives

### 4.1 Tools

Tools are functions Claude Code can invoke to perform actions. All tools return structured JSON responses.

#### 4.1.1 Solution & Project Tools

**`get_solution_info`**
```typescript
{
  name: "get_solution_info",
  description: "Get comprehensive information about the loaded solution",
  inputSchema: {
    type: "object",
    properties: {}
  }
}
```

Response:
```json
{
  "solutionPath": "C:\\Projects\\MyApp\\MyApp.sln",
  "name": "MyApp",
  "projects": [
    {
      "name": "MyApp.Core",
      "path": "C:\\Projects\\MyApp\\MyApp.Core\\MyApp.Core.csproj",
      "language": "C#",
      "projectType": "SDK",
      "targetFrameworks": ["net8.0"],
      "outputType": "Library",
      "references": ["MyApp.Common"],
      "packages": [
        { "name": "Newtonsoft.Json", "version": "13.0.3" }
      ]
    }
  ],
  "configurations": ["Debug", "Release"],
  "platforms": ["Any CPU"]
}
```

**`get_project_structure`**
```typescript
{
  name: "get_project_structure",
  description: "Get detailed structure of a specific project including files and folders",
  inputSchema: {
    type: "object",
    properties: {
      projectName: { type: "string", description: "Name of the project" }
    },
    required: ["projectName"]
  }
}
```

**`get_project_dependencies`**
```typescript
{
  name: "get_project_dependencies",
  description: "Get all dependencies (NuGet packages and project references) for a project",
  inputSchema: {
    type: "object",
    properties: {
      projectName: { type: "string", description: "Name of the project" }
    },
    required: ["projectName"]
  }
}
```

#### 4.1.2 Symbol & Navigation Tools

**`search_symbols`**
```typescript
{
  name: "search_symbols",
  description: "Search for symbols (types, methods, properties, etc.) across the solution",
  inputSchema: {
    type: "object",
    properties: {
      query: { 
        type: "string", 
        description: "Symbol name or pattern to search for" 
      },
      kind: { 
        type: "string", 
        enum: ["Class", "Interface", "Method", "Property", "Field", "Event", "Namespace", "All"],
        description: "Type of symbol to search for" 
      },
      projectName: {
        type: "string",
        description: "Optional: limit search to specific project"
      }
    },
    required: ["query"]
  }
}
```

Response:
```json
{
  "symbols": [
    {
      "name": "UserService",
      "kind": "Class",
      "containingNamespace": "MyApp.Services",
      "containingType": null,
      "filePath": "C:\\Projects\\MyApp\\Services\\UserService.cs",
      "line": 15,
      "column": 18,
      "accessibility": "Public",
      "modifiers": ["abstract"],
      "projectName": "MyApp.Core"
    }
  ],
  "totalCount": 1
}
```

**`get_symbol_details`**
```typescript
{
  name: "get_symbol_details",
  description: "Get comprehensive details about a specific symbol including documentation, members, and attributes",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string", description: "File containing the symbol" },
      line: { type: "integer", description: "Line number of the symbol" },
      column: { type: "integer", description: "Column number of the symbol" }
    },
    required: ["filePath", "line", "column"]
  }
}
```

**`find_references`**
```typescript
{
  name: "find_references",
  description: "Find all references to a symbol across the solution",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" }
    },
    required: ["filePath", "line", "column"]
  }
}
```

**`get_call_hierarchy`**
```typescript
{
  name: "get_call_hierarchy",
  description: "Get callers or callees of a method",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" },
      direction: { 
        type: "string", 
        enum: ["Callers", "Callees"],
        description: "Get methods that call this method (Callers) or methods called by this method (Callees)"
      }
    },
    required: ["filePath", "line", "column", "direction"]
  }
}
```

**`get_type_hierarchy`**
```typescript
{
  name: "get_type_hierarchy",
  description: "Get base types or derived types of a class/interface",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" },
      direction: { 
        type: "string", 
        enum: ["BaseTypes", "DerivedTypes"]
      }
    },
    required: ["filePath", "line", "column", "direction"]
  }
}
```

#### 4.1.3 Diagnostic Tools

**`get_diagnostics`**
```typescript
{
  name: "get_diagnostics",
  description: "Get all diagnostics (errors, warnings, suggestions) for the solution or specific file",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { 
        type: "string", 
        description: "Optional: filter diagnostics to specific file" 
      },
      projectName: { 
        type: "string", 
        description: "Optional: filter diagnostics to specific project" 
      },
      minSeverity: { 
        type: "string", 
        enum: ["Error", "Warning", "Info", "Suggestion"],
        description: "Minimum severity level to include"
      }
    }
  }
}
```

Response:
```json
{
  "diagnostics": [
    {
      "id": "CS0103",
      "severity": "Error",
      "message": "The name 'user' does not exist in the current context",
      "filePath": "C:\\Projects\\MyApp\\Controllers\\UserController.cs",
      "line": 42,
      "column": 16,
      "endLine": 42,
      "endColumn": 20,
      "projectName": "MyApp.Web",
      "hasCodeFix": false,
      "category": "Compiler"
    },
    {
      "id": "IDE0055",
      "severity": "Warning",
      "message": "Fix formatting",
      "filePath": "C:\\Projects\\MyApp\\Services\\UserService.cs",
      "line": 15,
      "column": 1,
      "hasCodeFix": true,
      "category": "Style"
    }
  ],
  "summary": {
    "errorCount": 1,
    "warningCount": 1,
    "infoCount": 0,
    "suggestionCount": 0
  }
}
```

**`get_available_code_fixes`**
```typescript
{
  name: "get_available_code_fixes",
  description: "Get available code fixes for a diagnostic at a specific location",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" }
    },
    required: ["filePath", "line", "column"]
  }
}
```

#### 4.1.4 Code Analysis Tools

**`analyze_method`**
```typescript
{
  name: "analyze_method",
  description: "Perform deep analysis on a method including complexity, data flow, and control flow",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      methodLine: { type: "integer", description: "Line where method is defined" }
    },
    required: ["filePath", "methodLine"]
  }
}
```

Response:
```json
{
  "methodName": "ProcessUser",
  "signature": "public async Task<bool> ProcessUser(User user)",
  "lineCount": 45,
  "cyclomaticComplexity": 8,
  "parameters": [
    {
      "name": "user",
      "type": "User",
      "isOptional": false
    }
  ],
  "returnType": "Task<bool>",
  "localVariables": [
    { "name": "result", "type": "bool" }
  ],
  "invocations": [
    { "method": "ValidateUser", "line": 12 },
    { "method": "SaveToDatabase", "line": 35 }
  ],
  "controlFlowInfo": {
    "hasLoops": true,
    "hasRecursion": false,
    "branchCount": 5
  }
}
```

**`get_type_info`**
```typescript
{
  name: "get_type_info",
  description: "Get comprehensive information about a type",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" }
    },
    required: ["filePath", "line", "column"]
  }
}
```

**`get_namespace_types`**
```typescript
{
  name: "get_namespace_types",
  description: "Get all types defined in a namespace",
  inputSchema: {
    type: "object",
    properties: {
      namespace: { type: "string", description: "Fully qualified namespace name" },
      projectName: { type: "string", description: "Optional: limit to specific project" }
    },
    required: ["namespace"]
  }
}
```

#### 4.1.5 Refactoring Tools

**`preview_rename`**
```typescript
{
  name: "preview_rename",
  description: "Preview all changes that would be made by renaming a symbol",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" },
      newName: { type: "string", description: "New name for the symbol" }
    },
    required: ["filePath", "line", "column", "newName"]
  }
}
```

Response:
```json
{
  "oldName": "userId",
  "newName": "customerId",
  "changes": [
    {
      "filePath": "C:\\Projects\\MyApp\\Services\\UserService.cs",
      "changes": [
        {
          "line": 15,
          "column": 20,
          "oldText": "userId",
          "newText": "customerId"
        }
      ]
    }
  ],
  "totalChanges": 12,
  "affectedFiles": 4
}
```

**`apply_rename`**
```typescript
{
  name: "apply_rename",
  description: "Apply a rename refactoring (requires preview first for safety)",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" },
      newName: { type: "string" }
    },
    required: ["filePath", "line", "column", "newName"]
  }
}
```

**`extract_method`**
```typescript
{
  name: "extract_method",
  description: "Extract selected code into a new method",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      startLine: { type: "integer" },
      startColumn: { type: "integer" },
      endLine: { type: "integer" },
      endColumn: { type: "integer" },
      methodName: { type: "string", description: "Name for the new method" }
    },
    required: ["filePath", "startLine", "startColumn", "endLine", "endColumn", "methodName"]
  }
}
```

**`apply_code_fix`**
```typescript
{
  name: "apply_code_fix",
  description: "Apply a specific code fix to resolve a diagnostic",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer" },
      column: { type: "integer" },
      fixIndex: { 
        type: "integer", 
        description: "Index of the code fix to apply (from get_available_code_fixes)" 
      }
    },
    required: ["filePath", "line", "column", "fixIndex"]
  }
}
```

#### 4.1.6 Document Tools

**`get_active_document`**
```typescript
{
  name: "get_active_document",
  description: "Get information about the currently active document in the editor",
  inputSchema: {
    type: "object",
    properties: {}
  }
}
```

**`read_document`**
```typescript
{
  name: "read_document",
  description: "Read the contents of a document",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      startLine: { type: "integer", description: "Optional: start line (1-based)" },
      endLine: { type: "integer", description: "Optional: end line (1-based)" }
    },
    required: ["filePath"]
  }
}
```

**`get_document_outline`**
```typescript
{
  name: "get_document_outline",
  description: "Get structural outline of a document (classes, methods, properties)",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" }
    },
    required: ["filePath"]
  }
}
```

**`open_document`**
```typescript
{
  name: "open_document",
  description: "Open a document in the Visual Studio editor",
  inputSchema: {
    type: "object",
    properties: {
      filePath: { type: "string" },
      line: { type: "integer", description: "Optional: line to navigate to" },
      column: { type: "integer", description: "Optional: column to navigate to" }
    },
    required: ["filePath"]
  }
}
```

#### 4.1.7 Build & Test Tools

**`build_solution`**
```typescript
{
  name: "build_solution",
  description: "Build the entire solution",
  inputSchema: {
    type: "object",
    properties: {
      configuration: { 
        type: "string", 
        description: "Build configuration (e.g., Debug, Release)" 
      },
      clean: { 
        type: "boolean", 
        description: "Whether to clean before building",
        default: false
      }
    }
  }
}
```

**`build_project`**
```typescript
{
  name: "build_project",
  description: "Build a specific project",
  inputSchema: {
    type: "object",
    properties: {
      projectName: { type: "string" },
      configuration: { type: "string" }
    },
    required: ["projectName"]
  }
}
```

**`get_build_output`**
```typescript
{
  name: "get_build_output",
  description: "Get the output from the last build operation",
  inputSchema: {
    type: "object",
    properties: {}
  }
}
```

**`discover_tests`**
```typescript
{
  name: "discover_tests",
  description: "Discover all tests in the solution or specific project",
  inputSchema: {
    type: "object",
    properties: {
      projectName: { type: "string", description: "Optional: limit to specific project" }
    }
  }
}
```

**`run_tests`**
```typescript
{
  name: "run_tests",
  description: "Run tests matching a filter",
  inputSchema: {
    type: "object",
    properties: {
      filter: { 
        type: "string", 
        description: "Test filter (e.g., FullyQualifiedName~MyApp.Tests.UserServiceTests)" 
      },
      projectName: { type: "string" }
    }
  }
}
```

### 4.2 Resources

Resources provide read-only access to data. They use URI-based identifiers.

**`vs://solution/info`**
- Current solution metadata
- Auto-refreshes when solution changes

**`vs://project/{projectName}/info`**
- Detailed project information
- Dependencies, references, files

**`vs://diagnostics/summary`**
- Real-time diagnostic summary
- Error/warning/info counts

**`vs://document/active`**
- Currently active document information
- Cursor position, selection, etc.

**`vs://symbols/{kind}`**
- All symbols of a specific kind in the solution
- e.g., `vs://symbols/classes`, `vs://symbols/interfaces`

### 4.3 Prompts

Prompts are pre-configured templates for common tasks.

**`analyze-errors`**
```typescript
{
  name: "analyze-errors",
  description: "Analyze all errors in the solution and suggest fixes",
  arguments: []
}
```

Template:
```
I need help fixing compilation errors in my Visual Studio solution. 
Please analyze all current errors and provide:
1. Root cause analysis for each error
2. Suggested fixes with code examples
3. Priority order for fixing (what to fix first)
4. Any potential side effects of the fixes
```

**`refactor-code`**
```typescript
{
  name: "refactor-code",
  description: "Get suggestions for refactoring code at a specific location",
  arguments: [
    { name: "filePath", required: true },
    { name: "line", required: true }
  ]
}
```

**`explain-symbol`**
```typescript
{
  name: "explain-symbol",
  description: "Get a detailed explanation of a symbol's purpose and usage",
  arguments: [
    { name: "filePath", required: true },
    { name: "line", required: true },
    { name: "column", required: true }
  ]
}
```

**`improve-tests`**
```typescript
{
  name: "improve-tests",
  description: "Analyze test coverage and suggest improvements",
  arguments: [
    { name: "projectName", required: false }
  ]
}
```

---

## 5. Implementation Details

### 5.1 VSPackage Structure

```csharp
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(UIContextGuids.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
public sealed class VsIdeMcpPackage : AsyncPackage
{
    public const string PackageGuidString = "your-guid-here";
    private McpServer _mcpServer;
    
    protected override async Task InitializeAsync(CancellationToken cancellationToken, 
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        
        // Get DTE
        var dte = await GetServiceAsync(typeof(DTE)) as DTE2;
        
        // Initialize Roslyn workspace
        var workspace = await GetServiceAsync(typeof(VisualStudioWorkspace)) 
            as VisualStudioWorkspace;
        
        // Create and start MCP server
        _mcpServer = new McpServer(dte, workspace);
        await _mcpServer.StartAsync(cancellationToken);
    }
    
    protected override void Dispose(bool disposing)
    {
        _mcpServer?.Dispose();
        base.Dispose(disposing);
    }
}
```

### 5.2 MCP Server Core

```csharp
public class McpServer : IDisposable
{
    private readonly DTE2 _dte;
    private readonly VisualStudioWorkspace _workspace;
    private readonly Server _server;
    
    public McpServer(DTE2 dte, VisualStudioWorkspace workspace)
    {
        _dte = dte;
        _workspace = workspace;
        
        // Initialize MCP server with stdio transport
        _server = new Server(
            new ServerOptions
            {
                Name = "visual-studio-ide",
                Version = "1.0.0"
            },
            new StdioServerTransport()
        );
        
        RegisterTools();
        RegisterResources();
        RegisterPrompts();
    }
    
    private void RegisterTools()
    {
        // Solution tools
        _server.AddTool(new Tool
        {
            Name = "get_solution_info",
            Description = "Get comprehensive information about the loaded solution",
            InputSchema = new JsonSchema { /* ... */ },
            Handler = GetSolutionInfoHandler
        });
        
        // Symbol tools
        _server.AddTool(new Tool
        {
            Name = "search_symbols",
            Description = "Search for symbols across the solution",
            InputSchema = new JsonSchema { /* ... */ },
            Handler = SearchSymbolsHandler
        });
        
        // Add remaining tools...
    }
    
    private async Task<ToolResult> GetSolutionInfoHandler(ToolCall call)
    {
        var service = new SolutionService(_dte, _workspace);
        var info = await service.GetSolutionInfoAsync();
        
        return new ToolResult
        {
            Content = new[]
            {
                new Content
                {
                    Type = "text",
                    Text = JsonSerializer.Serialize(info, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    })
                }
            }
        };
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _server.RunAsync(cancellationToken);
    }
    
    public void Dispose()
    {
        _server?.Dispose();
    }
}
```

### 5.3 Solution Service Implementation

```csharp
public class SolutionService
{
    private readonly DTE2 _dte;
    private readonly VisualStudioWorkspace _workspace;
    
    public SolutionService(DTE2 dte, VisualStudioWorkspace workspace)
    {
        _dte = dte;
        _workspace = workspace;
    }
    
    public async Task<SolutionInfo> GetSolutionInfoAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        
        var solution = _dte.Solution;
        if (solution == null)
        {
            throw new InvalidOperationException("No solution is currently loaded");
        }
        
        var roslynSolution = _workspace.CurrentSolution;
        
        var projects = new List<ProjectInfo>();
        foreach (Project project in solution.Projects)
        {
            var projectInfo = await GetProjectInfoAsync(project, roslynSolution);
            projects.Add(projectInfo);
        }
        
        return new SolutionInfo
        {
            SolutionPath = solution.FullName,
            Name = Path.GetFileNameWithoutExtension(solution.FullName),
            Projects = projects,
            Configurations = GetConfigurations(solution),
            Platforms = GetPlatforms(solution)
        };
    }
    
    private async Task<ProjectInfo> GetProjectInfoAsync(
        Project vsProject, 
        Solution roslynSolution)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        
        // Find corresponding Roslyn project
        var roslynProject = roslynSolution.Projects
            .FirstOrDefault(p => p.FilePath == vsProject.FullName);
        
        return new ProjectInfo
        {
            Name = vsProject.Name,
            Path = vsProject.FullName,
            Language = vsProject.CodeModel?.Language ?? "Unknown",
            ProjectType = GetProjectType(vsProject),
            TargetFrameworks = GetTargetFrameworks(roslynProject),
            OutputType = GetOutputType(vsProject),
            References = await GetReferencesAsync(roslynProject),
            Packages = await GetPackagesAsync(roslynProject)
        };
    }
    
    // ... additional helper methods
}
```

### 5.4 Symbol Service Implementation

```csharp
public class SymbolService
{
    private readonly VisualStudioWorkspace _workspace;
    
    public SymbolService(VisualStudioWorkspace workspace)
    {
        _workspace = workspace;
    }
    
    public async Task<List<SymbolInfo>> SearchSymbolsAsync(
        string query, 
        SymbolKind? kind = null,
        string projectName = null)
    {
        var solution = _workspace.CurrentSolution;
        var results = new List<SymbolInfo>();
        
        var projects = string.IsNullOrEmpty(projectName)
            ? solution.Projects
            : solution.Projects.Where(p => p.Name == projectName);
        
        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;
            
            var symbols = GetSymbols(compilation, query, kind);
            
            foreach (var symbol in symbols)
            {
                var location = symbol.Locations.FirstOrDefault();
                if (location?.SourceTree == null) continue;
                
                var lineSpan = location.GetLineSpan();
                
                results.Add(new SymbolInfo
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind.ToString(),
                    ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString(),
                    ContainingType = symbol.ContainingType?.Name,
                    FilePath = location.SourceTree.FilePath,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Accessibility = symbol.DeclaredAccessibility.ToString(),
                    Modifiers = GetModifiers(symbol),
                    ProjectName = project.Name
                });
            }
        }
        
        return results;
    }
    
    private IEnumerable<ISymbol> GetSymbols(
        Compilation compilation, 
        string query, 
        SymbolKind? kind)
    {
        var visitor = new SymbolVisitor(query, kind);
        
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            
            visitor.Visit(root, semanticModel);
        }
        
        return visitor.Symbols;
    }
    
    public async Task<List<ReferenceInfo>> FindReferencesAsync(
        string filePath, 
        int line, 
        int column)
    {
        var document = GetDocument(filePath);
        if (document == null) return new List<ReferenceInfo>();
        
        var semanticModel = await document.GetSemanticModelAsync();
        var root = await document.GetSyntaxRootAsync();
        
        var position = GetPosition(root, line, column);
        var node = root.FindNode(new TextSpan(position, 0));
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        
        if (symbol == null) return new List<ReferenceInfo>();
        
        var references = await SymbolFinder.FindReferencesAsync(
            symbol, 
            _workspace.CurrentSolution);
        
        var results = new List<ReferenceInfo>();
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                var lineSpan = location.Location.GetLineSpan();
                results.Add(new ReferenceInfo
                {
                    FilePath = lineSpan.Path,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Context = await GetContextAsync(location.Location)
                });
            }
        }
        
        return results;
    }
}
```

### 5.5 Diagnostic Service Implementation

```csharp
public class DiagnosticService
{
    private readonly VisualStudioWorkspace _workspace;
    
    public DiagnosticService(VisualStudioWorkspace workspace)
    {
        _workspace = workspace;
    }
    
    public async Task<DiagnosticsResult> GetDiagnosticsAsync(
        string filePath = null,
        string projectName = null,
        DiagnosticSeverity? minSeverity = null)
    {
        var solution = _workspace.CurrentSolution;
        var allDiagnostics = new List<DiagnosticInfo>();
        
        var projects = string.IsNullOrEmpty(projectName)
            ? solution.Projects
            : solution.Projects.Where(p => p.Name == projectName);
        
        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;
            
            var diagnostics = compilation.GetDiagnostics();
            
            // Add analyzer diagnostics
            if (project.AnalyzerReferences.Any())
            {
                var analyzers = project.AnalyzerReferences
                    .SelectMany(r => r.GetAnalyzers(project.Language))
                    .ToImmutableArray();
                
                var compilationWithAnalyzers = compilation
                    .WithAnalyzers(analyzers);
                
                var analyzerDiagnostics = await compilationWithAnalyzers
                    .GetAllDiagnosticsAsync();
                
                diagnostics = diagnostics.AddRange(analyzerDiagnostics);
            }
            
            foreach (var diagnostic in diagnostics)
            {
                if (minSeverity.HasValue && 
                    diagnostic.Severity < minSeverity.Value)
                    continue;
                
                if (!string.IsNullOrEmpty(filePath) && 
                    diagnostic.Location.SourceTree?.FilePath != filePath)
                    continue;
                
                var lineSpan = diagnostic.Location.GetLineSpan();
                
                allDiagnostics.Add(new DiagnosticInfo
                {
                    Id = diagnostic.Id,
                    Severity = diagnostic.Severity.ToString(),
                    Message = diagnostic.GetMessage(),
                    FilePath = lineSpan.Path,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                    ProjectName = project.Name,
                    HasCodeFix = await HasCodeFixAsync(diagnostic, project),
                    Category = diagnostic.Category
                });
            }
        }
        
        return new DiagnosticsResult
        {
            Diagnostics = allDiagnostics,
            Summary = new DiagnosticSummary
            {
                ErrorCount = allDiagnostics.Count(d => d.Severity == "Error"),
                WarningCount = allDiagnostics.Count(d => d.Severity == "Warning"),
                InfoCount = allDiagnostics.Count(d => d.Severity == "Info"),
                SuggestionCount = allDiagnostics.Count(d => d.Severity == "Hidden")
            }
        };
    }
    
    private async Task<bool> HasCodeFixAsync(
        Diagnostic diagnostic, 
        Project project)
    {
        var document = project.GetDocument(diagnostic.Location.SourceTree);
        if (document == null) return false;
        
        var codeFixProviders = GetCodeFixProviders(project.Language);
        
        foreach (var provider in codeFixProviders)
        {
            if (provider.FixableDiagnosticIds.Contains(diagnostic.Id))
            {
                return true;
            }
        }
        
        return false;
    }
}
```

### 5.6 Refactoring Service Implementation

```csharp
public class RefactoringService
{
    private readonly VisualStudioWorkspace _workspace;
    
    public RefactoringService(VisualStudioWorkspace workspace)
    {
        _workspace = workspace;
    }
    
    public async Task<RenamePreview> PreviewRenameAsync(
        string filePath,
        int line,
        int column,
        string newName)
    {
        var document = GetDocument(filePath);
        if (document == null)
            throw new ArgumentException("Document not found", nameof(filePath));
        
        var root = await document.GetSyntaxRootAsync();
        var position = GetPosition(root, line, column);
        var node = root.FindNode(new TextSpan(position, 0));
        
        var semanticModel = await document.GetSemanticModelAsync();
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        
        if (symbol == null)
            throw new InvalidOperationException("No symbol found at location");
        
        // Perform rename operation
        var solution = _workspace.CurrentSolution;
        var newSolution = await Renamer.RenameSymbolAsync(
            solution,
            symbol,
            newName,
            solution.Workspace.Options);
        
        // Collect changes
        var changes = new List<FileChange>();
        var solutionChanges = newSolution.GetChanges(solution);
        
        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                var oldDoc = solution.GetDocument(documentId);
                var newDoc = newSolution.GetDocument(documentId);
                
                var oldText = await oldDoc.GetTextAsync();
                var newText = await newDoc.GetTextAsync();
                
                var textChanges = newText.GetTextChanges(oldText);
                
                changes.Add(new FileChange
                {
                    FilePath = oldDoc.FilePath,
                    Changes = textChanges.Select(tc => new Change
                    {
                        Line = oldText.Lines.GetLinePosition(tc.Span.Start).Line + 1,
                        Column = oldText.Lines.GetLinePosition(tc.Span.Start).Character + 1,
                        OldText = tc.Span.IsEmpty ? "" : oldText.ToString(tc.Span),
                        NewText = tc.NewText
                    }).ToList()
                });
            }
        }
        
        return new RenamePreview
        {
            OldName = symbol.Name,
            NewName = newName,
            Changes = changes,
            TotalChanges = changes.Sum(c => c.Changes.Count),
            AffectedFiles = changes.Count
        };
    }
    
    public async Task<bool> ApplyRenameAsync(
        string filePath,
        int line,
        int column,
        string newName)
    {
        try
        {
            var preview = await PreviewRenameAsync(filePath, line, column, newName);
            
            // Apply the rename
            var document = GetDocument(filePath);
            var root = await document.GetSyntaxRootAsync();
            var position = GetPosition(root, line, column);
            var node = root.FindNode(new TextSpan(position, 0));
            
            var semanticModel = await document.GetSemanticModelAsync();
            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            
            var solution = _workspace.CurrentSolution;
            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                newName,
                solution.Workspace.Options);
            
            // Apply changes to workspace
            return _workspace.TryApplyChanges(newSolution);
        }
        catch
        {
            return false;
        }
    }
}
```

### 5.7 Configuration File (.mcp.json)

```json
{
  "mcpServers": {
    "visual-studio-ide": {
      "command": "VisualStudio.exe",
      "args": ["/mcp"],
      "env": {
        "VSINSTALLDIR": "C:\\Program Files\\Microsoft Visual Studio\\2022\\Professional"
      },
      "description": "Visual Studio IDE integration for Claude Code",
      "capabilities": {
        "tools": true,
        "resources": true,
        "prompts": true
      }
    }
  }
}
```

---

## 6. Security Considerations

### 6.1 Safety Mechanisms

1. **Read-Only by Default**
   - Most operations are read-only
   - Write operations require explicit confirmation

2. **Preview Before Apply**
   - All refactoring operations provide preview
   - User must explicitly apply changes

3. **Scope Limiting**
   - Operations limited to loaded solution
   - No file system access outside solution

4. **Error Handling**
   - All operations wrapped in try-catch
   - Detailed error messages
   - Graceful degradation

5. **Permission Model**
   - Sensitive operations logged
   - User consent for destructive actions

### 6.2 Data Privacy

- No code sent to external services
- All processing local to VS instance
- MCP communication via local stdio only
- No telemetry beyond VS standard

### 6.3 Resource Management

- Asynchronous operations
- Cancellation token support
- Proper disposal of resources
- Memory-efficient symbol caching

---

## 7. Usage Examples

### 7.1 Example: Finding and Fixing Errors

**Claude Code Interaction:**
```
User: "Check for any compiler errors in the solution"

Claude Code: <calls tool: get_diagnostics with minSeverity="Error">

Response shows 3 errors in UserService.cs

Claude Code: <calls tool: read_document for UserService.cs>
              <calls tool: get_symbol_details for error locations>
              
Claude analyzes the code and identifies missing null check

Claude Code: "I found 3 errors in UserService.cs. They're all related to 
              potential null reference exceptions. Would you like me to 
              suggest fixes?"

User: "Yes, please"

Claude Code: <generates code with null checks>
             "Here's the corrected code. Shall I apply these changes?"
```

### 7.2 Example: Refactoring a Method

**Claude Code Interaction:**
```
User: "I want to rename the ProcessUser method to ProcessCustomer everywhere"

Claude Code: <calls tool: search_symbols with query="ProcessUser", kind="Method">

Found 1 method definition

Claude Code: <calls tool: preview_rename>

Response shows 12 changes across 4 files

Claude Code: "I found 12 references to ProcessUser across 4 files:
              - UserService.cs (1 definition + 3 uses)
              - UserController.cs (5 uses)
              - UserTests.cs (2 uses)
              - IUserService.cs (1 interface definition)
              
              Would you like me to proceed with renaming all occurrences 
              to ProcessCustomer?"

User: "Yes"

Claude Code: <calls tool: apply_rename>
             "Successfully renamed ProcessUser to ProcessCustomer 
              across all files."
```

### 7.3 Example: Analyzing Complex Code

**Claude Code Interaction:**
```
User: "Explain what the CalculateDiscount method does"

Claude Code: <calls tool: get_active_document>
             <calls tool: search_symbols with query="CalculateDiscount">
             <calls tool: get_symbol_details at method location>
             <calls tool: analyze_method>
             
Response includes:
- Method signature
- Cyclomatic complexity: 12 (high)
- Parameters and return type
- Called methods
- Control flow information

Claude Code: "The CalculateDiscount method calculates customer discounts 
              based on purchase history and membership tier. However, I 
              notice it has high complexity (12). The method:
              
              1. Checks customer membership tier (Gold, Silver, Bronze)
              2. Calculates base discount percentage
              3. Applies purchase history multipliers
              4. Handles special promotional periods
              5. Caps the maximum discount at 50%
              
              The high complexity suggests this method could benefit from 
              refactoring. Would you like me to suggest how to simplify it?"
```

### 7.4 Example: Running Tests

**Claude Code Interaction:**
```
User: "Run all tests for the UserService class"

Claude Code: <calls tool: discover_tests>
             <calls tool: run_tests with filter="*UserService*">
             
Response shows:
- 15 tests discovered
- 13 passed
- 2 failed

Claude Code: <calls tool: get_diagnostics for test project>

"I ran 15 tests for UserService. 13 passed but 2 failed:

1. UserService_ProcessUser_WithNullInput_ThrowsException
   - Expected ArgumentNullException but got NullReferenceException
   
2. UserService_CalculateDiscount_WithGoldMember_ReturnsCorrectAmount
   - Expected 25.0 but got 20.0

Would you like me to investigate these failures and suggest fixes?"
```

---

## 8. Future Enhancements

### 8.1 Phase 2 Features

- **Debugger Integration**
  - Set/remove breakpoints
  - Step through code
  - Inspect variables
  - Call stack navigation

- **Code Generation**
  - Scaffold classes/interfaces
  - Generate unit tests
  - Create implementations from interfaces

- **Advanced Analysis**
  - Code smell detection
  - Architecture analysis
  - Dependency graph visualization

### 8.2 Phase 3 Features

- **Git Integration**
  - Commit history analysis
  - Blame information
  - Branch operations

- **NuGet Management**
  - Package search
  - Package updates
  - Vulnerability scanning

- **Performance Profiling**
  - Hot path identification
  - Memory usage analysis
  - Performance recommendations

---

## 9. Conclusion

This specification defines a comprehensive MCP server that exposes Visual Studio IDE's most valuable capabilities to Claude Code, enabling powerful AI-assisted pair programming workflows. The design prioritizes safety, performance, and developer experience while providing the essential tools needed for effective code navigation, understanding, and refactoring in .NET/C# projects.

The modular architecture allows for iterative implementation, starting with core features and progressively adding advanced capabilities. The use of Roslyn APIs ensures deep, semantic understanding of C# code, while the DTE automation provides access to the full Visual Studio ecosystem.

---

## Appendix A: Data Models

### SolutionInfo
```csharp
public class SolutionInfo
{
    public string SolutionPath { get; set; }
    public string Name { get; set; }
    public List<ProjectInfo> Projects { get; set; }
    public List<string> Configurations { get; set; }
    public List<string> Platforms { get; set; }
}
```

### ProjectInfo
```csharp
public class ProjectInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Language { get; set; }
    public string ProjectType { get; set; }
    public List<string> TargetFrameworks { get; set; }
    public string OutputType { get; set; }
    public List<string> References { get; set; }
    public List<PackageReference> Packages { get; set; }
}
```

### SymbolInfo
```csharp
public class SymbolInfo
{
    public string Name { get; set; }
    public string Kind { get; set; }
    public string ContainingNamespace { get; set; }
    public string ContainingType { get; set; }
    public string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string Accessibility { get; set; }
    public List<string> Modifiers { get; set; }
    public string ProjectName { get; set; }
}
```

### DiagnosticInfo
```csharp
public class DiagnosticInfo
{
    public string Id { get; set; }
    public string Severity { get; set; }
    public string Message { get; set; }
    public string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string ProjectName { get; set; }
    public bool HasCodeFix { get; set; }
    public string Category { get; set; }
}
```

---

## Appendix B: Error Codes

| Code | Description |
|------|-------------|
| VS001 | No solution loaded |
| VS002 | Project not found |
| VS003 | File not found |
| VS004 | Symbol not found |
| VS005 | Invalid line/column |
| VS006 | Compilation error |
| VS007 | Refactoring failed |
| VS008 | Build failed |
| VS009 | Document not open |
| VS010 | Operation cancelled |

---

## Appendix C: References

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Claude Code Documentation](https://docs.claude.com/en/docs/claude-code)
- [Visual Studio Extensibility Documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/)
- [Roslyn API Documentation](https://github.com/dotnet/roslyn/tree/main/docs)
- [EnvDTE Reference](https://learn.microsoft.com/en-us/dotnet/api/envdte)
- [Microsoft.CodeAnalysis Documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis)

---

**Document Version**: 1.0  
**Last Updated**: October 25, 2025  
**Author**: Claude (Anthropic)  
**Status**: Specification - Ready for Implementation
