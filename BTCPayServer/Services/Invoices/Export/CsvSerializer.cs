using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Ref: https://www.codeproject.com/Articles/566656/CSV-Serializer-for-NET
namespace BTCPayServer.Services.Invoices.Export
{
    /// <summary>
    /// Serialize and Deserialize Lists of any object type to CSV.
    /// </summary>
    public class CsvSerializer<T> where T : class, new()
	{
		private List<PropertyInfo> _properties;

        public bool IgnoreEmptyLines { get; set; } = true;
        public bool IgnoreReferenceTypesExceptString { get; set; } = true;
        public string NewlineReplacement { get; set; } = ((char)0x254).ToString(CultureInfo.InvariantCulture);

        public char Separator { get; set; } = ',';

		public string RowNumberColumnTitle { get; set; } = "RowNumber";
        public bool UseLineNumbers { get; set; } = false;
        public bool UseEofLiteral { get; set; } = false;

        /// <summary>
        /// Csv Serializer
        /// Initialize by selected properties from the type to be de/serialized
        /// </summary>
        public CsvSerializer()
		{
			var type = typeof(T);

			var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance
				| BindingFlags.GetProperty | BindingFlags.SetProperty);


			var q = properties.AsQueryable();

			if (IgnoreReferenceTypesExceptString)
			{
				q = q.Where(a => a.PropertyType.IsValueType || a.PropertyType.Name == "String");
			}

			var r = from a in q
					where a.GetCustomAttribute<CsvIgnoreAttribute>() == null
					select a;

			_properties = r.ToList();
		}

		/// <summary>
		/// Serialize
		/// </summary>
		/// <param name="stream">stream</param>
		/// <param name="data">data</param>
		public string Serialize(IList<T> data)
		{
			var sb = new StringBuilder();
			var values = new List<string>();

			sb.AppendLine(GetHeader());

			var row = 1;
			foreach (var item in data)
			{
				values.Clear();

				if (UseLineNumbers)
				{
					values.Add(row++.ToString(CultureInfo.InvariantCulture));
				}

				foreach (var p in _properties)
				{
					var raw = p.GetValue(item);
					var value = raw == null ? "" :
						raw.ToString()
                        .Replace("\"", "``", StringComparison.OrdinalIgnoreCase)
						.Replace(Environment.NewLine, NewlineReplacement, StringComparison.OrdinalIgnoreCase);

					value = String.Format(CultureInfo.InvariantCulture, "\"{0}\"", value);

					values.Add(value);
				}
				sb.AppendLine(String.Join(Separator.ToString(CultureInfo.InvariantCulture), values.ToArray()));
			}

			if (UseEofLiteral)
			{
				values.Clear();

				if (UseLineNumbers)
				{
					values.Add(row++.ToString(CultureInfo.InvariantCulture));
				}

				values.Add("EOF");

				sb.AppendLine(string.Join(Separator.ToString(CultureInfo.InvariantCulture), values.ToArray()));
			}

            return sb.ToString();
		}

		/// <summary>
		/// Get Header
		/// </summary>
		/// <returns></returns>
		private string GetHeader()
		{
			var header = _properties.Select(a => a.Name);

			if (UseLineNumbers)
			{
				header = new string[] { RowNumberColumnTitle }.Union(header);
			}

			return string.Join(Separator.ToString(CultureInfo.InvariantCulture), header.ToArray());
		}
    }

    public class CsvIgnoreAttribute : Attribute { }

	public class InvalidCsvFormatException : Exception
	{
		/// <summary>
		/// Invalid Csv Format Exception
		/// </summary>
		/// <param name="message">message</param>
		public InvalidCsvFormatException(string message)
			: base(message)
		{
		}

		public InvalidCsvFormatException(string message, Exception ex)
			: base(message, ex)
		{
		}
	}
}
