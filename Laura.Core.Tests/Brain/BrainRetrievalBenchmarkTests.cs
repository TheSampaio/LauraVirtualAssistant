using System.Diagnostics;
using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Laura.Core.Tests.Brain;

/// <summary>A reproducible synthetic excerpt benchmark. It does not claim production semantic precision or remote-model performance.</summary>
public sealed class BrainRetrievalBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LateAnswerCoverageImprovesAgainstThePreviousWholeQuestionExcerptAlgorithm()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-retrieval-benchmark-" + Guid.NewGuid().ToString("N"));
        var settings = new StubSettingsService(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(root, "Brain") } });
        var brain = new BrainService(settings, new AssistantStorage(root), new BrainIndex(Path.Combine(root, "Cache"), new NoEmbedding()), location => new FileBrainStorage(location), NullLogger<BrainService>.Instance);
        (string Topic, string Query, string Answer, string Text)[] cases =
        [
            ("Chronometer", "How does chronometer resolve clock skew?", "offset-window consensus", "Chronometer resolves clock skew using offset-window consensus."),
            ("Nebula", "What handles nebula retry exhaustion?", "jitter-bounded backoff", "Nebula handles retry exhaustion using jitter-bounded backoff."),
            ("Prisma", "Qual mecanismo do prisma valida snapshots de domínio?", "assinatura-janela", "O Prisma valida snapshots de domínio com assinatura-janela."),
            ("Atlas", "Where is atlas buffer ownership checked?", "sentinel_atlas_417", "```csharp\n// Atlas buffer ownership\nAtlasBuffer.ValidateOwnership(owner); // sentinel_atlas_417\n```"),
            ("Lunaris", "How is lunaris schema upgraded without data loss?", "journal-before-replace", "Lunaris upgrades schema without data loss by journal-before-replace.")
        ];
        var notes = new Dictionary<string, BrainNote>(StringComparer.Ordinal); var timings = new List<double>();
        try
        {
            foreach (var item in cases)
            {
                string body = "# " + item.Topic + " Manual\n\n" + string.Concat(Enumerable.Repeat("Fictional introductory garden material unrelated to the requested implementation detail.\n\n", 60))
                    + "## " + item.Topic + " Implementation\n\n" + item.Text;
                notes[item.Topic] = await brain.SaveAsync(new() { Path = "Knowledge/Manuals/" + item.Topic + ".md", Title = item.Topic + " Manual", Body = body });
            }
            int previousAnswers = 0, currentAnswers = 0, correctFirst = 0;
            foreach (var item in cases)
            {
                BrainNote expected = notes[item.Topic]; int position = expected.Body.IndexOf(item.Query, StringComparison.OrdinalIgnoreCase);
                int previousStart = Math.Max(0, position - 100); string previous = expected.Body.Substring(previousStart, Math.Min(800, expected.Body.Length - previousStart));
                if (previous.Contains(item.Answer, StringComparison.Ordinal)) { previousAnswers++; }
                var hits = await brain.SearchAsync(item.Query); BrainHit hit = Assert.Single(hits, hit => hit.Note.Id == expected.Id);
                if (hits[0].Note.Id == expected.Id) { correctFirst++; }
                if (hit.Excerpt.Contains(item.Answer, StringComparison.Ordinal)) { currentAnswers++; }
                Assert.True(hit.Excerpt.Length <= 800); Assert.True(hit.ExcerptStart > 3000); Assert.NotNull(hit.Passage);
                for (int repetition = 0; repetition < 10; repetition++)
                { var clock = Stopwatch.StartNew(); await brain.SearchAsync(item.Query); timings.Add(clock.Elapsed.TotalMilliseconds); }
            }
            timings.Sort();
            output.WriteLine("Corpus: 5 synthetic Markdown documents, 5 English/Portuguese questions, 50 warmed lexical searches; no ONNX model or cloud calls.");
            output.WriteLine($"Previous excerpt answer coverage: {previousAnswers}/5; passage excerpt answer coverage: {currentAnswers}/5; expected document precision@1: {correctFirst}/5.");
            output.WriteLine("Warmed retrieval p50/p95 milliseconds: " + timings[24].ToString("F2", CultureInfo.InvariantCulture) + "/" + timings[47].ToString("F2", CultureInfo.InvariantCulture));
            Assert.Equal(0, previousAnswers); Assert.Equal(5, currentAnswers); Assert.Equal(5, correctFirst);
        }
        finally
        {
            await brain.DisposeAsync(); string full = Path.GetFullPath(root);
            if (!full.StartsWith(Path.Combine(Path.GetTempPath(), "laura-retrieval-benchmark-"), StringComparison.OrdinalIgnoreCase)) { throw new IOException("Invalid synthetic cleanup root."); }
            if (Directory.Exists(full)) { Directory.Delete(full, true); }
        }
    }
    private sealed class NoEmbedding : IBrainEmbedding
    {
        public string ModelId => "";
        public float[] Embed(string text) => [];
    }
}
