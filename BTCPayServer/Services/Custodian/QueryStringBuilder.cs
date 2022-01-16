using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Services.Custodian;

/// <summary>
///  Helps up build a query string by converting an object into a set of named-values and making a
///  query string out of it.
/// </summary>
public class QueryStringBuilder
{
  private readonly List<KeyValuePair<string, object>> _keyValuePairs
    = new List<KeyValuePair<string, object>>();

  /// <summary> Builds the query string from the given instance. </summary>
  public static string BuildQueryString(object queryData, string argSeperator = "&")
  {
    var encoder = new QueryStringBuilder();
    encoder.AddEntry(null, queryData, allowObjects: true);

    return encoder.GetUriString(argSeperator);
  }

  /// <summary>
  ///  Convert the key-value pairs that we've collected into an actual query string.
  /// </summary>
  private string GetUriString(string argSeperator)
  {
    return String.Join(argSeperator,
                       _keyValuePairs.Select(kvp =>
                                             {
                                               var key = Uri.EscapeDataString(kvp.Key);
                                               var value = Uri.EscapeDataString(kvp.Value.ToString());
                                               return $"{key}={value}";
                                             }));
  }

  /// <summary> Adds a single entry to the collection. </summary>
  /// <param name="prefix"> The prefix to use when generating the key of the entry. Can be null. </param>
  /// <param name="instance"> The instance to add.
  ///  
  ///  - If the instance is a dictionary, the entries determine the key and values.
  ///  - If the instance is a collection, the keys will be the index of the entries, and the value
  ///  will be each item in the collection.
  ///  - If allowObjects is true, then the object's properties' names will be the keys, and the
  ///  values of the properties will be the values.
  ///  - Otherwise the instance is added with the given prefix to the collection of items. </param>
  /// <param name="allowObjects"> true to add the properties of the given instance (if the object is
  ///  not a collection or dictionary), false to add the object as a key-value pair. </param>
  private void AddEntry(string prefix, object instance, bool allowObjects)
  {
    var dictionary = instance as IDictionary;
    var collection = instance as ICollection;

    if (dictionary != null)
    {
      Add(prefix, GetDictionaryAdapter(dictionary));
    }
    else if (collection != null)
    {
      Add(prefix, GetArrayAdapter(collection));
    }
    else if (allowObjects)
    {
      Add(prefix, GetObjectAdapter(instance));
    }
    else
    {
      _keyValuePairs.Add(new KeyValuePair<string, object>(prefix, instance));
    }
  }

  /// <summary> Adds the given collection of entries. </summary>
  private void Add(string prefix, IEnumerable<Entry> datas)
  {
    foreach (var item in datas)
    {
      var newPrefix = String.IsNullOrEmpty(prefix)
        ? item.Key
        : $"{prefix}[{item.Key}]";

      AddEntry(newPrefix, item.Value, allowObjects: false);
    }
  }

  private struct Entry
  {
    public string Key;
    public object Value;
  }

  /// <summary>
  ///  Returns a collection of entries that represent the properties on the object.
  /// </summary>
  private IEnumerable<Entry> GetObjectAdapter(object data)
  {
    var properties = data.GetType().GetProperties();

    foreach (var property in properties)
    {
      yield return new Entry()
                   {
                     Key = property.Name,
                     Value = property.GetValue(data)
                   };
    }
  }

  /// <summary>
  ///  Returns a collection of entries that represent items in the collection.
  /// </summary>
  private IEnumerable<Entry> GetArrayAdapter(ICollection collection)
  {
    int i = 0;
    foreach (var item in collection)
    {
      yield return new Entry()
                   {
                     Key = i.ToString(),
                     Value = item,
                   };
      i++;
    }
  }

  /// <summary>
  ///  Returns a collection of entries that represent items in the dictionary.
  /// </summary>
  private IEnumerable<Entry> GetDictionaryAdapter(IDictionary collection)
  {
    foreach (DictionaryEntry item in collection)
    {
      yield return new Entry()
                   {
                     Key = item.Key.ToString(),
                     Value = item.Value,
                   };
    }
  }
}
