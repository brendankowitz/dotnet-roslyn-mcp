# Build & Package Guide

This document describes how to build, package, and publish the Roslyn MCP Server.

## Project Structure

```
vs-ide-mcp/
├── .github/
│   └── workflows/
│       ├── build.yml           # CI build workflow
│       └── publish.yml         # NuGet publish workflow
├── docs/
│   ├── SPECIFICATION.md        # Technical specification
│   └── QUICK_REFERENCE.md      # Quick reference guide
├── src/
│   ├── Configuration/          # (Reserved for future config classes)
│   ├── Models/                 # (Reserved for future DTOs)
│   ├── Services/               # (Reserved for future service classes)
│   ├── Tools/                  # (Reserved for future tool classes)
│   ├── Program.cs              # Entry point
│   ├── McpServer.cs            # MCP protocol handler
│   ├── RoslynService.cs        # Roslyn service implementation
│   ├── RoslynMcp.csproj        # Project file
│   └── server.json             # MCP server configuration
├── GitVersion.yml              # Semantic versioning configuration
├── RoslynMcp.sln               # Solution file
└── README.md                   # Main documentation
```

## Prerequisites

- **.NET 8.0 SDK** or later
- **Git** (for GitVersion)
- **Visual Studio Build Tools 2022** or **Visual Studio 2022** (for MSBuild)

## Local Build

### Restore Dependencies

```bash
dotnet restore RoslynMcp.sln
```

### Build

```bash
# Debug build
dotnet build RoslynMcp.sln

# Release build
dotnet build RoslynMcp.sln -c Release
```

### Run Locally

```bash
# Run from source
dotnet run --project src/RoslynMcp.csproj

# Set environment variables
DOTNET_SOLUTION_PATH=/path/to/solution.sln dotnet run --project src/RoslynMcp.csproj
```

## Packaging

### Create NuGet Package

```bash
# Pack to ./artifacts directory
dotnet pack src/RoslynMcp.csproj -c Release --output ./artifacts

# Pack with specific version
dotnet pack src/RoslynMcp.csproj -c Release --output ./artifacts /p:Version=1.2.3
```

### Install Locally

```bash
# Install from local package
dotnet tool install --global --add-source ./artifacts RoslynMcp

# Update existing installation
dotnet tool update --global --add-source ./artifacts RoslynMcp

# Uninstall
dotnet tool uninstall --global RoslynMcp
```

### Verify Installation

```bash
# Check if tool is installed
dotnet tool list --global | grep RoslynMcp

# Run the tool
dotnetroslyn-mcp --version
```

## Versioning with GitVersion

The project uses **GitVersion** for semantic versioning based on Git history.

### GitVersion Configuration

See `GitVersion.yml` for branch-specific versioning rules:

