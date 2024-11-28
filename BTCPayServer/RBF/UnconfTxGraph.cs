#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Plugins;
using NBitcoin;
using static BTCPayServer.RBF.UnconfTxGraph;

namespace BTCPayServer.RBF
{
    public class UnconfTxGraph
    {
        public record Replacement(Node NewNode, Transaction[] ReplacedTransactions);
        public class Replacements : List<Replacement>
        {
            private readonly UnconfTxGraph _originalGraph;

            public Replacements(IEnumerable<Replacement> replacements, UnconfTxGraph originalGraph) : base(replacements)
            {
                _originalGraph = originalGraph;
            }

            public void StripIntermediate()
            {
                List<Replacement> replacementsNoIntermediate = new();
                HashSet<uint256> intermediates = new();
                var replacementsByHash = this.ToDictionary(r => r.NewNode.Hash);
                foreach (var replacement in ((IEnumerable<Replacement>)this).Reverse())
                {
                    if (intermediates.Contains(replacement.NewNode.Hash))
                        continue;
                    foreach (var replaced in replacement.ReplacedTransactions)
                    {
                        if (!_originalGraph.TxsById.ContainsKey(replaced.GetHash()))
                        {
                            intermediates.Add(replaced.GetHash());
                        }
                    }
                }

                foreach (var replacement in this)
                {
                    if (intermediates.Contains(replacement.NewNode.Hash))
                        continue;
                    List<Transaction> replacedTxs = new();
                    FillReplacedTransaction(replacementsByHash, replacedTxs, intermediates, replacement);
                    replacementsNoIntermediate.Add(new Replacement(replacement.NewNode, replacedTxs.ToArray()));
                }
                this.Clear();
                this.AddRange(replacementsNoIntermediate);
            }

            private void FillReplacedTransaction(
                Dictionary<uint256, Replacement> replacementsByHash,
                List<Transaction> replacedTxs,
                HashSet<uint256> intermediates,
                Replacement replacement)
            {
                foreach (var replacedTx in replacement.ReplacedTransactions)
                {
                    if (intermediates.Contains(replacedTx.GetHash()))
                        FillReplacedTransaction(replacementsByHash, replacedTxs, intermediates, replacementsByHash[replacedTx.GetHash()]);
                    else
                        replacedTxs.Add(replacedTx);
                }
            }
        }
        public class Node
        {
            public Node(TxContext txContext)
            {
                TxContext = txContext;
                VirtualSize = txContext.Tx.GetVirtualSize();
            }
            public Transaction Tx => TxContext.Tx;
            public HashSet<Node> Parents { get; set; } = new();
            public HashSet<Node> Children { get; set; } = new();
            public int VirtualSize { get; }

            /// <summary>
            /// Fee of all ancestors + this tx
            /// </summary>
            public Money? AncestorFee { get; set; }
            /// <summary>
            /// Size for all the ancestor + this tx
            /// </summary>
            public int AncestorSize { get; set; }
            public FeeRate? EffectiveFeeRate { get; private set; }
            public bool IsOurs => Children.Count == Tx.Inputs.Count;
            public uint256 Hash => Tx.GetHash();

            public TxContext TxContext { get; }

            internal FeeRate ComputeEffectiveFeeRate()
            {
                if (EffectiveFeeRate is not null)
                    return EffectiveFeeRate;
                AncestorSize = VirtualSize;
                AncestorFee = TxContext.Fee;
                foreach (var parent in Parents)
                {
                    parent.ComputeEffectiveFeeRate();
                    AncestorFee += parent.AncestorFee;
                    AncestorSize += parent.AncestorSize;
                }
                EffectiveFeeRate = new FeeRate(AncestorFee, AncestorSize);
                return EffectiveFeeRate;
            }

            public record CompactResult(Node[] UncompactedChildren, Node Uncompacted, Node Compacted);
            internal CompactResult? Compact()
            {
                if (!this.TxContext.IsMine)
                    return null;
                if (this.TxContext.ChangeAddress is null)
                    return null;
                if (this.Children.Count == 0)
                    return new([], this, new Node(TxContext));
                CompactResult[] mergeResults = Children.Select(c => c.Compact()).Where(c => c is not null).ToArray()!;
                // Some children can't merge
                if (mergeResults.Length != Children.Count)
                    return null;
                
                var replacedChildren = mergeResults.SelectMany(m => m.UncompactedChildren).ToList();

                foreach (var children in Children)
                    replacedChildren.Add(children);


                var compactedTxs = mergeResults.Select(m => m.Compacted.Tx.Clone()).ToList();

                // The new tx has all the inputs of the children
                // + the inputs of the current tx
                // - the inputs coming from the outputs of this tx
                var newInputs = compactedTxs.SelectMany(c => c.Inputs)
                                                        .Where(i => i.PrevOut.Hash != Hash)
                                                        .ToList();
                foreach (var input in Tx.Inputs)
                    newInputs.Add(input);

                // The new tx has all the outputs of the children
                // + the outputs of the current tx
                // - the change outputs of the children

                var changeOutputs = compactedTxs.SelectMany((c, i) => c.Outputs.Where(o => o.ScriptPubKey == mergeResults[i].Compacted.TxContext.ChangeAddress)).ToHashSet();
                var newOutputs = compactedTxs.SelectMany((c,i) => c.Outputs.Where(o => !changeOutputs.Contains(o))).ToList();
                foreach (var output in Tx.Outputs)
                    newOutputs.Add(output);

                var newTx = Tx.Clone();
                newTx.Inputs.Clear();
                foreach (var input in newInputs)
                {
                    input.ScriptSig = Script.Empty;
                    input.WitScript = WitScript.Empty;
                    newTx.Inputs.Add(input);
                }
                newTx.Outputs.Clear();
                newTx.Outputs.AddRange(newOutputs);

                var changeOutput = newOutputs.Where(o => o.ScriptPubKey == TxContext.ChangeAddress).FirstOrDefault();
                if (changeOutput is null)
                    return null;
                // The new change output is equal to the sum of the change outputs of the children (since we removed those outputs)
                // + the fee of the children that we don't have to pay anymore
                changeOutput.Value += changeOutputs.Select(c => c.Value).Sum();
                var childrenFee = mergeResults.Select(m => m.Compacted.TxContext.Fee).Sum();
                changeOutput.Value += childrenFee;
                var mergedNode = new Node(new(newTx, childrenFee + TxContext.Fee, TxContext.ChangeAddress, true));
                newTx.PrecomputeHash(false, true);
                return new(replacedChildren.ToArray(), this, mergedNode);
            }

