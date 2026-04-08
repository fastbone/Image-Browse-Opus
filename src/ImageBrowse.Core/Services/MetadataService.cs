using ImageBrowse.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using Directory = MetadataExtractor.Directory;

namespace ImageBrowse.Services;

public sealed class MetadataService
{
    private readonly DatabaseService _db;

    public MetadataService(DatabaseService db)
    {
        _db = db;
    }

    public void LoadMetadata(ImageItem item)
    {
        if (item.MetadataLoaded) return;

        var cached = _db.GetMetadata(item.FilePath, item.DateModified);
        if (cached is not null)
        {
            item.DateTaken = cached.DateTaken;
            item.ImageWidth = cached.Width > 0 ? cached.Width : item.ImageWidth;
            item.ImageHeight = cached.Height > 0 ? cached.Height : item.ImageHeight;
            item.CameraManufacturer = cached.CameraMake;
            item.CameraModel = cached.CameraModel;
            item.LensModel = cached.LensModel;
            item.Iso = cached.Iso;
            item.FNumber = cached.FNumber;
            item.ExposureTime = cached.ExposureTime;
            item.FocalLength = cached.FocalLength;
            item.MetadataLoaded = true;
            return;
        }

        try
        {
            IReadOnlyList<Directory> directories = ImageMetadataReader.ReadMetadata(item.FilePath);

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            if (subIfd is not null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                    item.DateTaken = dateTaken;

                item.Iso = subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso) ? iso : null;

                var fn = subIfd.GetDescription(ExifDirectoryBase.TagFNumber);
                if (fn is not null) item.FNumber = fn;

                var exp = subIfd.GetDescription(ExifDirectoryBase.TagExposureTime);
                if (exp is not null) item.ExposureTime = exp;

                var fl = subIfd.GetDescription(ExifDirectoryBase.TagFocalLength);
                if (fl is not null) item.FocalLength = fl;

                if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w))
                    item.ImageWidth = w;
                if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h))
                    item.ImageHeight = h;

                var lens = subIfd.GetDescription(ExifDirectoryBase.TagLensModel);
                if (lens is not null) item.LensModel = lens;
            }

            if (ifd0 is not null)
            {
                var make = ifd0.GetDescription(ExifDirectoryBase.TagMake);
                if (make is not null) item.CameraManufacturer = make.Trim();

                var model = ifd0.GetDescription(ExifDirectoryBase.TagModel);
                if (model is not null) item.CameraModel = model.Trim();

                if (item.ImageWidth == 0 && ifd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var iw))
                    item.ImageWidth = iw;
                if (item.ImageHeight == 0 && ifd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var ih))
                    item.ImageHeight = ih;
            }

            if (item.ImageWidth == 0 || item.ImageHeight == 0)
            {
                var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
                if (jpeg is not null)
                {
                    if (jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var jw)) item.ImageWidth = jw;
                    if (jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var jh)) item.ImageHeight = jh;
                }
            }

            _db.SaveMetadata(item.FilePath, item.DateModified,
                item.DateTaken, item.ImageWidth, item.ImageHeight,
                item.CameraManufacturer, item.CameraModel, item.LensModel,
                item.Iso, item.FNumber, item.ExposureTime, item.FocalLength);

            item.MetadataLoaded = true;
        }
        catch
        {
        }
    }
}
