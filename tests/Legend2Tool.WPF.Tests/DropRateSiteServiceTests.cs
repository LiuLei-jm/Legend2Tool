using Legend2Tool.WPF.Services.Infrastructure.Text;
using Legend2Tool.WPF.Services.ScriptOptimization;
using System.Text;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class DropRateSiteServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Legend2Tool-DropRate-{Guid.NewGuid():N}");
    private readonly DropRateSiteService _service = new(new EncodingService());
    private string CustomPath => Path.Combine(_directory, "js", "custom.js");
    private string DataPath(string name) => Path.Combine(_directory, "data", name + ".js");

    public DropRateSiteServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(Path.Combine(_directory, "js"));
    }

    [Fact]
    public void Load_CommentsEscapesAndExtraFields_ReadsOnlyRealVersionArray()
    {
        File.WriteAllText(CustomPath, """
            // var version_list = [];
            const description = "version_list = [ ]";
            let version_list = [
                {name: '传\u5947\'服', data: "one", gllink: "https://example.com/[]", extra: { nested: [1, true, null] }},
                /* preserved */ {"name": "同名", "data": "two"},
            ];
            const footer = "keep this";
            """);

        var items = _service.Load(_directory);

        Assert.Equal(2, items.Count);
        Assert.Equal("传奇'服", items[0].Name);
        Assert.Equal("one", items[0].Data);
        Assert.Equal("two", items[1].Data);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[/* keep */]")]
    [InlineData("[{name:'旧版',data:'old'}]")]
    [InlineData("[{name:'旧版',data:'old'}, // keep\r\n]")]
    public void Append_PreservesOtherSettingsAndWritesDataIntoSite(string array)
    {
        string source = "const before = 1;\r\nvar version_list = " + array + ";\r\nconst after = 2;";
        File.WriteAllText(CustomPath, source);

        _service.Append(_directory, "新\"版\\服", "new", "var DataName = '新服';");

        string updated = File.ReadAllText(CustomPath);
        Assert.StartsWith("const before = 1;\r\nvar version_list = ", updated);
        Assert.EndsWith(";\r\nconst after = 2;", updated);
        if (source.Contains("keep")) Assert.Contains("keep", updated);
        Assert.Equal("新\"版\\服", _service.Load(_directory)[^1].Name);
        Assert.Equal("var DataName = '新服';", File.ReadAllText(DataPath("new")));
        Assert.False(File.Exists(Path.Combine(_directory, "custom.js")));
        Assert.False(File.Exists(Path.Combine(_directory, "new.js")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Append_SameName_ReplacesInPlaceAndOverwritesData(int index)
    {
        string[] names = ["first", "middle", "last"];
        string source = "var version_list = [\r\n"
            + string.Join(",\r\n", names.Select(name => $"  {{name:'{name}',data:'{name}'}}"))
            + "\r\n];\r\nvar keep = true;";
        File.WriteAllText(CustomPath, source);
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        foreach (string name in names) File.WriteAllText(DataPath(name), "old " + name);

        for (int i = 0; i < 2; i++)
            _service.Append(_directory, names[index], names[index], "replacement " + i);

        var versions = _service.Load(_directory);
        Assert.Equal(names, versions.Select(version => version.Name));
        Assert.Equal("replacement 1", File.ReadAllText(DataPath(names[index])));
        foreach (string name in names.Where(name => name != names[index]))
            Assert.Equal("old " + name, File.ReadAllText(DataPath(name)));
        string updated = File.ReadAllText(CustomPath);
        Assert.EndsWith("\r\n];\r\nvar keep = true;", updated);
        Assert.DoesNotMatch(@"(?m)^[\t ]*\r?$", updated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Append_ExistingDuplicateNames_KeepsOneUpdatedEntry(bool trailingComma)
    {
        File.WriteAllText(CustomPath, "var version_list = [\n"
            + "  {name:'same',data:'old'},\n"
            + "  {name:'same',data:'old'}, // keep comment\n"
            + "  {name:'other',data:'other'},\n"
            + "  {name:'same',data:'old'}" + (trailingComma ? "," : "") + "\n];");

        _service.Append(_directory, "same", "current", "new data");

        var versions = _service.Load(_directory);
        Assert.Equal(new[] { "same", "other" }, versions.Select(version => version.Name));
        Assert.Equal("current", versions[0].Data);
        Assert.Equal("new data", File.ReadAllText(DataPath("current")));
        Assert.Contains("// keep comment", File.ReadAllText(CustomPath));
        Assert.DoesNotMatch(@"(?m)^[\t ]*\r?$", File.ReadAllText(CustomPath));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Delete_FirstMiddleOrLastItem_RemovesOnlyTargetAndItsData(int index, bool trailingComma)
    {
        string array = "{name:'同名',data:'a'}, /* keep */ {name:'同名',data:'b'}, {name:'同名',data:'c'}";
        File.WriteAllText(CustomPath, "var version_list = [" + array + (trailingComma ? "," : "") + "]; var other = 1;");
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        foreach (string name in new[] { "a", "b", "c" }) File.WriteAllText(DataPath(name), name);
        var selected = _service.Load(_directory)[index];

        _service.Delete(_directory, selected);

        var remaining = _service.Load(_directory);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, item => item.Data == selected.Data);
        Assert.False(File.Exists(DataPath(selected.Data)));
        Assert.All(remaining, item => Assert.True(File.Exists(DataPath(item.Data))));
        Assert.Contains("/* keep */", File.ReadAllText(CustomPath));
        Assert.EndsWith("; var other = 1;", File.ReadAllText(CustomPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_OnlyItemAndMissingDataDirectory_LeavesEmptyArray(bool trailingComma)
    {
        File.WriteAllText(CustomPath, "var version_list = [{name:'单项',data:'one'}" + (trailingComma ? "," : "") + "];");
        _service.Delete(_directory, _service.Load(_directory).Single());
        Assert.Empty(_service.Load(_directory));
    }

    [Fact]
    public void Delete_SourceChanged_LeavesFilesUnchanged()
    {
        File.WriteAllText(CustomPath, "var version_list = [{name:'版本',data:'one'}];");
        var selected = _service.Load(_directory).Single();
        File.AppendAllText(CustomPath, "\n// external edit");
        string changed = File.ReadAllText(CustomPath);

        Assert.Throws<InvalidOperationException>(() => _service.Delete(_directory, selected));
        Assert.Equal(changed, File.ReadAllText(CustomPath));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AppendAndDelete_RepeatedEdits_DoNotAccumulateBlankLines(string newline)
    {
        string source = $"var version_list = [{newline}  {{name:'original',data:'original'}},{newline}    ];";
        File.WriteAllText(CustomPath, source);
        for (int i = 0; i < 3; i++)
        {
            _service.Append(_directory, "new", "new", "content");
            string appended = File.ReadAllText(CustomPath);
            Assert.DoesNotMatch(@"(?m)^[\t ]*\r?$", appended);
            Assert.EndsWith(newline + "    ];", appended);
            _service.Delete(_directory, _service.Load(_directory).Last());
            Assert.Equal(source, File.ReadAllText(CustomPath));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Delete_MultilineItem_RemovesItsEmptyLine(int index)
    {
        string[] entries = [
            "  {name:'a',data:'a'},",
            "  {\n    name:'b',\n    data:'b'\n  },",
            "  {name:'c',data:'c'},"
        ];
        File.WriteAllText(CustomPath, "var version_list = [\n" + string.Join("\n", entries) + "\n];");

        _service.Delete(_directory, _service.Load(_directory)[index]);

        string expected = "var version_list = [\n" + string.Join("\n", entries.Where((_, i) => i != index)) + "\n];";
        Assert.Equal(expected, File.ReadAllText(CustomPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edit_CleansExistingBlankLinesButPreservesCommentsStringsAndOtherScript(bool delete)
    {
        const string prefix = "// before\n\nvar version_list = [\n";
        const string comment = "  /* comment\n\n     keep its blank line */\n";
        const string entry = "  {name:'keep',data:'keep',extra:'first\\\n  \\\nlast'},\n";
        const string suffix = "];\n\n// after\n";
        File.WriteAllText(CustomPath, prefix + "  \n" + comment + "\t\n" + entry
            + "\n  {name:'remove',data:'remove'},\n\n" + suffix);

        if (delete) _service.Delete(_directory, _service.Load(_directory).Last());
        else _service.Append(_directory, "new", "new", "content");

        string updated = File.ReadAllText(CustomPath);
        Assert.StartsWith(prefix + comment + entry, updated);
        Assert.EndsWith(suffix, updated);
        if (delete) Assert.Equal(prefix + comment + entry + suffix, updated);
        else Assert.Equal(3, _service.Load(_directory).Count);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Delete_LastRemainingMultilineItem_LeavesNoBlankLine(string newline)
    {
        File.WriteAllText(CustomPath, $"var version_list = [{newline}  {{name:'one',data:'one'}},{newline}];");
        _service.Delete(_directory, _service.Load(_directory).Single());
        Assert.Equal($"var version_list = [{newline}];", File.ReadAllText(CustomPath));
    }

    [Fact]
    public void Delete_SharedData_ReportsConflictWithoutChangingFiles()
    {
        const string source = "var version_list = [{name:'a',data:'same'},{name:'b',data:'same'}];";
        File.WriteAllText(CustomPath, source);
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        File.WriteAllText(DataPath("same"), "original");

        Assert.Throws<InvalidOperationException>(() => _service.Delete(_directory, _service.Load(_directory)[0]));
        Assert.Equal(source, File.ReadAllText(CustomPath));
        Assert.Equal("original", File.ReadAllText(DataPath("same")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("bad:name")]
    [InlineData("")]
    public void Append_InvalidDataPath_DoesNotChangeCustomFile(string data)
    {
        const string source = "var version_list = [];";
        File.WriteAllText(CustomPath, source);
        Assert.Throws<InvalidOperationException>(() => _service.Append(_directory, "name", data, "content"));
        Assert.Equal(source, File.ReadAllText(CustomPath));
    }

    [Theory]
    [InlineData("var other = [];")]
    [InlineData("var version_list = [{name: getName(), data:'x'}];")]
    [InlineData("var version_list = [{name:'a',data:'x'},")]
    [InlineData("var version_list = [{name:'a',data:'x', extra: run()}];")]
    [InlineData("var version_list = []; version_list = [];")]
    public void Append_UnsupportedOrMalformedScript_LeavesOriginalUntouched(string source)
    {
        File.WriteAllText(CustomPath, source);
        Assert.Throws<FormatException>(() => _service.Append(_directory, "new", "new", "content"));
        Assert.Equal(source, File.ReadAllText(CustomPath));
        Assert.False(File.Exists(DataPath("new")));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16")]
    [InlineData("gb18030")]
    public void Append_PreservesEncodingBomAndCrLf(string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8" => new UTF8Encoding(false),
            "utf8-bom" => new UTF8Encoding(true),
            "utf16" => Encoding.Unicode,
            _ => Encoding.GetEncoding("GB18030")
        };
        string source = "// 保留中文\r\nvar version_list = [{name:'传奇',data:'old'}];\r\n";
        File.WriteAllText(CustomPath, source, encoding);

        _service.Append(_directory, "新服", "new", "content");

        byte[] bytes = File.ReadAllBytes(CustomPath);
        byte[] prefix = [.. encoding.GetPreamble(), .. encoding.GetBytes("// 保留中文\r\n")];
        Assert.True(bytes.AsSpan().StartsWith(prefix));
        string updated = encoding.GetString(bytes);
        Assert.DoesNotContain("\n", updated.Replace("\r\n", ""));
        Assert.Equal("新服", _service.Load(_directory)[1].Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Append_CustomFileLocked_RestoresPreviousData(bool existingData)
    {
        const string source = "var version_list = [];";
        File.WriteAllText(CustomPath, source);
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        if (existingData) File.WriteAllText(DataPath("new"), "previous");
        using (var locked = new FileStream(CustomPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => _service.Append(_directory, "name", "new", "replacement"));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(source, File.ReadAllText(CustomPath));
        if (existingData) Assert.Equal("previous", File.ReadAllText(DataPath("new")));
        else Assert.False(File.Exists(DataPath("new")));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Delete_CustomFileLocked_RestoresDeletedData()
    {
        const string source = "var version_list = [{name:'one',data:'one'}];";
        File.WriteAllText(CustomPath, source);
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        File.WriteAllText(DataPath("one"), "previous");
        var version = _service.Load(_directory).Single();
        using (var locked = new FileStream(CustomPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => _service.Delete(_directory, version));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(source, File.ReadAllText(CustomPath));
        Assert.Equal("previous", File.ReadAllText(DataPath("one")));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
