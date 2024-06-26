using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data
{
    public abstract class DBScriptsMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var script in GetType().GetCustomAttributes<DBScriptAttribute>().OrderBy(n => n.ScriptName))
            {
                var name = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .First(s => s.EndsWith("." + script.ScriptName, StringComparison.Ordinal));
                var stream = Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream(name);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                migrationBuilder.Sql(reader.ReadToEnd());
            }
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DBScriptAttribute : Attribute
    {
        public DBScriptAttribute(string scriptName)
        {
            ScriptName = scriptName;
        }
        public string ScriptName { get; set; }
    }
}
