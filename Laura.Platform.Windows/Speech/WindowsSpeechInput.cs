using System.Speech.Recognition;
using Laura.Core.Abstractions;
using Laura.Core.Speech;
using NAudio.Wave;
using Laura.Core.Models;
using Whisper.net;
using System.Text;

namespace Laura.Platform.Windows.Speech;

/// <summary>Offline Windows dictation with bounded in-memory microphone capture.</summary>
public sealed class WindowsSpeechInput : ISpeechInput
{
    private readonly ILocalModelManager _models;
    /// <summary>Connects the shared verified artifact manager.</summary>
    public WindowsSpeechInput(ILocalModelManager models) => _models = models;

    /// <inheritdoc />
    public IReadOnlyList<string> Languages => File.Exists(_models.GetModelPath(SupportedModels.Speech.Id)) ? ["en-US", "pt-BR"]
        : [.. SpeechRecognitionEngine.InstalledRecognizers().Select(static info => info.Culture.Name).Distinct()];
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> InputDevices => [.. Enumerable.Range(0, WaveIn.DeviceCount)
        .Select(static id => new AudioDevice(id, WaveIn.GetCapabilities(id).ProductName))];
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> OutputDevices => [.. Enumerable.Range(0, WaveOut.DeviceCount)
        .Select(static id => new AudioDevice(id, WaveOut.GetCapabilities(id).ProductName))];

    /// <inheritdoc />
    public async Task<string?> RecognizeAsync(string culture, int device, CancellationToken stopRecording, CancellationToken cancellationToken)
    {
        RecognizerInfo? recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(
            info => info.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase));
        bool useWhisper = File.Exists(_models.GetModelPath(SupportedModels.Speech.Id));
        if (!useWhisper && recognizerInfo is null) { throw new InvalidOperationException("Download the multilingual speech model on the Voice page to recognize this language offline."); }
        if (WaveIn.DeviceCount == 0 || device >= WaveIn.DeviceCount || device < -1)
        {
            throw new InvalidOperationException("The selected microphone is unavailable.");
        }
        using var capture = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        using var stream = new MemoryStream();
        using var writer = new WaveFileWriter(stream, capture.WaveFormat);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, args) =>
        {
            if (writer.Length + args.BytesRecorded <= 16000 * 2 * 15) { writer.Write(args.Buffer, 0, args.BytesRecorded); }
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null) { stopped.TrySetException(args.Exception); }
            else { stopped.TrySetResult(); }
        };
        cancellationToken.ThrowIfCancellationRequested();
        capture.StartRecording();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopRecording, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using CancellationTokenRegistration stop = deadline.Token.Register(capture.StopRecording);
        await stopped.Task.ConfigureAwait(false);
        writer.Flush();
        byte[] wave = stream.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (useWhisper)
        {
            return await TranscribeAsync(wave, culture, cancellationToken).ConfigureAwait(false);
        }
        return await Task.Run(async () =>
        {
            using var recognizer = new SpeechRecognitionEngine(recognizerInfo!);
            recognizer.LoadGrammar(new DictationGrammar());
            using var audio = new MemoryStream(wave, writable: false);
            recognizer.SetInputToWaveStream(audio);
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.RecognizeCompleted += (_, args) =>
            {
                if (args.Error is not null) { completion.TrySetException(args.Error); }
                else { completion.TrySetResult(args.Result?.Text); }
            };
            recognizer.RecognizeAsync(RecognizeMode.Single);
            using CancellationTokenRegistration cancel = cancellationToken.Register(recognizer.RecognizeAsyncCancel);
            string? text = await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads the packaged native CPU speech runtime and discards weights/audio after transcription.</summary>
    public async Task<string> TranscribeAsync(byte[] wave, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wave);
        return await Task.Run(async () =>
        {
            using var factory = WhisperFactory.FromPath(_models.GetModelPath(SupportedModels.Speech.Id));
            using var processor = factory.CreateBuilder().WithLanguage(culture == "pt-BR" ? "pt" : "en").Build();
            using var audio = new MemoryStream(wave, writable: false);
            var result = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Append(segment.Text).Append(' ');
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result.ToString().Trim();
        }, cancellationToken).ConfigureAwait(false);
    }
}
