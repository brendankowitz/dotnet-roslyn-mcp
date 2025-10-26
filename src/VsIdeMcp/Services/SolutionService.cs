using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using VsIdeMcp.Models;
using Project = EnvDTE.Project;
using Task = System.Threading.Tasks.Task;

namespace VsIdeMcp.Services
{
    /// <summary>
    /// Service for accessing Visual Studio solution and project information
    /// </summary>
    public class SolutionService
    {
        private readonly DTE2 _dte;
        private readonly VisualStudioWorkspace? _workspace;

        public SolutionService(DTE2 dte, VisualStudioWorkspace? workspace)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _workspace = workspace; // Workspace can be null - will be limited functionality
        }

        /// <summary>
        /// Gets comprehensive information about the loaded solution
        /// </summary>
        public async Task<Models.SolutionInfo> GetSolutionInfoAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var solution = _dte.Solution;
            if (solution == null || string.IsNullOrEmpty(solution.FullName))
            {
                return new Models.SolutionInfo
                {
                    Name = "No solution loaded",
                    SolutionPath = string.Empty
                };
            }

            var roslynSolution = _workspace.CurrentSolution;

            var solutionInfo = new Models.SolutionInfo
            {
                SolutionPath = solution.FullName,
                Name = Path.GetFileNameWithoutExtension(solution.FullName),
                Configurations = GetConfigurations(solution),
                Platforms = GetPlatforms(solution)
            };

            // Get all projects
            var projects = new List<Models.ProjectInfo>();
            foreach (Project project in solution.Projects)
            {
                try
                {
                    var projectInfo = await GetProjectInfoAsync(project, roslynSolution);
                    if (projectInfo != null)
                    {
                        projects.Add(projectInfo);
                    }
                }
                catch
                {
                    // Skip projects that can't be loaded
                }
            }

            solutionInfo.Projects = projects;
            return solutionInfo;
        }

        /// <summary>
        /// Gets information about a specific project
        /// </summary>
        private async Task<Models.ProjectInfo?> GetProjectInfoAsync(Project vsProject, Microsoft.CodeAnalysis.Solution roslynSolution)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (vsProject == null || string.IsNullOrEmpty(vsProject.FullName))
            {
                return null;
            }

            // Find corresponding Roslyn project
            var roslynProject = roslynSolution.Projects
                .FirstOrDefault(p => p.FilePath?.Equals(vsProject.FullName, StringComparison.OrdinalIgnoreCase) == true);

            var projectInfo = new Models.ProjectInfo
            {
                Name = vsProject.Name,
                Path = vsProject.FullName,
                Language = vsProject.CodeModel?.Language ?? "Unknown"
            };

            if (roslynProject != null)
            {
                // Get target frameworks
                projectInfo.TargetFrameworks = new List<string>();
                if (roslynProject.ParseOptions is Microsoft.CodeAnalysis.CSharp.CSharpParseOptions csharpOptions)
                {
                    projectInfo.TargetFrameworks.Add(csharpOptions.LanguageVersion.ToString());
                }

                // Get compilation options
                if (roslynProject.CompilationOptions != null)
                {
                    projectInfo.OutputType = roslynProject.CompilationOptions.OutputKind.ToString();
                }

                // Get project references
                projectInfo.References = roslynProject.ProjectReferences
                    .Select(pr => roslynSolution.GetProject(pr.ProjectId)?.Name ?? "Unknown")
                    .Where(name => name != "Unknown")
                    .ToList();

                // Get NuGet packages
                projectInfo.Packages = roslynProject.MetadataReferences
                    .OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
                    .Select(r => r.FilePath)
                    .Where(path => path != null && path.Contains("packages"))
                    .Select(path => ExtractPackageInfo(path!))
                    .Where(pkg => pkg != null)
                    .Distinct()
                    .ToList()!;
            }

            // Determine project type
            projectInfo.ProjectType = DetermineProjectType(vsProject);

            return projectInfo;
        }

        /// <summary>
        /// Gets solution configurations
        /// </summary>
        private List<string> GetConfigurations(EnvDTE.Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var configurations = new List<string>();
            try
            {
                if (solution.SolutionBuild?.SolutionConfigurations != null)
                {
                    foreach (SolutionConfiguration2 config in solution.SolutionBuild.SolutionConfigurations)
                    {
                        if (!configurations.Contains(config.Name))
                        {
                            configurations.Add(config.Name);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return configurations;
        }

        /// <summary>
        /// Gets solution platforms
        /// </summary>
        private List<string> GetPlatforms(EnvDTE.Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var platforms = new List<string>();
            try
            {
                if (solution.SolutionBuild?.SolutionConfigurations != null)
                {
                    foreach (SolutionConfiguration2 config in solution.SolutionBuild.SolutionConfigurations)
                    {
                        if (!platforms.Contains(config.PlatformName))
                        {
                            platforms.Add(config.PlatformName);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return platforms;
        }

        /// <summary>
        /// Determines the project type (SDK-style, legacy, etc.)
        /// </summary>
        private string DetermineProjectType(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var projectPath = project.FullName;
                if (File.Exists(projectPath))
                {
                    var content = File.ReadAllText(projectPath);
                    if (content.Contains("<Project Sdk="))
                    {
                        return "SDK";
                    }
                }
                return "Legacy";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Extracts package name and version from a file path
        /// </summary>
        private PackageReference? ExtractPackageInfo(string path)
        {
            try
            {
                var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                var packagesIndex = Array.FindIndex(parts, p => p.Equals("packages", StringComparison.OrdinalIgnoreCase));

                if (packagesIndex >= 0 && packagesIndex + 1 < parts.Length)
                {
                    var packagePart = parts[packagesIndex + 1];
                    var lastDot = packagePart.LastIndexOf('.');

                    if (lastDot > 0)
                    {
                        var name = packagePart.Substring(0, lastDot);
                        var version = packagePart.Substring(lastDot + 1);

                        return new PackageReference
                        {
                            Name = name,
                            Version = version
                        };
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return null;
        }
    }
}
