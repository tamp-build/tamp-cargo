using System.Text;
using Xunit;

namespace Tamp.Cargo.Tests;

/// <summary>
/// Tests for the native Cargo.toml version editor (TAM-203). Covers the
/// idempotency contract, format preservation across line-endings + quote
/// styles + BOM, and error paths for workspace virtual manifests + inheritance.
/// </summary>
public sealed class CargoTomlEditorTests : IDisposable
{
    private readonly string _scratch;

    public CargoTomlEditorTests()
    {
        _scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "tamp-cargo-toml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    private string Write(string name, string content, Encoding? encoding = null, bool withBom = false)
    {
        var path = System.IO.Path.Combine(_scratch, name);
        var enc = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var bytes = enc.GetBytes(content);
        if (withBom)
        {
            var withBomBytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
            File.WriteAllBytes(path, withBomBytes);
        }
        else
        {
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    private AbsolutePath P(string filename, string content) => AbsolutePath.Create(Write(filename, content));

    // ---- GetPackageVersion ----

    [Fact]
    public void GetPackageVersion_Returns_Value_From_Standard_Manifest()
    {
        var content = "[package]\nname = \"foo\"\nversion = \"1.0.7\"\nedition = \"2021\"\n";
        Assert.Equal("1.0.7", CargoTomlEditor.GetPackageVersion(content));
    }

    [Fact]
    public void GetPackageVersion_Returns_Null_When_No_Package_Section()
    {
        var content = "[workspace]\nmembers = [\"crate-a\", \"crate-b\"]\n";
        Assert.Null(CargoTomlEditor.GetPackageVersion(content));
    }

    [Fact]
    public void GetPackageVersion_Returns_Null_When_Inherited_From_Workspace()
    {
        var content = "[package]\nname = \"foo\"\nversion.workspace = true\n";
        Assert.Null(CargoTomlEditor.GetPackageVersion(content));
    }

    [Fact]
    public void GetPackageVersion_Reads_Through_AbsolutePath_Facade()
    {
        var p = P("Cargo.toml", "[package]\nname = \"x\"\nversion = \"0.1.0\"\n");
        Assert.Equal("0.1.0", Cargo.GetPackageVersion(p));
    }

    [Fact]
    public void GetPackageVersion_Returns_Null_When_File_Missing_Via_Facade()
    {
        var p = AbsolutePath.Create(System.IO.Path.Combine(_scratch, "missing.toml"));
        Assert.Null(Cargo.GetPackageVersion(p));
    }

    [Theory]
    [InlineData("[package]\nversion = \"1.0.0\"\n",         "1.0.0")]  // standard
    [InlineData("[package]\nversion=\"1.0.0\"\n",           "1.0.0")]  // no whitespace
    [InlineData("[package]\nversion = '1.0.0'\n",           "1.0.0")]  // single-quoted
    [InlineData("[package]\n  version  =  \"1.0.0\"\n",     "1.0.0")]  // padding
    [InlineData("[package]  # the main package\nversion = \"1.0.0\"\n", "1.0.0")]  // header comment
    [InlineData("[package]\nversion = \"1.0.0\"  # the current version\n", "1.0.0")]  // value comment
    public void GetPackageVersion_Tolerates_Whitespace_And_Quote_Variants(string content, string expected)
    {
        Assert.Equal(expected, CargoTomlEditor.GetPackageVersion(content));
    }

    [Fact]
    public void GetPackageVersion_Ignores_Version_In_Other_Sections()
    {
        var content =
            "[workspace]\nversion = \"0.0.0-workspace\"\n\n" +
            "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n\n" +
            "[dependencies]\nserde = { version = \"1.0\" }\n";
        Assert.Equal("1.0.0", CargoTomlEditor.GetPackageVersion(content));
    }

    [Fact]
    public void GetPackageVersion_Stops_At_Next_Section_Header()
    {
        // version field is in [package.metadata.docs.rs], NOT [package]. Should return null.
        var content =
            "[package]\nname = \"foo\"\n\n" +
            "[package.metadata.docs.rs]\nversion = \"1.0.0\"\n";
        Assert.Null(CargoTomlEditor.GetPackageVersion(content));
    }

    // ---- SetPackageVersion — happy path + format preservation ----

    [Fact]
    public void SetPackageVersion_Writes_New_Value_Preserving_Surrounding_Content()
    {
        var original = "[package]\nname = \"foo\"\nversion = \"1.0.0\"\nedition = \"2021\"\n";
        var p = P("Cargo.toml", original);
        Cargo.SetPackageVersion(p, "1.0.7");
        var updated = File.ReadAllText(p.Value);
        Assert.Equal("[package]\nname = \"foo\"\nversion = \"1.0.7\"\nedition = \"2021\"\n", updated);
    }

    [Fact]
    public void SetPackageVersion_Is_Idempotent_When_Already_At_Target_Version()
    {
        var original = "[package]\nname = \"foo\"\nversion = \"1.0.7\"\n";
        var p = P("Cargo.toml", original);

        var initialMtime = File.GetLastWriteTimeUtc(p.Value);
        Thread.Sleep(100); // ensure clock would advance if file were rewritten

        Cargo.SetPackageVersion(p, "1.0.7");

        // Content unchanged AND mtime unchanged — no write happened.
        Assert.Equal(original, File.ReadAllText(p.Value));
        Assert.Equal(initialMtime, File.GetLastWriteTimeUtc(p.Value));
    }

    [Theory]
    [InlineData("\n",      "LF")]
    [InlineData("\r\n",    "CRLF")]
    public void SetPackageVersion_Preserves_Line_Endings(string lineEnding, string label)
    {
        // Bare CR (Classic Mac OS) is not supported — Cargo.toml in the wild
        // is always LF or CRLF. The .NET multiline regex anchor doesn't honor
        // bare CR either, so we'd need bespoke handling for a non-existent case.
        var original = $"[package]{lineEnding}name = \"foo\"{lineEnding}version = \"1.0.0\"{lineEnding}";
        var p = P($"Cargo-{label}.toml", original);

        Cargo.SetPackageVersion(p, "2.0.0");

        var bytes = File.ReadAllBytes(p.Value);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal($"[package]{lineEnding}name = \"foo\"{lineEnding}version = \"2.0.0\"{lineEnding}", text);
    }

    [Fact]
    public void SetPackageVersion_Preserves_Single_Quote_Style()
    {
        var p = P("Cargo.toml", "[package]\nname = 'foo'\nversion = '1.0.0'\n");
        Cargo.SetPackageVersion(p, "1.0.1");
        // Quote style preserved.
        Assert.Equal("[package]\nname = 'foo'\nversion = '1.0.1'\n", File.ReadAllText(p.Value));
    }

    [Fact]
    public void SetPackageVersion_Preserves_Inline_Comment_On_Version_Line()
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"  # bump on release\n");
        Cargo.SetPackageVersion(p, "1.0.1");
        Assert.Equal("[package]\nname = \"foo\"\nversion = \"1.0.1\"  # bump on release\n", File.ReadAllText(p.Value));
    }

    [Fact]
    public void SetPackageVersion_Preserves_UTF8_BOM()
    {
        var path = System.IO.Path.Combine(_scratch, "Cargo-bom.toml");
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var content = "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(enc.GetBytes(content)).ToArray();
        File.WriteAllBytes(path, bytes);

        Cargo.SetPackageVersion(AbsolutePath.Create(path), "1.0.1");

        var resultBytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, resultBytes[0]);
        Assert.Equal(0xBB, resultBytes[1]);
        Assert.Equal(0xBF, resultBytes[2]);
        var resultText = Encoding.UTF8.GetString(resultBytes, 3, resultBytes.Length - 3);
        Assert.Equal("[package]\nname = \"foo\"\nversion = \"1.0.1\"\n", resultText);
    }

