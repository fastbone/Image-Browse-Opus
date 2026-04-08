namespace ImageBrowse.Services.Abstractions;

public interface IImageLoadingService
{
    object? LoadFullImage(string filePath, int maxDimension = 0);
}
