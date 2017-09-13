using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Models;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;

namespace BTCPayServer.Data
{
	public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
	{
		public ApplicationDbContext()
		{

		}
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options)
		{
		}

		public DbSet<InvoiceData> Invoices
		{
			get; set;
		}

		public DbSet<RefundAddressesData> RefundAddresses
		{
			get; set;
		}

		public DbSet<PaymentData> Payments
		{
			get; set;
		}

		public DbSet<StoreData> Stores
		{
			get; set;
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			var options = optionsBuilder.Options.FindExtension<SqliteOptionsExtension>();
			if(options?.ConnectionString == null)
				optionsBuilder.UseSqlite("Data Source=temp.db");
		}

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<InvoiceData>()
				.HasIndex(o => o.StoreDataId);

			builder.Entity<PaymentData>()
				.HasIndex(o => o.InvoiceDataId);

			builder.Entity<RefundAddressesData>()
				.HasIndex(o => o.InvoiceDataId);

			builder.Entity<UserStore>()
				   .HasKey(t => new {
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
