#nullable enable
namespace BTCPayServer.Components.TruncateCenter;

public class TruncateCenterViewModel
{
    public string Text { get; init; } = null!;
    public string? Start { get; set; }
    public string? End { get; set; }
    public string? Id { get; init; }
    public string? Classes { get; init; }
    public string? Link { get; init; }
    public int Padding { get; init; }
    public bool Copy { get; init; }
    public bool Elastic { get; init; }
    public bool IsVue { get; init; }
    public bool IsTruncated { get; init; }
}
