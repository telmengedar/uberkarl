using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first in-game file browser for loading and saving a level (DiVoid #7470 for
    /// load, #7552 for the file-browser look + save flow). It holds only the source abstraction and the
    /// opaque summaries/handles it hands back — no file, ZIP, or path knowledge, and no edit logic. Every
    /// step is rendered by the shared <see cref="ChoiceList"/>; this class owns the flow — which step
    /// comes next, what a chosen row means, and what cancel does at each step.
    ///
    /// <b>Load mode</b> (<see cref="SummonLoad"/>): packages → select → that package's levels → select →
    /// <see cref="ResourceChosen"/>.
    ///
    /// <b>Save mode</b> (<see cref="SummonSave"/>): packages (plus a leading "+ New package…" entry) →
    /// pick an existing package, or type a new one's name via the attached <see cref="OnScreenKeyboard"/>
    /// (with a same-name collision folded into a confirm-overwrite step rather than silently minting a
    /// distinct file — <see cref="PackageSaveTargetResolver"/>) → type the level's name → <see cref="SaveRequested"/>.
    /// The keyboard is a separate summoned overlay (<see cref="AttachKeyboard"/>, exactly like
    /// <see cref="LayerManagerPanel"/>'s rename affordance) that sits on top of whichever list step is
    /// current without the browser itself changing step underneath it.
    /// </summary>
    public partial class PackageBrowser : Control {

        enum Mode { Load, Save }
        enum Step { Packages, Resources, ConfirmOverwrite, SaveResources }

        // Which situation the current ConfirmOverwrite step is resolving — a same-name "+ New package…"
        // collision (confirm folds back into the save-resources step, since the target became an existing
        // package) or an explicitly-picked existing level resource (confirm proceeds straight to naming).
        enum ConfirmKind { NewPackageNameCollision, ResourceOverwrite }

        IPackageSource source;
        Mode mode;
        Step step;
        ConfirmKind confirmKind;
        IReadOnlyList<PackageSummary> packages = Array.Empty<PackageSummary>();
        IReadOnlyList<ResourceSummary> resources = Array.Empty<ResourceSummary>();
        PackageHandle selectedPackage;
        string selectedPackageName = string.Empty;
        string pendingNewPackageName;
        string currentLevelName = string.Empty;
        string collisionName = string.Empty;
        // Set when the author explicitly picked an existing level resource to overwrite (the save-resources
        // step's list, not "+ New level…") — carried through to the emitted PackageSaveTarget so the level
        // attaches to this exact path rather than deriving a fresh one (DiVoid #7571/#7572).
        ResourcePath? pendingOverwriteResource;

        ChoiceList choiceList;
        OnScreenKeyboard keyboard;

        /// <summary>Raised in load mode when a resource is chosen: the package it lives in and its in-package path.</summary>
        public event Action<PackageHandle, ResourcePath> ResourceChosen;

        /// <summary>Raised in save mode once a target package and a level name have both been settled.</summary>
        public event Action<PackageSaveTarget> SaveRequested;

        /// <summary>Raised when the browser is dismissed without completing either flow.</summary>
        public event Action Cancelled;

        /// <summary>True while the browser is summoned.</summary>
        public bool IsOpen => choiceList.IsOpen;

        /// <summary>Attaches the shared <see cref="ChoiceList"/> every step of this browser renders through.</summary>
        public void AttachChoiceList(ChoiceList list) {
            choiceList = list;
            choiceList.DismissSuppressed = () => keyboard != null && keyboard.IsOpen;
        }

        /// <summary>
        /// Attaches the shared <see cref="OnScreenKeyboard"/> the save flow's naming steps summon (DiVoid
        /// #7513). Called once by <see cref="LevelEditor"/> alongside construction, exactly like
        /// <see cref="LayerManagerPanel.AttachKeyboard"/>.
        /// </summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>Summon the browser in load mode against <paramref name="packageSource"/>, showing its packages.</summary>
        public void SummonLoad(IPackageSource packageSource) {
            source = packageSource;
            mode = Mode.Load;
            packages = source.ListPackages();
            ShowPackages();
        }

        /// <summary>
        /// Summon the browser in save mode against <paramref name="packageSource"/>, seeding the eventual
        /// level-name prompt with <paramref name="currentName"/> (the level's existing name, so re-saving
        /// under the same name is a single confirm rather than retyping it).
        /// </summary>
        public void SummonSave(IPackageSource packageSource, string currentName) {
            source = packageSource;
            mode = Mode.Save;
            currentLevelName = currentName ?? string.Empty;
            packages = source.ListPackages();
            ShowPackages();
        }

        // ----- packages step (shared by both modes; save mode prepends "+ New package…") -----

        void ShowPackages() {
            step = Step.Packages;
            pendingOverwriteResource = null;

            int newRowCount = mode == Mode.Save ? 1 : 0;
            string title = mode == Mode.Load ? "Open Package" : "Save To Package";
            string empty = mode == Mode.Load
                ? "No packages in the content folder."
                : "No packages yet — choose “+ New package…” to create one.";
            choiceList.Open(title, "✕ Close", packages.Count + newRowCount, PackageRow, empty, OnPackageRowChosen, HandleCancel);
        }

        ChoiceListRow PackageRow(int index) {
            if (mode == Mode.Save && index == 0)
                return new ChoiceListRow("+ New package…", string.Empty);

            PackageSummary summary = packages[mode == Mode.Save ? index - 1 : index];
            string itemWord = summary.ResourceCount == 1 ? "item" : "items";
            return new ChoiceListRow(summary.Name, $"v{summary.Version} · {summary.ResourceCount} {itemWord}");
        }

        void OnPackageRowChosen(int index) {
            if (mode == Mode.Load) {
                if (index < 0 || index >= packages.Count)
                    return;
                selectedPackage = packages[index].Handle;
                selectedPackageName = packages[index].Name;
                ShowResources();
                return;
            }

            if (index == 0) {
                OpenNewPackageNameKeyboard();
                return;
            }

            int packageIndex = index - 1;
            if (packageIndex < 0 || packageIndex >= packages.Count)
                return;

            selectedPackage = packages[packageIndex].Handle;
            selectedPackageName = packages[packageIndex].Name;
            pendingNewPackageName = null;
            pendingOverwriteResource = null;
            ShowSaveResources(); // an existing package: pick which level resource to save as (DiVoid #7571/#7572)
        }

        // ----- load mode: resources step -----

        void ShowResources() {
            IReadOnlyList<ResourceSummary> contents;
            try {
                contents = source.GetContents(selectedPackage);
            } catch (Exception exception) {
                GD.PrintErr($"PackageBrowser: {exception.GetType().Name}: {exception.Message}");
                ShowPackages();
                return;
            }

            List<ResourceSummary> levels = new List<ResourceSummary>();
            foreach (ResourceSummary entry in contents)
                if (entry.Kind == ResourceKind.Level)
                    levels.Add(entry);
            resources = levels;

            step = Step.Resources;
            choiceList.Open($"{selectedPackageName} — Select Level", "← Back", resources.Count, ResourceRow, "No loadable resources in this package.", OnResourceChosen, HandleCancel);
        }

        ChoiceListRow ResourceRow(int index) {
            ResourceSummary summary = resources[index];
            return new ChoiceListRow(summary.DisplayName, FormatBytes(summary.ByteLength));
        }

        void OnResourceChosen(int index) {
            if (index < 0 || index >= resources.Count)
                return;

            PackageHandle handle = selectedPackage;
            ResourcePath path = resources[index].Path;
            Close();
            ResourceChosen?.Invoke(handle, path);
        }

        static string FormatBytes(long byteLength) =>
            byteLength < 1024 ? $"{byteLength} B" : $"{byteLength / 1024.0:0.#} KB";

        // ----- save mode: pick a level resource within the target package (DiVoid #7571/#7572) -----

        // Mirrors the "+ New package…" pattern already shipped for the package step: once an EXISTING
        // package is the target, its existing level resources are offered alongside "+ New level…" —
        // picking one replaces it (one confirm, reusing ConfirmOverwrite below); picking "+ New level…"
        // names a brand-new resource. A "+ New package…" target skips this step entirely (no resources
        // exist yet in an archive that has not been created).
        void ShowSaveResources() {
            IReadOnlyList<ResourceSummary> contents;
            try {
                contents = source.GetContents(selectedPackage);
            } catch (Exception exception) {
                GD.PrintErr($"PackageBrowser: {exception.GetType().Name}: {exception.Message}");
                ShowPackages();
                return;
            }

            List<ResourceSummary> levels = new List<ResourceSummary>();
            foreach (ResourceSummary entry in contents)
                if (entry.Kind == ResourceKind.Level)
                    levels.Add(entry);
            resources = levels;

            step = Step.SaveResources;
            // Unlike the load-mode resources step, "+ New level…" is always offered — an empty package is
            // a perfectly normal save target, never a dead end.
            choiceList.Open($"{selectedPackageName} — Save Level", "← Back", resources.Count + 1, SaveResourceRow, string.Empty, OnSaveResourceChosen, HandleCancel);
        }

        ChoiceListRow SaveResourceRow(int index) {
            if (index == 0)
                return new ChoiceListRow("+ New level…", string.Empty);

            ResourceSummary summary = resources[index - 1];
            return new ChoiceListRow(summary.DisplayName, FormatBytes(summary.ByteLength));
        }

        void OnSaveResourceChosen(int index) {
            if (index == 0) {
                pendingOverwriteResource = null;
                OpenLevelNameKeyboard();
                return;
            }

            int resourceIndex = index - 1;
            if (resourceIndex < 0 || resourceIndex >= resources.Count)
                return;

            ResourceSummary picked = resources[resourceIndex];
            pendingOverwriteResource = picked.Path;
            collisionName = picked.DisplayName;
            confirmKind = ConfirmKind.ResourceOverwrite;
            ShowConfirmOverwrite();
        }

        // ----- save mode: naming (via the attached keyboard) + confirm-overwrite -----

        void OpenNewPackageNameKeyboard() {
            if (keyboard == null)
                return;
            keyboard.RequestText("New package name", string.Empty, OnNewPackageNameCommitted);
        }

        void OnNewPackageNameCommitted(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                ShowPackages(); // nothing typed — bounce back rather than silently doing nothing
                return;
            }

            PackageHandle? collision = PackageSaveTargetResolver.FindCollision(packages, name);
            if (collision is { } handle) {
                selectedPackage = handle;
                selectedPackageName = name.Trim();
                pendingNewPackageName = null;
                collisionName = name.Trim();
                confirmKind = ConfirmKind.NewPackageNameCollision;
                ShowConfirmOverwrite();
            } else {
                pendingNewPackageName = name.Trim();
                pendingOverwriteResource = null;
                OpenLevelNameKeyboard();
            }
        }

        void ShowConfirmOverwrite() {
            step = Step.ConfirmOverwrite;
            string title = confirmKind == ConfirmKind.NewPackageNameCollision
                ? $"A package named “{collisionName}” already exists."
                : $"Overwrite the level “{collisionName}”?";
            choiceList.Open(title, "← Back", 2, ConfirmOverwriteRow, string.Empty, OnConfirmOverwriteChosen, HandleCancel);
        }

        static ChoiceListRow ConfirmOverwriteRow(int index) => index == 0
            ? new ChoiceListRow("Overwrite it", string.Empty)
            : new ChoiceListRow("Choose a different name", string.Empty);

        void OnConfirmOverwriteChosen(int index) {
            if (index == 0) {
                if (confirmKind == ConfirmKind.NewPackageNameCollision)
                    ShowSaveResources(); // selectedPackage now holds the collided handle — pick its level resource
                else
                    OpenLevelNameKeyboard(); // resource overwrite confirmed — proceed with pendingOverwriteResource set
            } else {
                if (confirmKind == ConfirmKind.NewPackageNameCollision)
                    ShowPackages();
                else
                    ShowSaveResources();
            }
        }

        void OpenLevelNameKeyboard() {
            if (keyboard == null)
                return;

            string target = pendingNewPackageName ?? selectedPackageName;
            // An explicitly-picked existing resource seeds the keyboard with ITS name (confirming/tweaking
            // the spelling of the slot just picked), not whatever name the currently-open level happens to
            // carry — the pick itself is the intent (design #7572 decision 4).
            string seed = pendingOverwriteResource != null ? collisionName : currentLevelName;
            keyboard.RequestText($"Level name — saving into “{target}”", seed, OnLevelNameCommitted);
        }

        void OnLevelNameCommitted(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                ShowPackages(); // no name typed — back to picking a target rather than saving blank
                return;
            }

            PackageSaveTarget target = pendingNewPackageName != null
                ? new PackageSaveTarget(null, pendingNewPackageName, name.Trim(), null)
                : new PackageSaveTarget(selectedPackage, null, name.Trim(), pendingOverwriteResource);

            Close();
            SaveRequested?.Invoke(target);
        }

        // ----- back-navigation / close -----

        // The step this browser is on decides what "cancel" (ui_cancel or the header's Back/Close button,
        // both routed here by ChoiceList) means: one level back out of Resources/SaveResources/
        // ConfirmOverwrite into Packages (a resource-overwrite confirm steps back to the save-resources
        // list instead, mirroring its "no" branch above), or a full close from Packages itself.
        void HandleCancel() {
            switch (step) {
                case Step.Resources:
                case Step.SaveResources:
                    ShowPackages();
                    break;
                case Step.ConfirmOverwrite:
                    if (confirmKind == ConfirmKind.ResourceOverwrite)
                        ShowSaveResources();
                    else
                        ShowPackages();
                    break;
                default:
                    Close();
                    Cancelled?.Invoke();
                    break;
            }
        }

        void Close() {
            choiceList.Hide();
            pendingNewPackageName = null;
            pendingOverwriteResource = null;
        }
    }
}
