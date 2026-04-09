using System.Runtime.InteropServices;
using ImageBrowse.Services;
using LibVLCSharp.Shared;

namespace ImageBrowse.Services;

/// <summary>LibVLC snapshot for gallery video thumbnails (shared LibVLC instance).</summary>
internal static class AvaloniaVideoThumbnailHelper
{
    private const int ThumbnailSize = 256;
    private const int SnapshotTimeoutMs = 10000;

    internal static string[] LibVlcThumbnailArgs()
    {
        var list = new List<string> { "--no-audio" };
        if (OperatingSystem.IsLinux())
            list.Add("--no-xlib");
        return list.ToArray();
    }

    internal static (byte[] Data, int Width, int Height, TimeSpan Duration)? GenerateThumbnail(LibVLC libVlc, string filePath)
    {
        try
        {
            using var media = new Media(libVlc, filePath, FromType.FromPath);
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
            int thumbW = ((int)(videoWidth * scale) + 1) & ~1;
            int thumbH = ((int)(videoHeight * scale) + 1) & ~1;
            int pitch = thumbW * 4;

            var frameBuffer = new byte[pitch * thumbH];
            var bufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            var frameReady = new ManualResetEventSlim(false);

            try
            {
                using var player = new MediaPlayer(libVlc);
                player.SetVideoFormat("RV32", (uint)thumbW, (uint)thumbH, (uint)pitch);

                var ptr = bufferHandle.AddrOfPinnedObject();
                player.SetVideoCallbacks(
                    lockCb: (_, planes) =>
                    {
                        Marshal.WriteIntPtr(planes, ptr);
                        return IntPtr.Zero;
                    },
                    unlockCb: null,
                    displayCb: (_, _) => frameReady.Set()
                );

                using var thumbMedia = new Media(libVlc, filePath, FromType.FromPath);
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

            var jpegData = VideoFrameJpegEncoder.EncodeBgraToJpeg(frameBuffer, thumbW, thumbH, pitch);
            return (jpegData, videoWidth, videoHeight, duration);
        }
        catch
        {
            return null;
        }
    }
}
