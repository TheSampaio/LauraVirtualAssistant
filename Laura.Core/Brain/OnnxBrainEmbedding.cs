using System.Globalization;
using System.Text.RegularExpressions;
using Laura.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Laura.Core.Brain;

/// <summary>Optional CPU BERT/WordPiece sentence embeddings, with no downloads or cloud transmission.</summary>
public sealed partial class OnnxBrainEmbedding(ISettingsService settings) : IBrainEmbedding, IDisposable
{
    private readonly object _gate = new();
    private InferenceSession? _session;
    private Dictionary<string, int> _vocabulary = new(StringComparer.Ordinal);
    private string _identity = "";
    /// <inheritdoc />
    public string ModelId
    {
        get
        {
            lock (_gate)
            {
                string model = settings.Current.Brain.EmbeddingModel, vocabulary = settings.Current.Brain.EmbeddingVocabulary;
                if (!File.Exists(model) || !File.Exists(vocabulary)) { Close(); return ""; }
                string identity = model + ":" + new FileInfo(model).Length.ToString(CultureInfo.InvariantCulture) + ":" + File.GetLastWriteTimeUtc(model).Ticks.ToString(CultureInfo.InvariantCulture)
                    + ":" + vocabulary + ":" + File.GetLastWriteTimeUtc(vocabulary).Ticks.ToString(CultureInfo.InvariantCulture);
                if (_identity == identity) { return _identity; }
                Close();
                using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
                _session = new InferenceSession(model, options);
                _vocabulary = File.ReadLines(vocabulary).Select((word, id) => (word, id)).ToDictionary(item => item.word, item => item.id, StringComparer.Ordinal);
                if (!_vocabulary.ContainsKey("[CLS]") || !_vocabulary.ContainsKey("[SEP]") || !_vocabulary.ContainsKey("[UNK]") || !_session.InputMetadata.ContainsKey("input_ids"))
                { Close(); throw new InvalidOperationException("The local model requires a matching BERT WordPiece vocabulary and input_ids."); }
                _identity = identity; return identity;
            }
        }
    }
    /// <inheritdoc />
    public float[] Embed(string text)
    {
        lock (_gate)
        {
            if (ModelId.Length == 0 || _session is null) { return []; }
            var ids = new List<long> { _vocabulary["[CLS]"] };
            foreach (Match token in TokenPattern().Matches(text.ToLowerInvariant()))
            {
                string word = token.Value; int start = 0; var pieces = new List<long>();
                while (start < word.Length && word.Length <= 100)
                {
                    int end = word.Length; int found = -1;
                    while (end > start)
                    {
                        string piece = (start == 0 ? "" : "##") + word[start..end];
                        if (_vocabulary.TryGetValue(piece, out found)) { break; } end--;
                    }
                    if (end == start) { pieces.Clear(); pieces.Add(_vocabulary["[UNK]"]); break; }
                    pieces.Add(found); start = end;
                }
                if (word.Length > 100) { pieces.Add(_vocabulary["[UNK]"]); }
                ids.AddRange(pieces.Take(126 - ids.Count)); if (ids.Count >= 126) { break; }
            }
            ids.Add(_vocabulary["[SEP]"]); int length = ids.Count;
            var inputs = new List<NamedOnnxValue>();
            foreach (string name in _session.InputMetadata.Keys)
            {
                long[] values = name switch { "input_ids" => ids.ToArray(), "attention_mask" => Enumerable.Repeat(1L, length).ToArray(), "token_type_ids" => new long[length], _ => throw new InvalidOperationException("Unsupported local model input.") };
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, [1, length])));
            }
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs);
            Tensor<float> tensor = outputs[0].AsTensor<float>();
            int dimension = tensor.Dimensions[^1];
            if (dimension is < 32 or > 4096 || tensor.Rank is < 2 or > 3) { throw new InvalidOperationException("Unsupported local embedding output."); }
            float[] vector = new float[dimension]; float[] valuesOut = tensor.ToArray();
            if (tensor.Rank == 2) { Array.Copy(valuesOut, vector, dimension); }
            else
            {
                int tokens = tensor.Dimensions[1];
                for (int token = 0; token < tokens; token++) { for (int channel = 0; channel < dimension; channel++) { vector[channel] += valuesOut[token * dimension + channel] / tokens; } }
            }
            if (vector.Any(value => !float.IsFinite(value))) { throw new InvalidOperationException("Embedding output contains invalid values."); }
            double norm = Math.Sqrt(vector.Sum(value => (double)value * value));
            if (norm > 0) { for (int channel = 0; channel < dimension; channel++) { vector[channel] = (float)(vector[channel] / norm); } }
            return vector;
        }
    }
    /// <summary>Releases the previous native model when local configuration changes.</summary>
    private void Close() { _session?.Dispose(); _session = null; _identity = ""; _vocabulary.Clear(); }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { Close(); } GC.SuppressFinalize(this); }
    /// <summary>Splits Unicode words and punctuation before WordPiece segmentation.</summary>
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+|[^\s\p{L}\p{M}\p{N}]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
