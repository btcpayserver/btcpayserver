#nullable enable
using BTCPayServer.Payments;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections;
using Amazon.Runtime.Internal.Transform;

namespace BTCPayServer
{
    public interface IHandler<TId> { TId Id { get; } }
    public class HandlersDictionary<TId, THandler> : IEnumerable<THandler> where THandler: IHandler<TId>
                                                                           where TId: notnull
    {
        public HandlersDictionary(IEnumerable<THandler> handlers)
        {
            foreach (var handler in handlers)
            {
                _mappedHandlers.Add(handler.Id, handler);
            }
        }

        private readonly Dictionary<TId, THandler> _mappedHandlers =
        new Dictionary<TId, THandler>();
        public bool TryGetValue(TId id, [MaybeNullWhen(false)] out THandler value)
        {
            ArgumentNullException.ThrowIfNull(id);
            return _mappedHandlers.TryGetValue(id, out value);
        }
        public THandler? TryGet(TId id)
        {
            ArgumentNullException.ThrowIfNull(id);
            _mappedHandlers.TryGetValue(id, out var value);
            return value;
        }
        public bool Support(TId id) => _mappedHandlers.ContainsKey(id);
        public THandler this[TId index] => _mappedHandlers[index];
        public IEnumerator<THandler> GetEnumerator()
        {
            return _mappedHandlers.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
