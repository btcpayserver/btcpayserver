using System;
using System.Collections;
using System.Collections.Generic;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins
{
    public class PluginServiceCollection : IServiceCollection
    {
        public PluginServiceCollection(IServiceCollection inner, IServiceProvider bootstrapServices)
        {
            Inner = inner;
            BootstrapServices = bootstrapServices;
        }
        public ServiceDescriptor this[int index] { get => Inner[index]; set => Inner[index] = value; }

        public int Count => Inner.Count;

        public bool IsReadOnly => Inner.IsReadOnly;

        public IServiceCollection Inner { get; }
        public IServiceProvider BootstrapServices { get; }

        public void Add(ServiceDescriptor item)
        {
            Inner.Add(item);
        }

        public void Clear()
        {
            Inner.Clear();
        }

        public bool Contains(ServiceDescriptor item)
        {
            return Inner.Contains(item);
        }

        public void CopyTo(ServiceDescriptor[] array, int arrayIndex)
        {
            Inner.CopyTo(array, arrayIndex);
        }

        public IEnumerator<ServiceDescriptor> GetEnumerator()
        {
            return Inner.GetEnumerator();
        }

        public int IndexOf(ServiceDescriptor item)
        {
            return Inner.IndexOf(item);
        }

        public void Insert(int index, ServiceDescriptor item)
        {
            Inner.Insert(index, item);
        }

        public bool Remove(ServiceDescriptor item)
        {
            return Inner.Remove(item);
        }

        public void RemoveAt(int index)
        {
            Inner.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Inner.GetEnumerator();
        }
    }
}
