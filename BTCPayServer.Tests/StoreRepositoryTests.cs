using System;
using System.IO;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

using static System.Linq.Enumerable;

namespace BTCPayServer.Tests
{
    /// <summary>
    /// Integration test class for the Store database repsoitory.
    /// </summary>
    public class StoreRepositoryTests
    {
        /// <summary>
        /// Attempts to emulate the parts of the BTCPayServer ASP.NET core intialisation needed to test the Store 
        /// database repository.
        /// </summary>
        public class StoreRepositoryTestsStartup
        {
            public static readonly string SqliteFilename = "store_tests.db";

            private string _dbConnStr = $"DataSource=\"{SqliteFilename}\"";

            public StoreRepositoryTestsStartup(IConfiguration conf, IHostingEnvironment env, ILoggerFactory loggerFactory)
            { }

            public void ConfigureServices(IServiceCollection services)
            {
                services.AddDbContext<ApplicationDbContext>((provider, o) =>
                {
                    ApplicationDbContextFactory factory = provider.GetRequiredService<ApplicationDbContextFactory>();
                    factory.ConfigureBuilder(o);
                });
                services.TryAddSingleton<ApplicationDbContextFactory>(o =>
                {
                    ApplicationDbContextFactory dbContext = new ApplicationDbContextFactory(DatabaseType.Sqlite, _dbConnStr);
                    return dbContext;
                });
                services.TryAddSingleton<StoreRepository>();
                services.AddIdentity<ApplicationUser, IdentityRole>()
                    .AddEntityFrameworkStores<ApplicationDbContext>()
                    .AddDefaultTokenProviders();
                services.Configure<IdentityOptions>(options =>
                {
                    options.Password.RequireDigit = false;
                    options.Password.RequiredLength = 6;
                    options.Password.RequireLowercase = false;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireUppercase = false;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.AllowedForNewUsers = true;
                });
            }

            public void Configure(
               IApplicationBuilder app,
               IHostingEnvironment env,
               IServiceProvider prov,
               ILoggerFactory loggerFactory)
            { }
        }

        public StoreRepositoryTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "StoreRepositoryTests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        public void Dispose()
        {
            if (File.Exists(StoreRepositoryTestsStartup.SqliteFilename))
            {
                File.Delete(StoreRepositoryTestsStartup.SqliteFilename);
            }
        }

