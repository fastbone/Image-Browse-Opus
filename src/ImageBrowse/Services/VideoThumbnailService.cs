using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace ImageBrowse.Services;

public sealed class VideoThumbnailService : IDisposable
{
    private readonly LibVLC _libVLC;
    private const int ThumbnailSize = 256;
    private const int SnapshotTimeoutMs = 10000;

    public VideoThumbnailService()
    {
        _libVLC = new LibVLC("--no-audio", "--no-xlib");
    }

    /// <returns>Tuple of (jpegBytes, width, height, duration) or null on failure.</returns>
    public (byte[] Data, int Width, int Height, TimeSpan Duration)? GenerateThumbnail(string filePath)
    {
        try
        {
            using var media = new Media(_libVLC, filePath, FromType.FromPath);
            media.Parse(MediaParseOptions.ParseLocal, timeout: 5000).GetAwaiter().GetResult();

            long durationMs = media.Duration;
            if (durationMs <= 0) return null;

            var duration = TimeSpan.FromMilliseconds(durationMs);

            int videoWidth = 0, videoHeight = 0;
            foreach (var track in media.Tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    videoWidth = (int)track.Data.Video.Width;
                    videoHeight = (int)track.Data.Video.Height;
                    break;
                }
            }

            if (videoWidth <= 0 || videoHeight <= 0)
                return null;

            double scale = Math.Min((double)ThumbnailSize / videoWidth, (double)ThumbnailSize / videoHeight);
            scale = Math.Min(scale, 1.0);
            int thumbW = ((int)(videoWidth * scale) + 1) & ~1; // ensure even
            int thumbH = ((int)(videoHeight * scale) + 1) & ~1;
            int pitch = thumbW * 4;

            var frameBuffer = new byte[pitch * thumbH];
            var bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            var frameReady = new ManualResetEventSlim(false);

            try
            {
                using var player = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                player.SetVideoFormat("RV32", (uint)thumbW, (uint)thumbH, (uint)pitch);

                var ptr = bufferHandle.AddrOfPinnedObject();
                player.SetVideoCallbacks(
                    lockCb: (IntPtr opaque, IntPtr planes) =>
                    {
                        Marshal.WriteIntPtr(planes, ptr);
                        return IntPtr.Zero;
                    },
                    unlockCb: null,
                    displayCb: (IntPtr opaque, IntPtr picture) => frameReady.Set()
                );

                using var thumbMedia = new Media(_libVLC, filePath, FromType.FromPath);
                player.Play(thumbMedia);

                long seekTarget = Math.Min(durationMs / 10, 5000);
                player.Time = seekTarget;

                if (!frameReady.Wait(SnapshotTimeoutMs))
                {
                    player.Stop();
                    return null;
                }

                player.Stop();
            }
            finally
            {
                bufferHandle.Free();
            }

            var jpegData = EncodeToJpeg(frameBuffer, thumbW, thumbH, pitch);
            return (jpegData, videoWidth, videoHeight, duration);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] EncodeToJpeg(byte[] bgra, int width, int height, int stride)
    {
        var bmp = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null,
            bgra, stride);
        bmp.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _libVLC.Dispose();
    }
}
