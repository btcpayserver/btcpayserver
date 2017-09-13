using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
	public class ApplicationDbContextFactory
	{
		string _Path;
		public ApplicationDbContextFactory(string path)
		{
			_Path = path ?? throw new ArgumentNullException(nameof(path));
		}

		public ApplicationDbContext CreateContext()
		{
			var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
			builder.UseSqlite("Data Source=" + _Path);
			return new ApplicationDbContext(builder.Options);
		}
	}
}
