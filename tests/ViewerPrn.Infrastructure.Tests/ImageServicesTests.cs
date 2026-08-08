using ViewerPrn.Application.Abstractions;
using ViewerPrn.Domain.Images;
using ViewerPrn.Infrastructure.Images;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ViewerPrn.Infrastructure.Tests;

/// <summary>
/// Exercises the real Windows imaging stack against a file written for the test — no fixture
/// images checked into the repository.
/// </summary>
public sealed class ImageServicesTests
{
    private static async Task<string> WriteJpegAsync(TempDirectory temp, string name, int width, int height)
    {
        string path = temp.Combine(name);

        using (InMemoryRandomAccessStream memory = new())
        {
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memory);
            byte[] pixels = new byte[width * height * 4];
            Array.Fill(pixels, (byte)0x80);

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)width,
                (uint)height,
                96,
                96,
                pixels);
            await encoder.FlushAsync();

            memory.Seek(0);
            byte[] bytes = new byte[memory.Size];
            using DataReader reader = new(memory);
            await reader.LoadAsync((uint)memory.Size);
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
        }

        return path;
    }

    [Fact]
    public async Task ReadsDimensionsFromARealFile()
    {
        using TempDirectory temp = new();
        string path = await WriteJpegAsync(temp, "photo.jpg", 40, 20);

        ImageMetadata metadata = await new WicMetadataReader().ReadAsync(path);

        Assert.Equal(new PixelSize(40, 20), metadata.StoredSize);
        Assert.Equal(new PixelSize(40, 20), metadata.DisplaySize);
    }

    [Fact]
    public async Task MissingExifLeavesTheOptionalFieldsEmpty()
    {
        using TempDirectory temp = new();
        string path = await WriteJpegAsync(temp, "bare.jpg", 16, 16);

        ImageMetadata metadata = await new WicMetadataReader().ReadAsync(path);

        Assert.Equal(ImageOrientation.Normal, metadata.Orientation);
        Assert.Null(metadata.CameraModel);
        Assert.Null(metadata.IsoSpeed);
        Assert.Null(metadata.FNumber);
    }

    [Fact]
    public async Task ReadingSomethingThatIsNotAnImageFails()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("notes.txt");
        await File.WriteAllTextAsync(path, "this is not a picture");

        await Assert.ThrowsAnyAsync<Exception>(() => new WicMetadataReader().ReadAsync(path));
    }

    [Fact]
    public async Task ProducesAThumbnail()
    {
        using TempDirectory temp = new();
        string path = await WriteJpegAsync(temp, "thumb.jpg", 200, 120);
        using ShellThumbnailProvider provider = new();

        byte[]? thumbnail = await provider.GetThumbnailAsync(path, File.GetLastWriteTimeUtc(path), 96);

        Assert.NotNull(thumbnail);
        Assert.NotEmpty(thumbnail);
    }

    [Fact]
    public async Task TheSecondRequestIsServedFromTheCache()
    {
        using TempDirectory temp = new();
        string path = await WriteJpegAsync(temp, "cached.jpg", 200, 120);
        using ShellThumbnailProvider provider = new();
        DateTimeOffset modified = File.GetLastWriteTimeUtc(path);

        byte[]? first = await provider.GetThumbnailAsync(path, modified, 96);
        long bytesAfterFirst = provider.CachedBytes;
        byte[]? second = await provider.GetThumbnailAsync(path, modified, 96);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(bytesAfterFirst, provider.CachedBytes);
    }

    [Fact]
    public async Task AMissingFileYieldsNoThumbnailRatherThanAnError()
    {
        using TempDirectory temp = new();
        using ShellThumbnailProvider provider = new();

        Assert.Null(await provider.GetThumbnailAsync(temp.Combine("ghost.jpg"), DateTimeOffset.Now, 96));
    }
}
