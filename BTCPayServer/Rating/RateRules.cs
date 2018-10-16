using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BTCPayServer.Rating
{
    public enum RateRulesErrors
    {
        Ok,
        TooMuchNestedCalls,
        InvalidCurrencyIdentifier,
        NestedInvocation,
        UnsupportedOperator,
        MissingArgument,
        DivideByZero,
        InvalidNegative,
        PreprocessError,
        RateUnavailable,
        InvalidExchangeName,
    }
    public class RateRules
    {
        class NormalizeCurrencyPairsRewritter : CSharpSyntaxRewriter
        {
            public List<RateRulesErrors> Errors = new List<RateRulesErrors>();

            bool IsInvocation;
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (IsInvocation)
                {
                    Errors.Add(RateRulesErrors.NestedInvocation);
                    return base.VisitInvocationExpression(node);
                }
                if (node.Expression is IdentifierNameSyntax id)
                {
                    IsInvocation = true;
                    var arglist = (ArgumentListSyntax)this.Visit(node.ArgumentList);
                    IsInvocation = false;
                    return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(id.Identifier.ValueText.ToLowerInvariant()), arglist)
                                        .WithTriviaFrom(id);
                }
                else
                {
                    Errors.Add(RateRulesErrors.InvalidExchangeName);
                    return node;
                }
            }
            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (CurrencyPair.TryParse(node.Identifier.ValueText, out var currencyPair))
                {
                    return SyntaxFactory.IdentifierName(currencyPair.ToString())
                                        .WithTriviaFrom(node);
                }
                else
                {
                    Errors.Add(RateRulesErrors.InvalidCurrencyIdentifier);
                    return base.VisitIdentifierName(node);
                }
            }
        }
        class RuleList : CSharpSyntaxWalker
        {
            public Dictionary<CurrencyPair, (ExpressionSyntax Expression, SyntaxNode Trivia)> ExpressionsByPair = new Dictionary<CurrencyPair, (ExpressionSyntax Expression, SyntaxNode Trivia)>();
            public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                if (node.Kind() == SyntaxKind.SimpleAssignmentExpression
                    && node.Left is IdentifierNameSyntax id
                    && node.Right is ExpressionSyntax expression)
                {
                    if (CurrencyPair.TryParse(id.Identifier.ValueText, out var currencyPair))
                    {
                        expression = expression.WithTriviaFrom(expression);
                        ExpressionsByPair.TryAdd(currencyPair, (expression, id));
                    }
                }
                base.VisitAssignmentExpression(node);
            }

            public SyntaxNode GetSyntaxNode()
            {
                return SyntaxFactory.Block(
                        ExpressionsByPair.Select(e =>
                                SyntaxFactory.ExpressionStatement(
                                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                            SyntaxFactory.IdentifierName(e.Key.ToString()).WithTriviaFrom(e.Value.Trivia),
                                            e.Value.Expression)
                                    ))
                    );
            }
        }

        SyntaxNode root;
        RuleList ruleList;

        decimal _Spread;
        public decimal Spread
        {
            get
            {
                return _Spread;
            }
            set
            {
                if (value > 1.0m || value < 0.0m)
                    throw new ArgumentOutOfRangeException(paramName: nameof(value), message: "The spread should be between 0 and 1");
                _Spread = value;
            }
        }

        RateRules(SyntaxNode root)
        {
            ruleList = new RuleList();
            ruleList.Visit(root);
            // Remove every irrelevant statements
            this.root = ruleList.GetSyntaxNode();
        }
        public static bool TryParse(string str, out RateRules rules)
        {
            return TryParse(str, out rules, out var unused);
        }
        public static bool TryParse(string str, out RateRules rules, out List<RateRulesErrors> errors)
        {
            rules = null;
            errors = null;
            var expression = CSharpSyntaxTree.ParseText(str, new CSharpParseOptions(LanguageVersion.Default).WithKind(SourceCodeKind.Script));
            var rewriter = new NormalizeCurrencyPairsRewritter();
            // Rename BTC_usd to BTC_USD and verify structure
            var root = rewriter.Visit(expression.GetRoot());
            if (rewriter.Errors.Count > 0)
            {
                errors = rewriter.Errors;
                return false;
            }
            rules = new RateRules(root);
            return true;
        }

        public RateRule GetRuleFor(CurrencyPair currencyPair)
        {
            if (currencyPair.Left == "X" || currencyPair.Right == "X")
                throw new ArgumentException(paramName: nameof(currencyPair), message: "Invalid X currency");
            if (currencyPair.Left == currencyPair.Right)
                return new RateRule(this, currencyPair, CreateExpression("1.0"));
            var candidate = FindBestCandidate(currencyPair);
            if (Spread != decimal.Zero)
            {
                candidate = CreateExpression($"({candidate}) * ({(1.0m - Spread).ToString(CultureInfo.InvariantCulture)}, {(1.0m + Spread).ToString(CultureInfo.InvariantCulture)})");
            }
            return new RateRule(this, currencyPair, candidate);
        }

        public ExpressionSyntax FindBestCandidate(CurrencyPair p)
        {
            var invP = p.Inverse();
            var candidates = new List<(CurrencyPair Pair, int Prioriy, ExpressionSyntax Expression, bool Inverse)>();
            foreach (var pair in new[]
            {
                (Pair: p, Priority: 0, Inverse: false),
                (Pair: invP, Priority: 1, Inverse: true),
                (Pair: new CurrencyPair(p.Left, "X"), Priority: 2, Inverse: false),
                (Pair: new CurrencyPair("X", p.Right), Priority: 2, Inverse: false),
                (Pair: new CurrencyPair(invP.Left, "X"), Priority: 3, Inverse: true),
                (Pair: new CurrencyPair("X", invP.Right), Priority: 3, Inverse: true),
                (Pair: new CurrencyPair("X", "X"), Priority: 4, Inverse: false)
            })
            {
                if (ruleList.ExpressionsByPair.TryGetValue(pair.Pair, out var expression))
                {
                    candidates.Add((pair.Pair, pair.Priority, expression.Expression, pair.Inverse));
                }
            }
            if (candidates.Count == 0)
                return CreateExpression($"ERR_NO_RULE_MATCH({p})");
            var best = candidates
                    .OrderBy(c => c.Prioriy)
                    .ThenBy(c => c.Expression.Span.Start)
                    .First();
            return best.Inverse
                   ? CreateExpression($"1 / {invP}")
                   : best.Expression;
        }

        internal static ExpressionSyntax CreateExpression(string str)
        {
            return (ExpressionSyntax)CSharpSyntaxTree.ParseText(str, new CSharpParseOptions(LanguageVersion.Default).WithKind(SourceCodeKind.Script)).GetRoot().ChildNodes().First().ChildNodes().First().ChildNodes().First();
        }

        public override string ToString()
        {
            return root.NormalizeWhitespace("", "\n")
                .ToFullString()
                .Replace("{\n", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Replace("\n}", string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class RateRule
    {
        class ReplaceExchangeRateRewriter : CSharpSyntaxRewriter
        {
            public List<RateRulesErrors> Errors = new List<RateRulesErrors>();
            public ExchangeRates Rates;
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var exchangeName = node.Expression.ToString();
                if (exchangeName.StartsWith("ERR_", StringComparison.OrdinalIgnoreCase))
                {
                    Errors.Add(RateRulesErrors.PreprocessError);
                    return base.VisitInvocationExpression(node);
                }

                var currencyPair = node.ArgumentList.ChildNodes().FirstOrDefault()?.ToString();
                if (currencyPair == null || !CurrencyPair.TryParse(currencyPair, out var pair))
                {
                    Errors.Add(RateRulesErrors.InvalidCurrencyIdentifier);
                    return RateRules.CreateExpression($"ERR_INVALID_CURRENCY_PAIR({node.ToString()})");
                }
                else
                {
                    var rate = Rates.GetRate(exchangeName, pair);
                    if (rate == null)
                    {
                        Errors.Add(RateRulesErrors.RateUnavailable);
                        return RateRules.CreateExpression($"ERR_RATE_UNAVAILABLE({exchangeName}, {pair.ToString()})");
                    }
                    else
                    {
                        return RateRules.CreateExpression(rate.ToString());
                    }
                }
            }
        }

        class CalculateWalker : CSharpSyntaxWalker
        {
            public Stack<BidAsk> Values = new Stack<BidAsk>();
            public List<RateRulesErrors> Errors = new List<RateRulesErrors>();

            public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            {
                base.VisitPrefixUnaryExpression(node);
                bool invalid = false;
                switch (node.Kind())
                {
                    case SyntaxKind.UnaryMinusExpression:
                    case SyntaxKind.UnaryPlusExpression:
                        if (Values.Count < 1)
                        {
                            invalid = true;
                            Errors.Add(RateRulesErrors.MissingArgument);
                        }
                        break;
                    default:
                        invalid = true;
                        Errors.Add(RateRulesErrors.UnsupportedOperator);
                        break;
                }

                if (invalid)
                    return;

                switch (node.Kind())
                {
                    case SyntaxKind.UnaryMinusExpression:
                        var v = Values.Pop();
                        if (v.Bid == v.Ask)
                        {
                            Values.Push(-v);
                        }
                        else
                        {
                            Errors.Add(RateRulesErrors.InvalidNegative);
                        }
                        break;
                    case SyntaxKind.UnaryPlusExpression:
                        Values.Push(+Values.Pop());
                        break;
                    default:
                        throw new NotSupportedException("Should never happen");
                }
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                base.VisitBinaryExpression(node);


                bool invalid = false;
                switch (node.Kind())
                {
                    case SyntaxKind.AddExpression:
                    case SyntaxKind.MultiplyExpression:
                    case SyntaxKind.DivideExpression:
                    case SyntaxKind.SubtractExpression:
                        if (Values.Count < 2)
                        {
                            invalid = true;
                            Errors.Add(RateRulesErrors.MissingArgument);
                        }
                        break;
                }

                if (invalid)
                    return;

                var b = Values.Pop();
                var a = Values.Pop();

                switch (node.Kind())
                {
                    case SyntaxKind.AddExpression:
                        Values.Push(a + b);
                        break;
                    case SyntaxKind.MultiplyExpression:
                        Values.Push(a * b);
                        break;
                    case SyntaxKind.DivideExpression:
                        if (a.Ask == decimal.Zero || b.Ask == decimal.Zero)
                        {
                            Errors.Add(RateRulesErrors.DivideByZero);
                        }
                        else
                        {
                            Values.Push(a / b);
                        }
                        break;
                    case SyntaxKind.SubtractExpression:
                        if (b.Bid == b.Ask)
                        {
                            Values.Push(a - b);
                        }
                        else
                        {
                            Errors.Add(RateRulesErrors.InvalidNegative);
                        }
                        break;
                    default:
                        throw new NotSupportedException("Should never happen");
                }
            }

            Stack<decimal> _TupleValues = null;
            public override void VisitTupleExpression(TupleExpressionSyntax node)
            {
                _TupleValues = new Stack<decimal>();
                base.VisitTupleExpression(node);
                if (_TupleValues.Count != 2)
                {
                    Errors.Add(RateRulesErrors.MissingArgument);
                }
                else
                {
                    var ask = _TupleValues.Pop();
                    var bid = _TupleValues.Pop();
                    Values.Push(new BidAsk(bid, ask));
                }
                _TupleValues = null;
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.NumericLiteralExpression:
                        var v = decimal.Parse(node.ToString(), CultureInfo.InvariantCulture);
                        if (_TupleValues == null)
                            Values.Push(new BidAsk(v));
                        else
                            _TupleValues.Push(v);
                        break;
                }
            }
        }

        class HasBinaryOperations : CSharpSyntaxWalker
        {
            public bool Result = false;
            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                base.VisitBinaryExpression(node);
                switch (node.Kind())
                {
                    case SyntaxKind.AddExpression:
                    case SyntaxKind.MultiplyExpression:
                    case SyntaxKind.DivideExpression:
                    case SyntaxKind.MinusToken:
                        Result = true;
                        break;
                }
            }
        }
        class FlattenExpressionRewriter : CSharpSyntaxRewriter
        {
            RateRules parent;
            CurrencyPair pair;
            int nested = 0;
            public FlattenExpressionRewriter(RateRules parent, CurrencyPair pair)
            {
                this.pair = pair;
                this.parent = parent;
            }

            public ExchangeRates ExchangeRates = new ExchangeRates();
            bool IsInvocation;
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (IsInvocation)
                {
                    Errors.Add(RateRulesErrors.InvalidCurrencyIdentifier);
                    return RateRules.CreateExpression($"ERR_INVALID_CURRENCY_PAIR({node.ToString()})");
                }
                IsInvocation = true;
                _ExchangeName = node.Expression.ToString();
                var result = base.VisitInvocationExpression(node);
                IsInvocation = false;
                return result;
            }

            bool IsArgumentList;
            public override SyntaxNode VisitArgumentList(ArgumentListSyntax node)
            {
                IsArgumentList = true;
                var result = base.VisitArgumentList(node);
                IsArgumentList = false;
                return result;
            }

            string _ExchangeName = null;

            public List<RateRulesErrors> Errors = new List<RateRulesErrors>();
            const int MaxNestedCount = 8;
            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (
                    (!IsInvocation || IsArgumentList) &&
                    CurrencyPair.TryParse(node.Identifier.ValueText, out var currentPair))
                {
                    var replacedPair = new CurrencyPair(left: currentPair.Left == "X" ? pair.Left : currentPair.Left,
                                                       right: currentPair.Right == "X" ? pair.Right : currentPair.Right);
                    if (IsInvocation) // eg. replace bittrex(BTC_X) to bittrex(BTC_USD)
                    {
                        ExchangeRates.Add(new ExchangeRate() { CurrencyPair = replacedPair, Exchange = _ExchangeName });
                        return SyntaxFactory.IdentifierName(replacedPair.ToString());
                    }
                    else // eg. replace BTC_X to BTC_USD, then replace by the expression for BTC_USD
                    {
                        var bestCandidate = parent.FindBestCandidate(replacedPair);
                        if (nested > MaxNestedCount)
                        {
                            Errors.Add(RateRulesErrors.TooMuchNestedCalls);
                            return RateRules.CreateExpression($"ERR_TOO_MUCH_NESTED_CALLS({replacedPair})");
                        }
                        var innerFlatten = CreateNewContext(replacedPair);
                        var replaced = innerFlatten.Visit(bestCandidate);
                        if (replaced is ExpressionSyntax expression)
                        {
                            var hasBinaryOps = new HasBinaryOperations();
                            hasBinaryOps.Visit(expression);
                            if (hasBinaryOps.Result)
                            {
                                replaced = SyntaxFactory.ParenthesizedExpression(expression);
                            }
                        }
                        if (Errors.Contains(RateRulesErrors.TooMuchNestedCalls))
                        {
                            return RateRules.CreateExpression($"ERR_TOO_MUCH_NESTED_CALLS({replacedPair})");
                        }
                        return replaced;
                    }
                }
                return base.VisitIdentifierName(node);
            }

            private FlattenExpressionRewriter CreateNewContext(CurrencyPair pair)
            {
                return new FlattenExpressionRewriter(parent, pair)
                {
                    Errors = Errors,
                    nested = nested + 1,
                    ExchangeRates = ExchangeRates,
                };
            }
        }
        private SyntaxNode expression;
        FlattenExpressionRewriter flatten;

        public RateRule(RateRules parent, CurrencyPair currencyPair, SyntaxNode candidate)
        {
            _CurrencyPair = currencyPair;
            flatten = new FlattenExpressionRewriter(parent, currencyPair);
            this.expression = flatten.Visit(candidate);
        }


        private readonly CurrencyPair _CurrencyPair;
        public CurrencyPair CurrencyPair
        {
            get
            {
                return _CurrencyPair;
            }
        }

        public ExchangeRates ExchangeRates
        {
            get
            {
                return flatten.ExchangeRates;
            }
        }


        public bool Reevaluate()
        {
            _BidAsk = null;
            _EvaluatedNode = null;
            _Evaluated = null;
            Errors.Clear();

            var rewriter = new ReplaceExchangeRateRewriter();
            rewriter.Rates = ExchangeRates;
            var result = rewriter.Visit(this.expression);
            Errors.AddRange(rewriter.Errors);
            _Evaluated = result.NormalizeWhitespace("", "\n").ToString();
            if (HasError)
                return false;

            var calculate = new CalculateWalker();
            calculate.Visit(result);
            if (calculate.Values.Count != 1 || calculate.Errors.Count != 0)
            {
                Errors.AddRange(calculate.Errors);
                return false;
            }
            _BidAsk = calculate.Values.Pop();
            _EvaluatedNode = result;
            return true;
        }


        private readonly HashSet<RateRulesErrors> _Errors = new HashSet<RateRulesErrors>();
        public HashSet<RateRulesErrors> Errors
        {
            get
            {
                return _Errors;
            }
        }

        SyntaxNode _EvaluatedNode;
        string _Evaluated;
        public bool HasError
        {
            get
            {
                return _Errors.Count != 0;
            }
        }

        public string ToString(bool evaluated)
        {
            if (!evaluated)
                return ToString();
            if (_Evaluated == null)
                return "Call Evaluate() first";
            return _Evaluated;
        }

        public override string ToString()
        {
            return expression.NormalizeWhitespace("", "\n").ToString();
        }

        BidAsk _BidAsk;
        public BidAsk BidAsk
        {
            get
            {
                return _BidAsk;
            }
        }
    }
}
