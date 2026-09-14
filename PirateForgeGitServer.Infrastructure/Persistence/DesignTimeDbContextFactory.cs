using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PirateForgeGitServer.Infrastructure.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PirateForgeDbContext>
{
    public PirateForgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PirateForgeDbContext>();

        optionsBuilder.UseSqlite(
            "Database=C:\\code\\projects\\git-server\\.pirateforge\\database\\pirate-forge.db;");

        return new PirateForgeDbContext(optionsBuilder.Options);
    }
}
