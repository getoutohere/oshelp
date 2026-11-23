using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string app = Path.GetFullPath(AppContext.BaseDirectory);
            string repository = Directory.GetParent(app.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            string pointer = Path.Combine(app, "current.json");
            using var stream = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            string relative = document.RootElement.GetProperty("directory").GetString() ?? throw new InvalidDataException("Release directory is missing.");
            string release = Path.GetFullPath(Path.Combine(app, relative));
            string releases = Path.Combine(app, "releases") + Path.DirectorySeparatorChar;
            if (!release.StartsWith(releases, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release directory is outside app/releases.");
            string executable = Path.Combine(release, "OSHelp.exe");
            if (!File.Exists(executable) || !File.Exists(Path.Combine(release, "release.json"))) throw new FileNotFoundException("The selected OSHelp release is incomplete.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = release };
            foreach (string arg in args.Where(a => a != "--wait")) start.ArgumentList.Add(arg);
            void Default(string option, string value) { if (!args.Contains(option)) { start.ArgumentList.Add(option); start.ArgumentList.Add(value); } }
            Default("--repository", release);
            Default("--game", Directory.GetParent(repository)!.FullName);
            Default("--data-dir", Path.Combine(repository, "local/userdata"));
            if (args.Contains("--smoke")) start.WindowStyle = ProcessWindowStyle.Hidden;
            using var process = Process.Start(start) ?? throw new IOException("Could not start OSHelp.");
            if (args.Contains("--wait")) { process.WaitForExit(); return process.ExitCode; }
            return 0;
        }
        catch (Exception e)
        {
            string message = e.Message + "\n\nRun tools/Deploy.ps1 from the oshelp folder to publish a release.";
            Console.Error.WriteLine(message);
            if (!args.Contains("--wait") && !args.Contains("--smoke")) MessageBox(IntPtr.Zero, message, "OSHelp launcher", 0x10);
            return 1;
        }
    }
}
