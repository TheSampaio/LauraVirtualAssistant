using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Laura.App.Platforms.Windows;

/// <summary>Captures one explicitly requested camera frame, releasing the camera immediately afterward.</summary>
public static class WindowsCameraCapture
{
    /// <summary>Initializes video-only capture and returns a JPEG without opening the microphone.</summary>
    public static async Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
    {
        using var capture = new MediaCapture();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Video }).AsTask(cancellationToken).ConfigureAwait(true);
        using var stream = new InMemoryRandomAccessStream();
        await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream).AsTask(cancellationToken).ConfigureAwait(true);
        stream.Seek(0); byte[] bytes = new byte[checked((int)stream.Size)];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length).AsTask(cancellationToken).ConfigureAwait(true); reader.ReadBytes(bytes); return bytes;
    }
}
