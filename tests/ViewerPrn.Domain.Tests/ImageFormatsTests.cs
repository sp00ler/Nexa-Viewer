using ViewerPrn.Domain.Images;

namespace ViewerPrn.Domain.Tests;

public sealed class ImageFormatsTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPG")]
    [InlineData("photo.jpeg")]
    [InlineData(@"E:\holiday\IMG_0042.HEIC")]
    [InlineData("scan.tiff")]
    [InlineData("icon.ico")]
    public void RecognisesImages(string name) => Assert.True(ImageFormats.IsImage(name));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("archive.rar")]
    [InlineData("noextension")]
    [InlineData("")]
    public void RejectsEverythingElse(string name) => Assert.False(ImageFormats.IsImage(name));
}
