namespace EmailArchiveViewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Attachments are extracted under %TEMP%\EmailArchiveViewer\ when opened. Sweep
        // leftovers from previous sessions so they don't accumulate on the machine.
        try
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "EmailArchiveViewer");
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch { /* best effort — a file still open from another viewer instance, etc. */ }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
