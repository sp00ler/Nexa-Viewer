using System.Globalization;
using System.Resources;

namespace ViewerPrn.App;

/// <summary>
/// UI strings. The neutral resource set is Russian; <c>Strings.en.resx</c> ships as a
/// satellite assembly.
/// </summary>
// ponytail: ResourceManager directly, no generated designer class and no x:Uid/MRT wiring.
// Keys are plain strings — a typo shows the key itself in the UI instead of throwing.
// Switch to generated strongly-typed accessors if the string count outgrows one screen.
internal static class Strings
{
    private static readonly ResourceManager Manager = new("ViewerPrn.App.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    // CurrentUICulture picks the resource set; CurrentCulture formats the values inside it.
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
