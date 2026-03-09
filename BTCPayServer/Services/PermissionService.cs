#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Client;

namespace BTCPayServer.Services;

public record PermissionDefinitionNode(
    PolicyDefinition Definition,
    IReadOnlyCollection<PermissionDefinitionNode> Children,
    IReadOnlyCollection<PermissionDefinitionNode> Parents)
{
    public IEnumerable<PermissionDefinitionNode> EnumerateDescendants(bool includeSelf = true)
    {
        if (includeSelf)
            yield return this;
        foreach (var descendant in Children.SelectMany(c => c.EnumerateDescendants()))
            yield return descendant;
    }
    public IEnumerable<PermissionDefinitionNode> EnumerateParents(bool includeSelf = true)
    {
        if (includeSelf)
            yield return this;
        foreach (var parent in Parents.SelectMany(p => p.EnumerateParents()))
            yield return parent;
    }
}

public class PermissionService
{
    record PermissionDefinitionNodeBuilder(
        PolicyDefinition Definition,
        List<PermissionDefinitionNodeBuilder> Children,
        List<PermissionDefinitionNodeBuilder> Parents)
    {
        public void AddChild(PermissionDefinitionNodeBuilder node)
        {
            Children.Add(node);
            node.Parents.Add(this);
        }

        public void Build(Dictionary<string, PermissionDefinitionNode> nodes)
        {
            HashSet<string> visited = new();
            var unrestricted = Build(nodes, null, visited);
            AddParents(unrestricted);
        }

        private void AddParents(PermissionDefinitionNode node)
        {
            foreach (var child in node.Children)
            {
                ((List<PermissionDefinitionNode>)child.Parents).Add(node);
                AddParents(child);
            }
        }

        PermissionDefinitionNode Build(Dictionary<string, PermissionDefinitionNode> nodes, PermissionDefinitionNode? parent, HashSet<string> visited)
        {
            if (!visited.Add($"{parent} -> {Definition.Policy}"))
                throw new InvalidOperationException($"Circular reference detected in permissions [{Definition}]");
            if (nodes.TryGetValue(Definition.Policy, out var n))
                return n;

            var children = new List<PermissionDefinitionNode>();
            n = new PermissionDefinitionNode(Definition, children, new List<PermissionDefinitionNode>());
            nodes.Add(Definition.Policy, n);
            children.AddRange(Children.Select(c => c.Build(nodes, n, visited)));
            return n;
        }
    }

    private readonly IReadOnlyDictionary<string, PolicyDefinition> _definitions;

    public PermissionService(IEnumerable<PolicyDefinition> definitions)
    {
        var definitionsByPermission = new Dictionary<string, PolicyDefinition>(StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<PolicyDefinition, PermissionDefinitionNodeBuilder>();
        foreach (var definition in definitions)
        {
            definitionsByPermission[definition.Policy] = definition;
            nodes.Add(definition, new PermissionDefinitionNodeBuilder(definition, new(), new()));
        }

        _definitions = new ReadOnlyDictionary<string, PolicyDefinition>(definitionsByPermission);

        foreach (var node in nodes)
        {
            foreach (var included in node.Key.IncludedPermissions)
                node.Value.AddChild(nodes[GetPolicyDefinition(definitionsByPermission, included)]);

            foreach (var includedBy in node.Key.IncludedByPermissions)
                nodes[GetPolicyDefinition(definitionsByPermission, includedBy)].AddChild(node.Value);
        }

        var unrestricted = nodes[definitionsByPermission[Policies.Unrestricted]];
        foreach (var node in nodes)
        {
            if (node.Key.Policy == Policies.Unrestricted)
                continue;
            if (node.Value.Parents.Count is 0)
                unrestricted.AddChild(node.Value);
        }

        var permNodes = new Dictionary<string, PermissionDefinitionNode>();
        unrestricted.Build(permNodes);
        PermissionNodesByPolicy = new ReadOnlyDictionary<string, PermissionDefinitionNode>(permNodes);
        UnrestrictedPermissionNode = permNodes[Policies.Unrestricted];
        Definitions = _definitions;
    }

    private static PolicyDefinition GetPolicyDefinition(Dictionary<string, PolicyDefinition> definitionsByPermission, string included)
    {
        if (definitionsByPermission.TryGetValue(included, out var definition))
            return definition;
        throw new ArgumentException($"Permission '{included}' is not defined");
    }

    public IReadOnlyDictionary<string, PermissionDefinitionNode> PermissionNodesByPolicy { get; }
    public PermissionDefinitionNode UnrestrictedPermissionNode { get; }

    public IReadOnlyDictionary<string, PolicyDefinition> Definitions { get; }

    public bool TryGetDefinition(string permission, [MaybeNullWhen(false)] out PolicyDefinition definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(permission))
            return false;
        return _definitions.TryGetValue(permission, out definition);
    }

    public PolicyDefinition? TryGetDefinition(string permission)
    {
        this.TryGetDefinition(permission, out var definition);
        return definition;
    }

    public bool IsValidPolicy(string policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
            return false;
        return _definitions.ContainsKey(policy);
    }

    public bool Contains(Permission permission, Permission requestedPermission, bool anyScope = false)
    {
        if (permission is null)
            throw new ArgumentNullException(nameof(permission));
        if (requestedPermission is null)
            throw new ArgumentNullException(nameof(requestedPermission));
        if (!ContainsPolicy(permission.Policy, requestedPermission.Policy))
            return false;
        return permission.Scope == null ||
               anyScope || requestedPermission.Scope == permission.Scope;
    }

    private bool ContainsPolicy(string policy, string subpolicy)
    {
        if (!PermissionNodesByPolicy.TryGetValue(policy, out var policyNode) ||
            !PermissionNodesByPolicy.TryGetValue(subpolicy, out var subPolicyNode))
            return false;

        return subPolicyNode.EnumerateParents().Any(p => p == policyNode);
    }
}