- **main**: Production releases (e.g., 1.0.0)
- **dev**: Development pre-releases (e.g., 1.1.0-alpha.5)
- **feature/***: Feature branches (e.g., 1.1.0-myfeature.3)
- **release/***: Release candidates (e.g., 1.1.0-beta.2)
- **hotfix/***: Hotfix releases (e.g., 1.0.1-beta.1)

### Manual Version Override

```bash
# Build with specific version
dotnet build -c Release /p:Version=2.0.0

# Pack with specific version
dotnet pack -c Release /p:Version=2.0.0 --output ./artifacts
```

## GitHub Actions Workflows

### Build Workflow

**Triggers:**
- Push to `main` or `dev` branches
- Pull requests to `main` or `dev`
- Manual trigger via `workflow_call`

**Steps:**
1. Checkout code with full history
2. Setup .NET SDK
3. Install and run GitVersion
4. Restore dependencies
5. Build solution
6. Run tests (if any)
7. Create NuGet package
8. Upload artifacts (7-day retention)

**Usage:**
```bash
# Triggered automatically on push/PR
# View results in GitHub Actions tab
```

### Publish Workflow

**Triggers:**
- GitHub release published
- Manual workflow dispatch

**Steps:**
1. Checkout code with full history
2. Setup .NET SDK
3. Determine version (GitVersion or manual input)
4. Build and pack
5. Publish to NuGet.org
6. Upload artifacts (90-day retention)

**Setup:**
1. Create NuGet API key at https://www.nuget.org/account/apikeys
2. Add to GitHub Secrets as `NUGET_API_KEY`

**Usage:**

*Via GitHub Release:*
```bash
# 1. Tag a release
git tag v1.0.0
git push origin v1.0.0

# 2. Create GitHub Release from tag
# 3. Publish workflow runs automatically
```

*Via Manual Dispatch:*
```bash
# Use GitHub Actions UI:
# 1. Go to Actions > Publish to NuGet
# 2. Click "Run workflow"
# 3. Optionally specify version or leave empty for GitVersion
```

## Publishing to NuGet.org

### Prerequisites

1. Create account at https://www.nuget.org
2. Generate API key with "Push new packages and package versions" scope
3. Add API key to GitHub Secrets as `NUGET_API_KEY`

### Manual Publish

```bash
# Create package
dotnet pack src/RoslynMcp.csproj -c Release --output ./artifacts /p:Version=1.0.0

# Publish to NuGet
dotnet nuget push ./artifacts/RoslynMcp.1.0.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### Automated Publish (Recommended)

1. Create and push a Git tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. Create a GitHub Release from the tag

3. The publish workflow runs automatically and pushes to NuGet

## Package Contents

The generated NuGet package includes:

- **Tool executable**: `dotnetroslyn-mcp`
- **Dependencies**: Roslyn SDK, MCP SDK, Microsoft.Extensions.Hosting
- **README.md**: Package documentation
- **server.json**: Example MCP configuration
- **License**: MIT License

## Testing the Package

### Test Installation

```bash
# Install from local artifacts
dotnet tool install --global --add-source ./artifacts RoslynMcp

# Verify installation
which dotnetroslyn-mcp  # Linux/macOS
where dotnetroslyn-mcp  # Windows
```

### Test with Claude Code

```bash
# Add to Claude Code
claude mcp add --transport stdio roslyn \
  --env DOTNET_SOLUTION_PATH="/path/to/solution.sln" \
  -- dotnetroslyn-mcp

# Test with a prompt
# Open Claude Code and try: "Find all references to Program"
```

### Test JSON-RPC Manually

```bash
# Run the server
dotnetroslyn-mcp

# In another terminal, send requests
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | dotnetroslyn-mcp
```

## Troubleshooting

### Build Errors

**Error: Cannot find .NET SDK**
```bash
# Install .NET 8.0 SDK
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0
```

**Error: MSBuild not found**
```bash
# Install Visual Studio Build Tools 2022
# Or install full Visual Studio 2022
```

**Error: GitVersion failed**
```bash
# Ensure you have at least one commit
git commit --allow-empty -m "Initial commit"
```

### Package Errors

**Error: Package already exists**
```bash
# Use --skip-duplicate flag
dotnet nuget push ./artifacts/*.nupkg --skip-duplicate ...
```

**Error: API key unauthorized**
```bash
# Verify API key has correct scopes:
# - Push new packages and package versions
# Regenerate key if needed at https://www.nuget.org/account/apikeys
```

## Development Workflow

### Recommended Workflow

1. **Create feature branch**
   ```bash
   git checkout -b feature/my-feature
   ```

2. **Make changes and commit**
   ```bash
   git add .
   git commit -m "feat: add new feature"
   ```

3. **Test locally**
   ```bash
   dotnet build
   dotnet pack -c Release --output ./artifacts
   dotnet tool install --global --add-source ./artifacts RoslynMcp
   ```

4. **Push and create PR**
   ```bash
   git push origin feature/my-feature
   # Create PR via GitHub UI
   ```

5. **Merge to dev**
   ```bash
   # After PR approval, merge to dev
   # CI builds and creates alpha package
   ```

6. **Release**
   ```bash
   # Create release branch
   git checkout -b release/1.0.0

   # Merge to main
   # Create GitHub Release
   # Publish workflow pushes to NuGet
   ```

## Support

- **Build Issues**: Check GitHub Actions logs
- **Package Issues**: Check NuGet.org package page
- **Version Issues**: Check GitVersion.yml configuration
- **General Issues**: Create GitHub Issue
