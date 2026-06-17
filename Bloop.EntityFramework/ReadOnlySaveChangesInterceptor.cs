using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bloop.EntityFramework
{
    public class ReadOnlySaveChangesInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is IReadOnlyDbContext)
                throw new NotSupportedException("This is a read-only context.");
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is IReadOnlyDbContext)
                throw new NotSupportedException("This is a read-only context.");
            return ValueTask.FromResult(result);
        }
    }
}
