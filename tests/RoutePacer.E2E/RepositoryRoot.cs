namespace RoutePacer.E2E;

public static class RepositoryRoot
{
    /// <summary>Walks upward from the test binaries until the solution file is found.</summary>
    public static string Path
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "RoutePacer.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("RoutePacer.slnx was not found above the test output directory.");
        }
    }

    public static string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);
}
