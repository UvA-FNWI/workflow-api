using System.Security.Cryptography;
using System.Text;

namespace UvA.Workflow.Migrations;

public enum MigrationKind
{
    RenameProperty
}

/// <summary>
/// Immutable migration intent loaded from a workflow definition's Migrations folder.
/// Operational progress is stored separately in <see cref="Migration"/>.
/// </summary>
public class MigrationDefinition : INamed
{
    public const string RenamePropertyKind = "renameProperty";

    /// <summary>Filename without extension; forms part of the stable migration identifier.</summary>
    [YamlIgnore]
    public string Name { get; set; } = null!;

    /// <summary>Currently only <c>renameProperty</c> is supported.</summary>
    public string Kind { get; set; } = null!;

    public string OldPath { get; set; } = null!;
    public string NewPath { get; set; } = null!;
    public string? Description { get; set; }

    [YamlIgnore] public string WorkflowDefinition { get; set; } = null!;

    [YamlIgnore] public string Id => $"{WorkflowDefinition}:{Name}";

    [YamlIgnore]
    public MigrationKind MigrationKind => Kind switch
    {
        RenamePropertyKind => MigrationKind.RenameProperty,
        _ => throw new InvalidOperationException($"Unsupported property migration kind '{Kind}'")
    };

    /// <summary>A stable checksum used to detect edits after a migration has been activated.</summary>
    [YamlIgnore]
    public string Checksum
    {
        get
        {
            var normalized = string.Join('\n',
                WorkflowDefinition,
                Name,
                Kind,
                OldPath,
                NewPath,
                Description ?? "");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }
    }
}