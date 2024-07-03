using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.RBF;
using NBitcoin;
using Xunit;
using YamlDotNet.Serialization.ObjectGraphVisitors;
using static BTCPayServer.RBF.UnconfTxGraph;
using static NLog.LayoutRenderers.Wrappers.ReplaceLayoutRendererWrapper;

namespace BTCPayServer.Tests
{
    [Trait("Fast", "Fast")]
    public class CompactTxTests
    {
        class GraphBuilder
        {
            class BuildingContext
            {
                
                public BuildingContext()
                {
                }

                public Transaction Tx;
                public bool IsMine;
                public Script ChangeAddress;
                public Money Fee { get; internal set; }
                public UnconfTxGraph.TxContext AsTxContext()
                {
                    return new UnconfTxGraph.TxContext(BuildTx(), Fee, ChangeAddress, IsMine);
                }

                public List<Action<Transaction>> BuildSteps = new List<Action<Transaction>>();
                public List<BuildingContext> Parents = new List<BuildingContext>();

                internal Transaction BuildTx()
                {
                    if (Tx != null)
                        return Tx;

                    var tx = Network.Main.CreateTransaction();
                    foreach (var p in Parents)
                    {
                        var parent = p.BuildTx();
                        tx.Inputs.Add(new OutPoint(parent.GetHash(), 0));
                    }

                    foreach (var step in BuildSteps)
                        step(tx);
                    
                    Tx = tx;
                    return tx;
                }
            }

            Stack<BuildingContext> _stack = new Stack<BuildingContext>();
            List<BuildingContext> _buildingContexts = new List<BuildingContext>();
            public GraphBuilder PushNewTx(string name = null, bool isMine = false, Money fee = null)
            {
                BuildingContext parent = null;
                if (_stack.Count != 0)
                    parent = _stack.Peek();
                fee ??= Money.Satoshis(1000);
                var ctx = new BuildingContext() { IsMine = isMine, Fee = fee };
                _stack.Push(ctx);
                _buildingContexts.Add(_stack.Peek());
                if (parent != null)
                    ctx.Parents.Add(parent);
                if (name != null)
                    return SetName(name);
                return this;
            }

            
            public GraphBuilder Pop()
            {
                _stack.Pop();
                return this;
            }

            public GraphBuilder SetFee(Money fee)
            {
                _stack.Peek().Fee = fee;
                return this;
            }
            public GraphBuilder IsMine(bool withChangeAddress = true, Money changeValue = null)
            {
                changeValue ??= Money.Satoshis(1000);
                _stack.Peek().IsMine = true;
                if (withChangeAddress)
                    SetChange(new Key().GetScriptPubKey(ScriptPubKeyType.Segwit), changeValue);
                return this;
            }
            public GraphBuilder AddInput()
            {
                var id = RandomUtils.GetUInt256();
                _stack.Peek().BuildSteps.Add(tx => tx.Inputs.Add(new OutPoint(id, 0)));
                return this;
            }
            public GraphBuilder AddOutput(Script script = null, Money value = null)
            {
                if (script is null)
                    script = new Key().PubKey.WitHash.ScriptPubKey;
                var ctx = _stack.Peek();
                _stack.Peek().BuildSteps.Add(tx => tx.Outputs.Add(value, script));
                return this;
            }
            public GraphBuilder SetChange(Script change, Money value = null)
            {
                _stack.Peek().ChangeAddress = change;
                return AddOutput(change, value);
            }

            public UnconfTxGraph Build()
            {
                return new UnconfTxGraph(_buildingContexts.ConvertAll(c => c.AsTxContext()));
            }
            Dictionary<string, BuildingContext> _Names = new Dictionary<string, BuildingContext>();
            public GraphBuilder SetName(string name)
            {
                _Names.Add(name, _stack.Peek());
                return this;
            }

            public GraphBuilder SpentBy(string name)
            {
                _Names[name].Parents.Add(_stack.Peek());
                return this;
            }

