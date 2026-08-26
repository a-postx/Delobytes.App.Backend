using Serilog;

namespace Delobytes.App.Backend;

internal static class Node
{
    internal static readonly string Id = GetOrSetNodeId();

    private static string GetOrSetNodeId()
    {
        string appDataFolder = "AppData";

        if (string.IsNullOrEmpty(Program.RootPath))
        {
            return string.Empty;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(Program.RootPath, appDataFolder));

            string filePath = Path.Combine(Program.RootPath, appDataFolder, "nodeid");

            if (!File.Exists(filePath))
            {
                string id = Guid.NewGuid().ToString("N");
                File.WriteAllText(filePath, id);
                return id;
            }
            else
            {
                return File.ReadAllText(filePath).Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting Node Id");
            return string.Empty;
        }
    }
}
