namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Normalizes and validates file-upload constraints while workflow properties are parsed.
/// File type and size restrictions are optional, but when supplied they are only valid
/// for <see cref="DataType.File"/> properties.
/// </summary>
public partial class ModelParser
{
    /// <summary>
    /// Converts configured file extensions to the canonical form used during upload validation:
    /// surrounding whitespace and leading dots are removed, values are lower-cased, and
    /// duplicates are discarded.
    /// </summary>
    private static void NormalizeAllowedFileTypes(PropertyDefinition propertyDefinition)
    {
        if (propertyDefinition.AllowedFileTypes == null)
            return;

        if (propertyDefinition.DataType != DataType.File)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' defines allowedFileTypes but is not a File property");

        // Store extensions without a leading dot; upload validation adds the separator when
        // comparing each configured extension with the end of the uploaded filename.
        var normalized = propertyDefinition.AllowedFileTypes
            .Select(fileType => fileType.Trim())
            .Select(fileType => fileType.TrimStart('.'))
            .Select(fileType => fileType.ToLowerInvariant())
            .Distinct()
            .ToArray();

        // Dots inside an extension support compound types such as "tar.gz". The remaining
        // punctuation covers commonly used extension names without allowing path characters.
        if (normalized.Length == 0 || normalized.Any(fileType =>
                fileType.Length < 1 || fileType.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '_' and not '+')))
            throw new Exception(
                $"Property '{propertyDefinition.Name}' contains invalid allowedFileTypes; use file extensions such as pdf or zip");

        propertyDefinition.AllowedFileTypes = normalized;
    }

    /// <summary>
    /// Validates an explicitly configured upload limit. The value is expressed in bytes and
    /// must be positive; the effective default for file properties is applied elsewhere.
    /// </summary>
    private static void ValidateAllowedFileSize(PropertyDefinition propertyDefinition)
    {
        if (propertyDefinition.AllowedFileSize == null)
            return;

        if (propertyDefinition.DataType != DataType.File)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' defines allowedFileSize but is not a File property");

        if (propertyDefinition.AllowedFileSize <= 0)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' contains an invalid allowedFileSize; use a positive number of bytes");
    }
}