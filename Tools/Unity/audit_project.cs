if (UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
    throw new System.InvalidOperationException("Аудит запускается только в готовом редакторе вне Play Mode.");
var output = "Logs/verify/project-audit";
System.IO.Directory.CreateDirectory(output);
var statusPath = output + "/status.json";
var modules = new System.Collections.Generic.List<object>();
var parameters = new Unity.ProjectAuditor.Editor.AnalysisParams
{
    AssemblyNames = new[] { "FrostSpace.Core", "FrostSpace.Features", "FrostSpace.Editor", "FrostSpace.Tests.EditMode" },
    CodeAnalysisFlags = Unity.ProjectAuditor.Editor.CodeAnalysisFlags.All,
    Categories = new Unity.ProjectAuditor.Editor.Core.SerializableEnum<Unity.ProjectAuditor.Editor.IssueCategory>[]
    {
        Unity.ProjectAuditor.Editor.IssueCategory.Code,
        Unity.ProjectAuditor.Editor.IssueCategory.ObsoleteAPI,
        Unity.ProjectAuditor.Editor.IssueCategory.CodeCompilerMessage,
        Unity.ProjectAuditor.Editor.IssueCategory.ProjectSetting,
        Unity.ProjectAuditor.Editor.IssueCategory.AssetIssue,
        Unity.ProjectAuditor.Editor.IssueCategory.Mesh,
        Unity.ProjectAuditor.Editor.IssueCategory.MeshCollider,
        Unity.ProjectAuditor.Editor.IssueCategory.Material,
        Unity.ProjectAuditor.Editor.IssueCategory.Texture,
        Unity.ProjectAuditor.Editor.IssueCategory.AudioClip,
        Unity.ProjectAuditor.Editor.IssueCategory.AnimationClip,
        Unity.ProjectAuditor.Editor.IssueCategory.AnimatorController,
        Unity.ProjectAuditor.Editor.IssueCategory.GameObject,
        Unity.ProjectAuditor.Editor.IssueCategory.Package,
        Unity.ProjectAuditor.Editor.IssueCategory.ShaderCompilerMessage
    }
};
parameters.OnModuleCompleted = (name, result, milliseconds) =>
{
    modules.Add(new { name, result = result.ToString(), milliseconds });
    System.IO.File.WriteAllText(statusPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { state = "running", modules }));
};
parameters.OnCompleted = report =>
{
    var issues = report.GetAllIssues().Where(item => item.IsIssue()).Select(item => new
    {
        category = item.Category.ToString(),
        severity = item.Severity.ToString(),
        description = item.Description,
        path = item.RelativePath,
        line = item.Line,
        id = item.Id.ToString(),
        recommendation = item.Id.GetDescriptor().Recommendation
    }).ToArray();
    System.IO.File.WriteAllText(output + "/issues.json", Newtonsoft.Json.JsonConvert.SerializeObject(issues, Newtonsoft.Json.Formatting.Indented));
    report.Save(output + "/report.projectauditor");
    System.IO.File.WriteAllText(statusPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { state = "completed", unity = UnityEngine.Application.unityVersion, issueCount = issues.Length, modules }));
};
System.IO.File.WriteAllText(statusPath, "{\"state\":\"starting\"}");
new Unity.ProjectAuditor.Editor.ProjectAuditor().AuditAsync(parameters);
return "Project Auditor запущен; результаты: " + output;
