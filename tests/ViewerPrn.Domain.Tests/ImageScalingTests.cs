using ViewerPrn.Domain.Images;

namespace ViewerPrn.Domain.Tests;

public sealed class ImageScalingTests
{
    [Fact]
    public void SmallImagesAreNeverEnlarged()
    {
        Assert.Equal(new PixelSize(80, 60), ImageScaling.FitDown(new PixelSize(80, 60), new PixelSize(1920, 1080)));
    }

    [Fact]
    public void AnImageThatExactlyFitsIsUnchanged()
    {
        Assert.Equal(new PixelSize(1920, 1080), ImageScaling.FitDown(new PixelSize(1920, 1080), new PixelSize(1920, 1080)));
    }

    [Fact]
    public void WideImagesAreLimitedByWidth()
    {
        Assert.Equal(new PixelSize(1000, 250), ImageScaling.FitDown(new PixelSize(4000, 1000), new PixelSize(1000, 1000)));
    }

    [Fact]
    public void TallImagesAreLimitedByHeight()
    {
        Assert.Equal(new PixelSize(250, 1000), ImageScaling.FitDown(new PixelSize(1000, 4000), new PixelSize(1000, 1000)));
    }

    [Fact]
    public void AspectRatioIsPreservedWithinARoundingPixel()
    {
        PixelSize result = ImageScaling.FitDown(new PixelSize(3000, 2000), new PixelSize(640, 640));

        Assert.Equal(640, result.Width);
        Assert.Equal(427, result.Height);
    }

    [Fact]
    public void AnExtremeRatioStillKeepsAtLeastOnePixel()
    {
        PixelSize result = ImageScaling.FitDown(new PixelSize(10000, 1), new PixelSize(100, 100));

        Assert.Equal(100, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void NonPositiveDimensionsAreRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageScaling.FitDown(new PixelSize(width, height), new PixelSize(10, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageScaling.FitDown(new PixelSize(10, 10), new PixelSize(width, height)));
    }

    [Theory]
    [InlineData(ImageOrientation.Normal, 400, 300)]
    [InlineData(ImageOrientation.FlipHorizontal, 400, 300)]
    [InlineData(ImageOrientation.Rotate180, 400, 300)]
    [InlineData(ImageOrientation.FlipVertical, 400, 300)]
    [InlineData(ImageOrientation.Transpose, 300, 400)]
    [InlineData(ImageOrientation.Rotate90, 300, 400)]
    [InlineData(ImageOrientation.Transverse, 300, 400)]
    [InlineData(ImageOrientation.Rotate270, 300, 400)]
    public void QuarterTurnsSwapTheDimensions(ImageOrientation orientation, int expectedWidth, int expectedHeight)
    {
        PixelSize displayed = ImageScaling.ApplyOrientation(new PixelSize(400, 300), orientation);

        Assert.Equal(new PixelSize(expectedWidth, expectedHeight), displayed);
    }

    [Fact]
    public void ARotatedPortraitPhotoFitsByItsDisplayedShape()
    {
        // 4000x3000 stored, rotated a quarter turn: it is a 3000x4000 portrait on screen and
        // must be limited by height, not width.
        PixelSize displayed = ImageScaling.ApplyOrientation(new PixelSize(4000, 3000), ImageOrientation.Rotate90);

        Assert.Equal(new PixelSize(750, 1000), ImageScaling.FitDown(displayed, new PixelSize(1000, 1000)));
    }
}