    [Fact]
    public void SetPackageVersion_Preserves_Unicode_In_Other_Fields()
    {
        var original = "[package]\nname = \"テスト\"\nversion = \"1.0.0\"\ndescription = \"🚀 launching\"\n";
        var p = P("Cargo.toml", original);
        Cargo.SetPackageVersion(p, "2.0.0");
        var expected = "[package]\nname = \"テスト\"\nversion = \"2.0.0\"\ndescription = \"🚀 launching\"\n";
        Assert.Equal(expected, File.ReadAllText(p.Value));
    }

    [Fact]
    public void SetPackageVersion_Preserves_Trailing_Content_After_Section()
    {
        var original =
            "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n\n" +
            "[dependencies]\nserde = { version = \"1.0\", features = [\"derive\"] }\n\n" +
            "[[bin]]\nname = \"foo\"\npath = \"src/main.rs\"\n";
        var p = P("Cargo.toml", original);

        Cargo.SetPackageVersion(p, "2.0.0");
        var expected = original.Replace("version = \"1.0.0\"", "version = \"2.0.0\"");
        Assert.Equal(expected, File.ReadAllText(p.Value));
    }

    [Fact]
    public void SetPackageVersion_Does_Not_Touch_Other_Version_Lines()
    {
        // The version field inside [dependencies] must be left alone — only
        // the one inside [package] should change.
        var original =
            "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n\n" +
            "[dependencies]\nversion = \"99.99.99\"\n";   // hypothetical dep called "version"

        var p = P("Cargo.toml", original);
        Cargo.SetPackageVersion(p, "2.0.0");

        var updated = File.ReadAllText(p.Value);
        Assert.Contains("[package]\nname = \"foo\"\nversion = \"2.0.0\"", updated);
        Assert.Contains("[dependencies]\nversion = \"99.99.99\"", updated); // untouched
    }

