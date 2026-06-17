using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bloop.EntityFramework.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private static readonly ReadOnlySaveChangesInterceptor Interceptor = new();

        /*
            // MySQL app
            services.AddReadOnlyDbContext<ReadOnlyCoreDbContext, CoreDbContext>(
                options => options.UseMySql(readerConnectionString, serverVersion));

            // Postgres app
            services.AddReadOnlyDbContext<ReadOnlyOrderDbContext, OrderDbContext>(
                options => options.UseNpgsql(readerConnectionString));

            // SQL Server app
            services.AddReadOnlyDbContext<ReadOnlyInventoryDbContext, InventoryDbContext>(
                options => options.UseSqlServer(readerConnectionString));
         */

        public static IServiceCollection AddReadOnlyDbContext<TReadOnly, TBase>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureProvider)
            where TReadOnly : TBase, IReadOnlyDbContext
            where TBase : DbContext
        {
            services.AddDbContext<TReadOnly>(options =>
            {
                configureProvider(options);
                options.AddInterceptors(Interceptor)
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            });

            return services;
        }
    }
}
