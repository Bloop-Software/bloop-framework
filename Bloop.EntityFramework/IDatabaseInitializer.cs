using Microsoft.EntityFrameworkCore;

namespace Bloop.EntityFramework;

public interface IDatabaseInitializer
{
    string Schema { get; }
    IReadOnlyList<DatabaseMigration> Migrations { get; }
}

public record DatabaseMigration(string Id, Func<DbContext, Task> Apply);
