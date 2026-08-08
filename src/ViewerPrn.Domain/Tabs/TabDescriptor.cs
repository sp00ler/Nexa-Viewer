namespace ViewerPrn.Domain.Tabs;

/// <summary>
/// One Explorer tab. Phase 1 keeps identity, source path and title only; selection and
/// view/sort state join in Phases 2-3 (docs/REQUIREMENTS.md:10).
/// </summary>
public sealed record TabDescriptor(Guid Id, string Path, string Title);
