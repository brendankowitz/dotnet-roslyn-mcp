using System.Collections.Generic;

namespace VsIdeMcp.Models
{
    /// <summary>
    /// Information about a Visual Studio solution
    /// </summary>
    public class SolutionInfo
    {
        public string SolutionPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<ProjectInfo> Projects { get; set; } = new();
        public List<string> Configurations { get; set; } = new();
        public List<string> Platforms { get; set; } = new();
    }

    /// <summary>
    /// Information about a project
    /// </summary>
    public class ProjectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string ProjectType { get; set; } = string.Empty;
        public List<string> TargetFrameworks { get; set; } = new();
        public string OutputType { get; set; } = string.Empty;
        public List<string> References { get; set; } = new();
        public List<PackageReference> Packages { get; set; } = new();
    }

    /// <summary>
    /// NuGet package reference
    /// </summary>
    public class PackageReference
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