            public IEnumerable<Node> Enumerate()
            {
                yield return this;
                foreach (var item in Children)
                {
                    yield return item;
                }
            }
        }

        public record TxContext(Transaction Tx, Money Fee, Script? ChangeAddress, bool IsMine);
        public UnconfTxGraph(IEnumerable<TxContext> unconfTxs)
        {
            foreach (var tx in unconfTxs)
                tx.Tx.PrecomputeHash(false, true);
            TxsById = unconfTxs.ToDictionary(tx => tx.Tx.GetHash(), tx => new Node(tx));
            foreach (var tx in unconfTxs.Select(t => t.Tx))
            {
                var node = TxsById[tx.GetHash()];
                foreach (var input in tx.Inputs)
                {
                    if (TxsById.TryGetValue(input.PrevOut.Hash, out var parent))
                    {
                        node.Parents.Add(parent);
                        parent.Children.Add(node);
                    }
                }
            }
            RootNodes = [];
            LeafNodes = [];
            TxsUpdated();
        }

        private void TxsUpdated()
        {
            RootNodes = TxsById.Values.Where(n => n.Parents.Count == 0).ToArray();
            LeafNodes = TxsById.Values.Where(n => n.Children.Count == 0).ToArray();

            foreach (var leaf in LeafNodes)
            {
                leaf.ComputeEffectiveFeeRate();
            }
        }

        public UnconfTxGraph Clone()
        {
            return new UnconfTxGraph(TxsById.Values.Select(r => r.TxContext));
        }

        public UnconfTxGraph Compact(uint256[] txs, out Replacements replacements)
        {
            var toCompact = txs.ToHashSet();
            var clone = Clone();
            var replacementList = new List<Replacement>();

            foreach (var node in Enumerate())
            {
                if (!toCompact.Contains(node.Hash))
                    continue;
                if (!clone.TxsById.ContainsKey(node.Hash))
                    continue;

                var uncompacted = clone.TxsById[node.Hash];
                var merged = uncompacted.Compact();
                if (merged is null)
                    continue;

                var uncompactedNodes = merged.UncompactedChildren.Select(c => c).ToList();
                uncompactedNodes.Add(uncompacted);

                foreach (var uncompactedNode in uncompactedNodes)
                {
                    foreach (var parent in uncompactedNode.Parents)
                    {
                        parent.Children.Add(merged.Compacted);
                        merged.Compacted.Parents.Add(parent);
                    }
                }

                replacementList.Add(new Replacement(merged.Compacted, uncompactedNodes.Select(n => n.Tx).ToArray()));

                foreach (var r in uncompactedNodes)
                {
                    clone.TxsById.Remove(r.Tx.GetHash());
                }
                clone.TxsById.Add(merged.Compacted.Hash, merged.Compacted);
                clone.CleanupHierarchy();
            }
            clone.TxsUpdated();
            replacements = new Replacements(replacementList, this);
            return clone;
        }

        private void CleanupHierarchy()
        {
            foreach (var node in this.TxsById.Values)
            {
                node.Children.RemoveWhere(c => !this.TxsById.ContainsKey(c.Hash));
                node.Parents.RemoveWhere(c => !this.TxsById.ContainsKey(c.Hash));
            }
        }

        public IEnumerable<Node> Enumerate()
        {
            foreach (var n in RootNodes.SelectMany(r => r.Enumerate()))
            {
                yield return n;
            }
        }

        public Money GetTotalFee()
        {
            return TxsById.Values.Select(t => t.TxContext.Fee).Sum();
        }

        public Dictionary<uint256, Node> TxsById { get; private set; } = new Dictionary<uint256, Node>();
        public UnconfTxGraph.Node[] RootNodes { get; private set; }
        public UnconfTxGraph.Node[] LeafNodes { get; private set; }
    }
}
