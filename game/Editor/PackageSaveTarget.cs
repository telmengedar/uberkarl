using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// What <see cref="PackageBrowser"/>'s Save/Save-As flow (DiVoid #7552, corrected under the
    /// package-as-VFS save model #7571/#7572) hands back once the author has picked a target package (or
    /// named a new one) and settled on a level resource. Exactly one of
    /// <see cref="ExistingHandle"/>/<see cref="NewPackageName"/> is set: <see cref="ExistingHandle"/> when
    /// merging into an archive already in the source — picked directly from the list, or resolved as a
    /// same-name collision against a typed "+ New package…" name (<see cref="PackageSaveTargetResolver"/>)
    /// — <see cref="NewPackageName"/> when none collided and a brand-new archive should be minted.
    /// <see cref="LevelName"/> is always set: the level's new display name, applied
    /// (<see cref="Uberkarl.Editor.LevelEditSession.RenameLevel"/>) before the level is serialized.
    /// <see cref="OverwriteResourcePath"/> is set only when the author explicitly picked an existing level
    /// resource inside <see cref="ExistingHandle"/> to overwrite (as opposed to "＋ New level…") — the
    /// level attaches to that exact path rather than deriving a fresh one from <see cref="LevelName"/>.
    /// </summary>
    public readonly struct PackageSaveTarget {

        public PackageHandle? ExistingHandle { get; }

        public string NewPackageName { get; }

        public string LevelName { get; }

        public ResourcePath? OverwriteResourcePath { get; }

        public PackageSaveTarget(PackageHandle? existingHandle, string newPackageName, string levelName, ResourcePath? overwriteResourcePath) {
            ExistingHandle = existingHandle;
            NewPackageName = newPackageName;
            LevelName = levelName;
            OverwriteResourcePath = overwriteResourcePath;
        }
    }
}
