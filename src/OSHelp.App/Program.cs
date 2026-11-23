using OSHelp.Core;
using System.Text.Json;

namespace OSHelp.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null; }
        var releasePath = Path.Combine(AppContext.BaseDirectory, "release.json");
        using var release = File.Exists(releasePath) ? JsonDocument.Parse(File.ReadAllText(releasePath)) : null;
        string? ReleaseValue(string name) => release?.RootElement.TryGetProperty(name, out var value) == true ? value.GetString() : null;
        var repository = Arg("--repository") ?? CatalogImport.FindRepository();
        bool preview = string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.GetFullPath(Path.Combine(repository, "artifacts/preview")), StringComparison.OrdinalIgnoreCase);
        var gameRoot = Arg("--game") ?? ReleaseValue("gameRoot") ?? Directory.GetParent(repository)!.FullName;
        var data = Arg("--data-dir") ?? ReleaseValue("dataDirectory") ?? Path.Combine(repository, preview ? "local/preview-userdata" : "local/userdata");
        Theme.Load(repository);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
        {
            var smoke = Arg("--smoke");
            if (smoke != null) { Directory.CreateDirectory(smoke); File.WriteAllText(Path.Combine(smoke, "error.txt"), e.Exception.ToString()); Application.ExitThread(); }
            else Dialogs.Message(Form.ActiveForm ?? new Form(), "OSHelp", e.Exception.ToString());
        };
        Application.Run(new MainForm(repository, gameRoot, data) { SmokeDirectory = Arg("--smoke"), Text = preview ? "OSHelp - Preview" : "OSHelp - Obenseuer Tools" });
    }
}
