using System.Text;
using System.Text.RegularExpressions;

namespace Tamp.Cargo;

/// <summary>
/// Minimal, surgical Cargo.toml editor — reads and writes only the
/// <c>[package].version</c> field while preserving every other byte of the
/// manifest (comments, whitespace, quote style, line endings, BOM).
/// </summary>
/// <remarks>
/// Internal helper backing <see cref="Cargo.GetPackageVersion"/> and
/// <see cref="Cargo.SetPackageVersion"/>. Deliberately not a full TOML parser
/// — we never need to interpret anything outside the version field, and
/// dragging in Tomlyn would balloon the satellite's footprint for one verb.
///
/// The string-level approach has two side benefits:
/// <list type="bullet">
///   <item>Round-trips byte-for-byte when version is unchanged (idempotent).</item>
///   <item>Preserves adopter formatting conventions verbatim — tabs vs spaces,
///         comment placement, trailing whitespace, blank-line padding. A full
///         parser would re-emit with its own opinions.</item>
/// </list>
/// </remarks>
internal static class CargoTomlEditor
{
    // [package] header at top of line. Allows leading whitespace (rare but legal)
    // and optional trailing comment. Anchored multiline so we hit the literal
    // [package] section and not e.g. [package.metadata.*] subsections.
    private static readonly Regex PackageSectionHeader = new(
        @"^[ \t]*\[package\][ \t]*(?:#[^\r\n]*)?(?=\r\n|\n|\r|$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Any section header (`[whatever]` at start of line). Used to find the END
    // of the [package] section.
    private static readonly Regex AnySectionHeader = new(
        @"^[ \t]*\[",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // version = "1.0.0" or version = '1.0.0' with optional whitespace.
    // Captures: 1=prefix-up-to-quote, 2=quote-char, 3=value, 4=closing-quote (= group 2 backref).
    private static readonly Regex VersionLine = new(
        @"^([ \t]*version[ \t]*=[ \t]*)(['""])([^'""\r\n]*)\2",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Workspace inheritance: `version.workspace = true`. Adopters with this
    // pattern can't be edited here — the version lives on the workspace root.
    private static readonly Regex VersionWorkspaceInherit = new(
        @"^[ \t]*version[ \t]*\.[ \t]*workspace[ \t]*=[ \t]*true",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Return <c>[package].version</c>, or <c>null</c> if there is no [package] section or no version field.</summary>
    public static string? GetPackageVersion(string content)
    {
        var (sectionStart, sectionEnd) = FindPackageSectionBody(content);
        if (sectionStart < 0) return null;
        var body = content.Substring(sectionStart, sectionEnd - sectionStart);
        if (VersionWorkspaceInherit.IsMatch(body)) return null;
        var m = VersionLine.Match(body);
        return m.Success ? m.Groups[3].Value : null;
    }

    /// <summary>
    /// Write <paramref name="version"/> into the <c>[package].version</c> field
    /// at <paramref name="filePath"/>. Returns <c>true</c> if the file was
    /// modified, <c>false</c> if the version was already correct (idempotent
    /// no-op — file mtime not bumped).
    /// </summary>
    /// <exception cref="FileNotFoundException">Manifest file missing.</exception>
    /// <exception cref="ArgumentException">Version is null/whitespace or contains quote / newline characters.</exception>
    /// <exception cref="InvalidOperationException">
    /// Manifest has no <c>[package]</c> section (workspace virtual manifest), or
    /// the section uses workspace inheritance, or the section has no <c>version</c> field.
    /// </exception>
    public static bool SetPackageVersion(string filePath, string version)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Cargo manifest not found: {filePath}", filePath);
        ValidateVersion(version);

        var rawBytes = File.ReadAllBytes(filePath);
        var (content, hasBom) = DecodeWithBom(rawBytes);

        var (sectionStart, sectionEnd) = FindPackageSectionBody(content);
        if (sectionStart < 0)
            throw new InvalidOperationException(
                $"Cargo.toml at '{filePath}' has no [package] section. " +
                "Workspace virtual manifests should set [workspace.package].version on the workspace root; " +
                "member crates with `version.workspace = true` inherit from there.");

        var body = content.Substring(sectionStart, sectionEnd - sectionStart);
        if (VersionWorkspaceInherit.IsMatch(body))
            throw new InvalidOperationException(
                $"Cargo.toml at '{filePath}' inherits its package version from the workspace " +
                "(`version.workspace = true`). Edit the workspace root's [workspace.package] version, " +
                "not this member crate's manifest.");

        var versionMatch = VersionLine.Match(body);
        if (!versionMatch.Success)
            throw new InvalidOperationException(
                $"Cargo.toml at '{filePath}' has a [package] section but no `version = \"...\"` field within it.");

        var currentVersion = versionMatch.Groups[3].Value;
        if (currentVersion == version) return false;   // idempotent: no write, no mtime bump

        // Surgical replace: rebuild only the prefix+quote+VALUE+quote span, then
        // splice back into the original content. Everything before the
        // [package] header and after the section's last byte is byte-identical.
        var prefix = versionMatch.Groups[1].Value;
        var quote = versionMatch.Groups[2].Value;
        var replacement = $"{prefix}{quote}{version}{quote}";

        var absoluteVersionStart = sectionStart + versionMatch.Index;
        var absoluteVersionEnd = absoluteVersionStart + versionMatch.Length;
        var newContent = content.Substring(0, absoluteVersionStart)
                         + replacement
                         + content.Substring(absoluteVersionEnd);

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var newBodyBytes = encoding.GetBytes(newContent);
        var newBytes = hasBom ? Concat(Utf8Bom, newBodyBytes) : newBodyBytes;

        File.WriteAllBytes(filePath, newBytes);
        return true;
    }

    private static void ValidateVersion(string version)
    {
        if (version is null) throw new ArgumentNullException(nameof(version));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version must be non-empty.", nameof(version));
        if (version.IndexOfAny(new[] { '"', '\'', '\r', '\n' }) >= 0)
            throw new ArgumentException(
                $"Version '{version}' contains invalid characters (quotes or newlines).",
                nameof(version));
    }

    private static (string Content, bool HasBom) DecodeWithBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), true);
        return (Encoding.UTF8.GetString(bytes), false);
    }

    /// <summary>
    /// Returns the body span of the first <c>[package]</c> section — start
    /// inclusive (immediately after the header's newline), end exclusive
    /// (start of next section header, or EOF). Returns <c>(-1, -1)</c> when
    /// no <c>[package]</c> section exists.
    /// </summary>
    private static (int Start, int End) FindPackageSectionBody(string content)
    {
        var header = PackageSectionHeader.Match(content);
        if (!header.Success) return (-1, -1);

        // Body starts immediately after the header's line. Skip the line
        // terminator so the body string starts at column 0 of the next line.
        var afterHeader = header.Index + header.Length;
        if (afterHeader < content.Length && content[afterHeader] == '\r')
        {
            afterHeader++;
            if (afterHeader < content.Length && content[afterHeader] == '\n') afterHeader++;
        }
        else if (afterHeader < content.Length && content[afterHeader] == '\n')
        {
            afterHeader++;
        }

        var next = AnySectionHeader.Match(content, afterHeader);
        var end = next.Success ? next.Index : content.Length;
        return (afterHeader, end);
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        var result = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, result, 0, left.Length);
        Buffer.BlockCopy(right, 0, result, left.Length, right.Length);
        return result;
    }
}
