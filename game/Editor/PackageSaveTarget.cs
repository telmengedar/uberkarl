using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// What <see cref="PackageBrowser"/>'s Save/Save-As flow (DiVoid #7552) hands back once the author has
    /// picked a target package (or named a new one) and typed the level name. Exactly one of
    /// <see cref="ExistingHandle"/>/<see cref="NewPackageName"/> is set: <see cref="ExistingHandle"/> when
    /// writing into a package already in the source — picked directly from the list, or resolved as a
    /// same-name collision against a typed "+ New package…" name (<see cref="PackageSaveTargetResolver"/>)
    /// — <see cref="NewPackageName"/> when none collided and a brand-new package should be created.
    /// <see cref="LevelName"/> is always set: the level's new display name, applied (<see cref="Uberkarl.Editor.LevelEditSession.RenameLevel"/>)
    /// before the level is serialized.
    /// </summary>
    public readonly struct PackageSaveTarget {

        public PackageHandle? ExistingHandle { get; }

        public string NewPackageName { get; }

        public string LevelName { get; }

        public PackageSaveTarget(PackageHandle? existingHandle, string newPackageName, string levelName) {
            ExistingHandle = existingHandle;
            NewPackageName = newPackageName;
            LevelName = levelName;
        }
    }
}
