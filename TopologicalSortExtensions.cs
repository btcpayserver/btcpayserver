using System;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer;

public static class TopologicalSortExtensions
{
	public static List<T> TopologicalSort<T>(this IReadOnlyCollection<T> nodes, Func<T, IEnumerable<T>> dependsOn)
	{
		return nodes.TopologicalSort(dependsOn, k => k, k => k);
	}

	public static List<T> TopologicalSort<T, TDepend>(this IReadOnlyCollection<T> nodes, Func<T, IEnumerable<TDepend>> dependsOn, Func<T, TDepend> getKey)
	{
		return nodes.TopologicalSort(dependsOn, getKey, o => o);
	}

	public static List<TValue> TopologicalSort<T, TDepend, TValue>(this IReadOnlyCollection<T> nodes,
								  Func<T, IEnumerable<TDepend>> dependsOn,
								  Func<T, TDepend> getKey,
								  Func<T, TValue> getValue,
								  IComparer<T> solveTies = null)
	{
		if (nodes.Count == 0)
			return new List<TValue>();
		if (getKey == null)
			throw new ArgumentNullException(nameof(getKey));
		if (getValue == null)
			throw new ArgumentNullException(nameof(getValue));
		solveTies = solveTies ?? Comparer<T>.Default;
		List<TValue> result = new List<TValue>(nodes.Count);
		HashSet<TDepend> allKeys = new HashSet<TDepend>(nodes.Count);
		var noDependencies = new SortedDictionary<T, HashSet<TDepend>>(solveTies);

		foreach (var node in nodes)
			allKeys.Add(getKey(node));
		var dependenciesByValues = nodes.ToDictionary(node => node,
								node => new HashSet<TDepend>(dependsOn(node).Where(n => allKeys.Contains(n))));
		foreach (var e in dependenciesByValues.Where(x => x.Value.Count == 0))
		{
			noDependencies.Add(e.Key, e.Value);
		}
		if (noDependencies.Count == 0)
		{
			throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
		}
		while (noDependencies.Count > 0)
		{
			var nodep = noDependencies.First();
			noDependencies.Remove(nodep.Key);
			dependenciesByValues.Remove(nodep.Key);

			var elemKey = getKey(nodep.Key);
			result.Add(getValue(nodep.Key));
			foreach (var selem in dependenciesByValues)
			{
				if (selem.Value.Remove(elemKey) && selem.Value.Count == 0)
					noDependencies.Add(selem.Key, selem.Value);
			}
		}
		if (dependenciesByValues.Count != 0)
		{
			throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
		}
		return result;
	}
}