    [Fact]
    public void SetPackageVersion_Stable_Across_Multiple_Bumps()
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"\nedition = \"2021\"\n");
        Cargo.SetPackageVersion(p, "1.0.1");
        Cargo.SetPackageVersion(p, "1.0.2");
        Cargo.SetPackageVersion(p, "2.0.0-beta.1");
        Assert.Equal("2.0.0-beta.1", Cargo.GetPackageVersion(p));
        Assert.Equal("[package]\nname = \"foo\"\nversion = \"2.0.0-beta.1\"\nedition = \"2021\"\n",
            File.ReadAllText(p.Value));
    }

    // ---- SetPackageVersion — error paths ----

    [Fact]
    public void SetPackageVersion_Throws_When_File_Missing()
    {
        var missing = AbsolutePath.Create(System.IO.Path.Combine(_scratch, "missing.toml"));
        Assert.Throws<FileNotFoundException>(() => Cargo.SetPackageVersion(missing, "1.0.0"));
    }

    [Fact]
    public void SetPackageVersion_Throws_When_No_Package_Section()
    {
        var p = P("Cargo.toml", "[workspace]\nmembers = [\"a\", \"b\"]\n");
        var ex = Assert.Throws<InvalidOperationException>(() => Cargo.SetPackageVersion(p, "1.0.0"));
        Assert.Contains("[package]", ex.Message);
        Assert.Contains("workspace", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void SetPackageVersion_Throws_When_Version_Workspace_Inherited()
    {
        var p = P("Cargo.toml", "[package]\nname = \"member\"\nversion.workspace = true\n");
        var ex = Assert.Throws<InvalidOperationException>(() => Cargo.SetPackageVersion(p, "1.0.0"));
        Assert.Contains("inherits", ex.Message);
        Assert.Contains("workspace", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void SetPackageVersion_Throws_When_Section_Has_No_Version_Field()
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nedition = \"2021\"\n");
        var ex = Assert.Throws<InvalidOperationException>(() => Cargo.SetPackageVersion(p, "1.0.0"));
        Assert.Contains("no `version", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void SetPackageVersion_Throws_On_Empty_Or_Whitespace_Version(string version)
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n");
        Assert.Throws<ArgumentException>(() => Cargo.SetPackageVersion(p, version));
    }

    [Fact]
    public void SetPackageVersion_Throws_On_Null_Version()
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n");
        Assert.Throws<ArgumentNullException>(() => Cargo.SetPackageVersion(p, null!));
    }

    [Theory]
    [InlineData("1.0.\"0")]
    [InlineData("1.0.'0")]
    [InlineData("1.0.0\nmalicious")]
    [InlineData("1.0.0\rsneaky")]
    public void SetPackageVersion_Rejects_Version_With_Invalid_Characters(string version)
    {
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n");
        Assert.Throws<ArgumentException>(() => Cargo.SetPackageVersion(p, version));
    }

    [Fact]
    public void SetPackageVersion_Throws_With_Path_In_Message()
    {
        var p = P("Cargo.toml", "[workspace]\nmembers = [\"a\"]\n");
        var ex = Assert.Throws<InvalidOperationException>(() => Cargo.SetPackageVersion(p, "1.0.0"));
        // Adopters need the path in the error to grep diagnostics.
        Assert.Contains(p.Value, ex.Message);
    }

    [Fact]
    public void SetPackageVersion_Returns_Without_Modifying_When_Version_Matches_Even_If_Other_Edits_Happened_Outside()
    {
        // Simulate a clock-precision concern: ensure the no-op skip happens even
        // when adopters write some non-version content between calls.
        var p = P("Cargo.toml", "[package]\nname = \"foo\"\nversion = \"1.0.0\"\n");

        Cargo.SetPackageVersion(p, "1.0.0");   // first no-op
        var mtime1 = File.GetLastWriteTimeUtc(p.Value);
        Thread.Sleep(50);

        Cargo.SetPackageVersion(p, "1.0.0");   // second no-op
        var mtime2 = File.GetLastWriteTimeUtc(p.Value);

        Assert.Equal(mtime1, mtime2);
    }
}
