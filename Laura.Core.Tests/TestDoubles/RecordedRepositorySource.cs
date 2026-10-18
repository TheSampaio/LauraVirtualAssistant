using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Brain;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Serves deterministic frozen public-source fixtures while charging every actual bounded read.</summary>
internal sealed class RecordedRepositorySource(int count, int bytesPerFile = 80) : IRemoteBrainSource
{
    internal int Reads { get; private set; }
    internal int Inspections { get; private set; }
    internal int CancelOnRead { get; set; } = int.MaxValue;
    internal bool Truncated { get; set; }
    /// <inheritdoc />
    public Task<RemoteBrainRepository> InspectAsync(GitHubRepositoryAddress address, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); budget.Request(); budget.Receive(128); Inspections++;
        return Task.FromResult(new RemoteBrainRepository(address, new string('b', 40), "MIT", "Synthetic public source",
            Enumerable.Range(0, count).Select(index => new RemoteBrainFile("Module/Source" + index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".cs", new string('a', 40), bytesPerFile)).ToArray(), Truncated));
    }
    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(RemoteBrainRepository repository, RemoteBrainFile file, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        budget.Request(); Reads++;
        if (Reads == CancelOnRead) { await Task.Delay(Timeout.Infinite, cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = Encoding.UTF8.GetBytes(new string('x', file.Size)); budget.Receive(bytes.Length);
        return bytes;
    }
}
