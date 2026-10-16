using System.Diagnostics;
using Laura.Core.Models;
using LLama.Native;
using LLama.Sampling;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Counts actual nonterminal sampled tokens without retaining their identities or changing sampling behavior.</summary>
internal sealed class MeasuredSamplingPipeline : DefaultSamplingPipeline
{
    private long _started, _first, _last;
    private int _count;
    /// <inheritdoc />
    public override void Reset() { base.Reset(); _count = 0; _first = 0; _last = 0; _started = Stopwatch.GetTimestamp(); }
    /// <inheritdoc />
    public override LLamaToken Sample(SafeLLamaContextHandle ctx, int index)
    {
        LLamaToken token = base.Sample(ctx, index);
        if (!token.IsEndOfGeneration(ctx.ModelHandle.Vocab))
        {
            long now = Stopwatch.GetTimestamp(); if (_count++ == 0) { _first = now; } _last = now;
        }
        return token;
    }
    /// <summary>Freezes numeric counters before sampler disposal; generated text, logits and token IDs are excluded.</summary>
    internal LocalInferenceMetrics Snapshot(int promptTokens) => new(promptTokens, _count,
        _count == 0 ? 0 : Stopwatch.GetElapsedTime(_started, _first).TotalMilliseconds,
        _count < 2 ? 0 : Stopwatch.GetElapsedTime(_first, _last).TotalMilliseconds);
}