        // Tests that the ASP.NET UserManager can be accessed.
        [Fact]
        [Trait("Integration", "Integration")]
        public void CanGetUserManager()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();
                Assert.NotNull(userManager);
            }
        }

        // Tests that the BTCPayServer StoreRepository can be accessed.
        [Fact]
        [Trait("Integration", "Integration")]
        public void CanAccessStoreRepository()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                IServiceProvider services = scope.ServiceProvider;
                StoreRepository storeRepository = services.GetService<StoreRepository>();
                Assert.NotNull(storeRepository);
            }
        }

        // Tests that a user can be added via the ASP.NET Identity services.
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanAddUser()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();

                string emailAddress = "test@btcpay.com";
                var user = new ApplicationUser { UserName = "test", Email = emailAddress };
                IdentityResult result = await userManager.CreateAsync(user, "password");

                ApplicationUser testUser = await userManager.FindByEmailAsync(emailAddress);

                Assert.NotNull(testUser);

                Logs.Tester.LogInformation($"Test user id {testUser.Id}.");
            }
        }

        // Tests that a store can be added.
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanAddStore()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();
                StoreRepository storeRepository = services.GetService<StoreRepository>();

                // Create the user that will be the store owner.
                string emailAddress = "test@btcpay.com";
                var user = new ApplicationUser { UserName = "test", Email = emailAddress };
                await userManager.CreateAsync(user, "password");

                IdentityUser testUser = await userManager.FindByEmailAsync(emailAddress);

                Assert.NotNull(testUser);

                // Create the store.
                StoreData store = await storeRepository.CreateStore(testUser.Id, "Test Store");

                int storeCount = await storeRepository.GetStoresTotal(null);

                Assert.Equal(1, storeCount);
            }
        }

        // Tests that multiple stores can be added.
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanAddMultipleStores()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();
                StoreRepository storeRepository = services.GetService<StoreRepository>();

                // Create the user that will be the store owner.
                string emailAddress = "test@btcpay.com";
                var user = new ApplicationUser { UserName = "test", Email = emailAddress };
                await userManager.CreateAsync(user, "password");

                IdentityUser testUser = await userManager.FindByEmailAsync(emailAddress);

                Assert.NotNull(testUser);

                int storesToAdd = 13;

                foreach (int i in Range(1, storesToAdd))
                {
                    StoreData store = await storeRepository.CreateStore(testUser.Id, $"Test Store {i}");
                }

                int storeCount = await storeRepository.GetStoresTotal(null);

                Assert.Equal(storesToAdd, storeCount);
            }
        }

        // Tests that the stores can be retrieved when filtering on the owner.
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanGetStoresForUser()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();
                StoreRepository storeRepository = services.GetService<StoreRepository>();

                // Create two users.
                string emailAddress = "test@btcpay.com";
                var user = new ApplicationUser { UserName = "test", Email = emailAddress };
                await userManager.CreateAsync(user, "password");

                string emailAddress2 = "test2@btcpay.com";
                var user2 = new ApplicationUser { UserName = "test2", Email = emailAddress2 };
                await userManager.CreateAsync(user2, "password");

                ApplicationUser testUser = await userManager.FindByEmailAsync(emailAddress);
                ApplicationUser testUser2 = await userManager.FindByEmailAsync(emailAddress2);

                Assert.NotNull(testUser);
                Assert.NotNull(testUser2);

                // Create 2 stores, one for each user.
                StoreData store = await storeRepository.CreateStore(testUser.Id, "Test Store");
                string store2Name = "Test Store 2";
                StoreData store2 = await storeRepository.CreateStore(testUser2.Id, store2Name);

                StoreRepository.StoreQuery query = new StoreRepository.StoreQuery { UserId = testUser2.Id };
                int userStoreCount = await storeRepository.GetStoresTotal(query);
                var userStores = await storeRepository.GetStores(query);

                Assert.Equal(1, userStoreCount);
                Assert.Equal(store2Name, userStores.First().StoreName);
            }
        }

        // Tests that ALL stores are retrieved when the user is the server admin.
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanGetStoresForAdmin()
        {
            IWebHost host = new WebHostBuilder()
                .UseStartup<StoreRepositoryTestsStartup>()
                .Build();

            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

                IServiceProvider services = scope.ServiceProvider;
                UserManager<ApplicationUser> userManager = services.GetService<UserManager<ApplicationUser>>();
                StoreRepository storeRepository = services.GetService<StoreRepository>();

                // Create two users.
                string emailAddress = "test@btcpay.com";
                var user = new ApplicationUser { UserName = "test", Email = emailAddress };
                await userManager.CreateAsync(user, "password");

                string emailAddress2 = "test2@btcpay.com";
                var user2 = new ApplicationUser { UserName = "test2", Email = emailAddress2 };
                await userManager.CreateAsync(user2, "password");

                ApplicationUser testUser = await userManager.FindByEmailAsync(emailAddress);
                ApplicationUser testUser2 = await userManager.FindByEmailAsync(emailAddress2);

                Assert.NotNull(testUser);
                Assert.NotNull(testUser2);

                // Create 2 stores, one for each user.
                string storeName = "Test Store";
                StoreData store = await storeRepository.CreateStore(testUser.Id, storeName);
                string store2Name = "Test Store 2";
                StoreData store2 = await storeRepository.CreateStore(testUser2.Id, store2Name);

                StoreRepository.StoreQuery query = new StoreRepository.StoreQuery { IsAdmin = true, UserId = testUser2.Id };
                int userStoreCount = await storeRepository.GetStoresTotal(query);
                var userStores = await storeRepository.GetStores(query);

                Assert.Equal(2, userStoreCount);
                Assert.Contains(storeName, userStores.Select(s => s.StoreName));
                Assert.Contains(store2Name, userStores.Select(s => s.StoreName));
                Assert.False(userStores.Where(s => s.StoreName == storeName).Single().HasClaim(Policies.CanModifyStoreSettings.Key));
                Assert.True(userStores.Where(s => s.StoreName == store2Name).Single().HasClaim(Policies.CanModifyStoreSettings.Key));
            }
        }
    }
}