            public GraphBuilder GetTx(string name, out Transaction tx)
            {
                tx = _Names[name].BuildTx();
                return this;
            }
        }
        [Fact]
        public void CanCompactTx()
        {
            var builder = new GraphBuilder();
            var graph =
                builder
                .PushNewTx(name: "a", fee: Money.Satoshis(12345))
                    .AddInput()
                    .AddInput()
                    .IsMine()
                    .PushNewTx(name: "b", fee: Money.Satoshis(12345))
                        .AddOutput()
                        .IsMine()
                        .Pop()
                    .PushNewTx(name: "b2", fee: Money.Satoshis(12345))
                        .IsMine()
                        .PushNewTx(name: "c", fee: Money.Satoshis(12345))
                            .IsMine()
                            .Pop()
                        .Pop()
                    .Pop()
                .PushNewTx(name: "a2", fee: Money.Satoshis(12345))
                    .AddInput()
                    .AddOutput()
                .GetTx("a", out var a)
                .GetTx("b", out var b)
                .GetTx("b2", out var b2)
                .GetTx("c", out var c)
                .GetTx("a2", out var a2)
                .Build();


            AssertRoots(graph, [a, a2]);
            AssertLeafs(graph, [a2, b, c]);
            

            graph = AssertReplacement(graph, [a],
                [
                    [a, b, b2, c]
                ], out var replacements);
            var ar = replacements[0].NewNode.Tx;
            AssertRoots(graph, [ar, a2]);
            AssertLeafs(graph, [ar, a2]);
            Assert.Empty(graph.TxsById[ar.GetHash()].Children);

            //// Reset
            graph = builder.Build();
            graph = AssertReplacement(graph, [b2],
                [
                    [b2, c]
                ], out replacements);


            // Same as previous, except that c is also spending a2
            builder = new GraphBuilder();
            graph = builder
                .PushNewTx(name: "a", fee: Money.Satoshis(100))
                    .AddInput()
                    .AddInput()
                    .IsMine()
                    .PushNewTx(name: "b", fee: Money.Satoshis(200))
                        .AddOutput()
                        .IsMine()
                        .Pop()
                    .PushNewTx(name: "b2", fee: Money.Satoshis(400))
                        .IsMine()
                        .PushNewTx(name: "c", fee: Money.Satoshis(800))
                            .IsMine()
                            .Pop()
                        .Pop()
                    .Pop()
                .PushNewTx(name: "a2", fee: Money.Satoshis(1600))
                    .AddInput()
                    .AddOutput()
                    .SpentBy("b2")
                    .IsMine()
                .GetTx("a", out a)
                .GetTx("b", out b)
                .GetTx("b2", out b2)
                .GetTx("c", out c)
                .GetTx("a2", out a2)
                .Build();
            graph = AssertReplacement(graph, [a2],
                [
                    [a2, b2, c]
                ], out replacements);
            Assert.Equal(5 - 2, graph.TxsById.Count);
            graph = builder.Build();

            var expectedOutpoints = graph.RootNodes.SelectMany(r => r.Tx.Inputs.Select(i => i.PrevOut)).ToArray();
            var expectedTotalFee = graph.GetTotalFee();
            Assert.Equal(100 + 200 + 400 + 800 + 1600, expectedTotalFee.Satoshi);

            graph = graph.Compact([a.GetHash(), a2.GetHash()], out replacements);
            Assert.Equal(2, replacements.Count);

            // Compacting a will compact a, b, b2 and c into an
            var an = AssertReplacement(replacements[0], [a, b, b2, c]);
            an.ComputeEffectiveFeeRate();

            // `an` ancestor fee is the sum of all the compacted txs + the parent, a2.
            Assert.Equal(100 + 200 + 400 + 800 + 1600, an.AncestorFee.Satoshi);

            // Then, compacting a2 will compact a2 and an into a2n
            var a2n = AssertReplacement(replacements[1], [an.Tx, a2]);
            // End fee should just be the sum of the fee of all the txs that got compacted
            Assert.Equal(100 + 200 + 400 + 800 + 1600, an.AncestorFee.Satoshi);
            Assert.Equal(expectedTotalFee, a2n.AncestorFee);

            // Make sure that the new single transaction include all the inputs
            var actualOutpoints = a2n.Tx.Inputs.Select(i => i.PrevOut).ToArray();
            Assert.Equivalent(expectedOutpoints, actualOutpoints, true);

            // This will strip `an` from the list of replacements, as a2n is compacting it
            replacements.StripIntermediate();
            var r = Assert.Single(replacements);
            Assert.Equal(a2n, r.NewNode);
            AssertReplacement(r, [a, b, b2, a2, c]);
        }

        private Node AssertReplacement(Replacement replacement, IEnumerable<Transaction> replaced)
        {
            foreach (var r in replaced)
            {
                Assert.Contains(replacement.ReplacedTransactions, rr => rr.GetHash() == r.GetHash());
            }
            Assert.Equal(replaced.Count(), replacement.ReplacedTransactions.Length);
            return replacement.NewNode;
        }

        private UnconfTxGraph AssertReplacement(UnconfTxGraph graph, Transaction[] replaced, Transaction[][] expectedReplacements, out Replacements replacements)
        {
            var newGraph = graph.Compact(replaced.Select(r => r.GetHash()).ToArray(), out replacements);
            Assert.Equal(replaced.Length, expectedReplacements.Length);
            for (int i = 0; i < replaced.Length; i++)
            {
                var replacement = Assert.Single(replacements, r => r.ReplacedTransactions.Contains(replaced[i]));

                var expectedReplacedTransactions = expectedReplacements[i];
                Assert.Equal(expectedReplacedTransactions.Length, replacement.ReplacedTransactions.Length);
                foreach (var item in expectedReplacedTransactions)
                {
                    Assert.Contains(replacement.ReplacedTransactions, r => r.GetHash() == item.GetHash());
                }
            }
            return newGraph;
        }

        private void AssertLeafs(UnconfTxGraph graph, Transaction[] txs, bool strict = true)
        {
            foreach (var tx in txs)
            {
                Assert.Contains(graph.LeafNodes, r => r.Hash == tx.GetHash());
                Assert.True(graph.TxsById.ContainsKey(tx.GetHash()));
            }
            if (strict)
                Assert.Equal(graph.LeafNodes.Length, txs.Length);
        }
        private void AssertRoots(UnconfTxGraph graph, Transaction[] txs, bool strict = true)
        {
            foreach (var tx in txs)
            {
                Assert.Contains(graph.RootNodes, r => r.Hash == tx.GetHash());
            }
            if (strict)
                Assert.Equal(graph.RootNodes.Length, txs.Length);
        }
    }
}
