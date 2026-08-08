using System.Xml.Linq;

namespace ViewerPrn.Domain.Tests;

/// <summary>
/// The UI strings live in plain .resx files with string keys, so a missing translation would
/// otherwise only show up as an English word in a Russian menu. This compares the key sets
/// directly on the files — no reference to the app assembly needed.
/// </summary>
public sealed class ResourceParityTests
{
    [Fact]
    public void RussianAndEnglishResourcesDefineTheSameKeys()
    {
        string appDirectory = Path.Combine(RepositoryRoot(), "src", "ViewerPrn.App");

        HashSet<string> russian = KeysOf(Path.Combine(appDirectory, "Strings.resx"));
        HashSet<string> english = KeysOf(Path.Combine(appDirectory, "Strings.en.resx"));

        Assert.Empty(russian.Except(english));
        Assert.Empty(english.Except(russian));
        Assert.NotEmpty(russian);
    }

    private static HashSet<string> KeysOf(string resxPath) =>
    [
        .. XDocument.Load(resxPath)
            .Root!
            .Elements("data")
            .Select(element => element.Attribute("name")!.Value),
    ];

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ViewerPrn.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
