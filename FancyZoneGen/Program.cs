using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string configFile = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\AppData\Local\Microsoft\PowerToys\FancyZones\custom-layouts.json");
Console.WriteLine($"Reading from {configFile}");

if (!File.Exists(configFile))
{
    Console.Error.WriteLine("custom-layouts.json missing");
    return 1;
}

// parse config
JsonNode? doc;
string fileHash;
{
    var fileBytes = File.ReadAllBytes(configFile);
    doc = JsonNode.Parse(fileBytes);
    fileHash = Convert.ToHexString(MD5.Create().ComputeHash(fileBytes));
}

var layouts = (doc?.Root ?? throw new InvalidOperationException("Expected root element"))
    .GetArray("custom-layouts")
    .Where(l => l?.GetValue<string>("type") == "canvas")
    .ToList();

if (layouts.Count == 0)
{
    Console.Error.WriteLine("No canvas type layouts found");
    return 2;
}

// select layout
JsonNode targetLayout;
if (layouts.Count == 1)
{
    targetLayout = layouts[0]!;
}
else
{
    int index;
    do
    {
        for (var i = 0; i < layouts.Count; i++)
        {
            Console.WriteLine($"[{i + 1}] {layouts[i].GetValue<string>("name")}");
        }

        Console.Write($"Which layout to fill? [1-{layouts.Count}] > ");
        Console.Out.Flush();
    } while (!int.TryParse(Console.ReadLine(), out index) || index < 1 || index > layouts.Count);
    targetLayout = layouts[index - 1]!;
}

// dump info
var info = targetLayout["info"] ?? throw new InvalidOperationException("Expected object: info");
string name = targetLayout.GetValue<string>("name");
int refWidth = info.GetValue<int>("ref-width");
int refHeight = info.GetValue<int>("ref-height");
Console.WriteLine($"Updating {name} ({refWidth}x{refHeight})");

// perform backup if required
string backupFile = Path.ChangeExtension(configFile, $".{fileHash}.json.bak");
if (!File.Exists(backupFile))
{
    Console.WriteLine("Backing up original to: " + Path.GetFileName(backupFile));
    File.Copy(configFile, backupFile, true);
}
else
{
    Console.WriteLine("Backup already exists at: " + Path.GetFileName(backupFile));
}

// calculate zones
Console.WriteLine($"Splitting at 30% ({.3*refWidth:0}px), 50% ({.5*refWidth:0}px) and 70%({.7*refWidth:0}px)");
int halfHeight = refHeight / 2;
var zones = new[] {.3, .5, .7}
    // column locations in pixels
    .Select(pct => (int) (pct * refWidth))
    // partial width, full height zones
    .SelectMany(px => new[]
    {
        new {X = 0, width = px}, // left
        new {X = px, width = refWidth - px} // right
    })
    // include full width
    .Prepend(new {X = 0, width = refWidth})
    // combine columns and rows into final json
    .SelectMany(_ => new[]
    {
        new {Y = 0, height = refHeight}, // full height
        new {Y = 0, height = halfHeight}, // top half
        new {Y = halfHeight, height = halfHeight}, // bottom half
    }, (col, row) => new JsonObject
    {
        ["X"] = JsonValue.Create(col.X),
        ["Y"] = JsonValue.Create(row.Y),
        ["width"] = JsonValue.Create(col.width),
        ["height"] = JsonValue.Create(row.height)
    })
    .ToArray<JsonNode>();

// write changes
info["zones"] = new JsonArray(zones);

using (var fs = File.Create(configFile))
using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions {Indented = true}))
{
    doc.WriteTo(writer);
}

Console.WriteLine("Done, restart PowerToys to apply changes.");

return 0;

internal static class JsonExtensions
{
    internal static JsonArray GetArray(this JsonNode? obj, string propName)
        => obj?[propName] as JsonArray
           ?? throw new InvalidOperationException("Expected array: " + propName);

    internal static T GetValue<T>(this JsonNode? obj, string propName)
        => obj?[propName] is { } o
            ? o.GetValue<T>()
            : throw new InvalidOperationException("Expected value: " + propName);
}
