namespace Uberkarl.Behavior;

/// <summary>One subject's quarantine, retained by <see cref="BehaviorRuntime"/> for the life of a playtest run — identity copied from the subject, the reason from the raising <see cref="BehaviorQuarantineEvent"/>.</summary>
public sealed record QuarantinedSubject(string SubjectId, string Kind, string Name, GridCell Cell, BehaviorEventKind? TriggeringEvent, string Reason);
