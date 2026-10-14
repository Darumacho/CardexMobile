using Cardex.Data;

namespace CardexMobile.Data;

public class MobileAppDbContext : AppDbContext
{
    protected override string GetDatabasePath()
    {
        var folder = FileSystem.AppDataDirectory;
        return Path.Combine(folder, "collection.db");
    }
}
