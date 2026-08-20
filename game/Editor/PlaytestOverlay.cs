using System;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Hosts a playtest run inside the level editor. <see cref="Start"/> builds the play world — via the
    /// shared <see cref="PlayRuntimeBuilder"/>, the SAME builder <see cref="LevelPlay"/> uses — from a
    /// <see cref="ResolvedLevel"/> snapshot the caller projects from the level currently being edited
    /// (<c>Uberkarl.Editor.EditableLevelSnapshot.ToResolvedLevel</c>), and raises <see cref="ExitRequested"/>
    /// when the author presses the return control (<c>ui_cancel</c> — Escape on keyboard, the B button on
    /// gamepad) so the host can tear the run down and restore editing.
    ///
    /// Building a fresh play world on every <see cref="Start"/> and freeing the whole subtree on
    /// <see cref="Stop"/> is what keeps the in-progress edit buffer untouched: this overlay never reads
    /// from or writes back into the editor's session/model — it only ever consumes a one-shot, already
    /// -built <see cref="ResolvedLevel"/> value. There is no "restore the buffer" step because playtesting
    /// never had a way to touch it in the first place.
    /// </summary>
    public partial class PlaytestOverlay : Control {

        Node2D playWorld;

        /// <summary>Raised when the author asks to return to the editor.</summary>
        public event Action ExitRequested;

        /// <summary>True while a playtest run is live.</summary>
        public bool IsPlaying => playWorld != null;

        public override void _Ready() {
            EditorLayout.FillParent(this);
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        /// <summary>Starts a playtest run of <paramref name="level"/>. No-op if a run is already live.</summary>
        public void Start(ResolvedLevel level) {
            if (IsPlaying)
                return;

            playWorld = new Node2D { Name = "Playtest" };
            AddChild(playWorld);
            PlayRuntimeBuilder.Populate(playWorld, level);
            Visible = true;
        }

        /// <summary>Tears the run down (frees the whole play-world subtree, including its camera).
        /// No-op if not playing.</summary>
        public void Stop() {
            if (!IsPlaying)
                return;

            playWorld.QueueFree();
            playWorld = null;
            Visible = false;
        }

        // The one input this overlay owns: the return control. Everything else (movement, jump) is read
        // directly off the global Input singleton by Player, so it needs no wiring here.
        public override void _UnhandledInput(InputEvent @event) {
            if (!IsPlaying || !@event.IsActionPressed("ui_cancel"))
                return;

            GetViewport().SetInputAsHandled();
            ExitRequested?.Invoke();
        }
    }
}
