using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class UserStore
    {
        public string ApplicationUserId
        {
            get; set;
        }
        public ApplicationUser ApplicationUser
        {
            get; set;
        }

        public string StoreDataId
        {
            get; set;
        }
        public StoreData StoreData
        {
            get; set;
        }
        public string Role
        {
            get;
            set;
        }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<UserStore>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.UserStores).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<UserStore>()
                   .HasKey(t => new
                   {
                       t.ApplicationUserId,
                       t.StoreDataId
                   });
            builder.Entity<UserStore>()
                 .HasOne(pt => pt.ApplicationUser)
                 .WithMany(p => p.UserStores)
                 .HasForeignKey(pt => pt.ApplicationUserId);

            builder.Entity<UserStore>()
                .HasOne(pt => pt.StoreData)
                .WithMany(t => t.UserStores)
                .HasForeignKey(pt => pt.StoreDataId);
        }
    }
}
