namespace ViewerPrn.Domain.Viewer;

/// <summary>
/// The intro/cycle parameters derived from the total image count of a gallery.
/// </summary>
/// <param name="TotalImages">Physical image count of the gallery.</param>
/// <param name="IntroCount">Y in <c>X(Y)/Z</c> — introductory images excluded from the cycle.</param>
/// <param name="CycleLength">Z in <c>X(Y)/Z</c>.</param>
public readonly record struct CycleDefinition(int TotalImages, int IntroCount, int CycleLength);
