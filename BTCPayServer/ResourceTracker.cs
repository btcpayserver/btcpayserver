#nullable enable
using System;
using System.Collections.Concurrent;

namespace BTCPayServer
{
    public class ResourceTracker<T> where T: notnull
    {
        public class ScopedResourceTracker : IDisposable
        {
            private ResourceTracker<T> _parent;

            public ScopedResourceTracker(ResourceTracker<T> resourceTracker)
            {
                _parent = resourceTracker;
            }
            ConcurrentDictionary<T, string> _Scoped = new();
            public bool TryTrack(T resource)
            {
                if (!_parent._TrackedResources.TryAdd(resource, string.Empty))
                    return false;
                _Scoped.TryAdd(resource, string.Empty);
                return true;
            }

            public bool Contains(T resource) => _Scoped.ContainsKey(resource);

            public void Dispose()
            {
                foreach (var d in _Scoped)
                    _parent._TrackedResources.TryRemove(d.Key, out _);
            }
        }
        internal ConcurrentDictionary<T, string> _TrackedResources = new();
        public ScopedResourceTracker StartTracking() => new ScopedResourceTracker(this);
        public bool Contains(T resource) => _TrackedResources.ContainsKey(resource);
    }
}
