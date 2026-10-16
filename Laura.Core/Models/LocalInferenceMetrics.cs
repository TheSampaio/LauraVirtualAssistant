namespace Laura.Core.Models;

/// <summary>Measured sampler counters from one request, including filtered tokens but no retained text or token identities.</summary>
/// <param name="PromptTokens">Actual tokenizer count for the submitted template.</param>
/// <param name="SampledTokens">Nonterminal tokens selected by the native sampler, including filtered output.</param>
/// <param name="FirstSampleMilliseconds">Elapsed time from sampler reset to the first nonterminal token.</param>
/// <param name="SamplingSpanMilliseconds">Elapsed time between the first and last nonterminal token.</param>
public sealed record LocalInferenceMetrics(int PromptTokens, int SampledTokens, double FirstSampleMilliseconds, double SamplingSpanMilliseconds)
{
    /// <summary>Gets sampled tokens per second after the first token, or null when a finite sustained interval was not measured.</summary>
    public double? SampledTokensPerSecond => SampledTokens > 1 && double.IsFinite(SamplingSpanMilliseconds) && SamplingSpanMilliseconds > 0
        ? (SampledTokens - 1) * 1000d / SamplingSpanMilliseconds : null;
}
