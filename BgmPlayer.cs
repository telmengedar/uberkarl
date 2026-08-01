using System;
using System.Globalization;
using Godot;
using Pooshit.AudioSynth.Formats.Midi;
using Pooshit.AudioSynth.Formats.Sf2;
using Pooshit.AudioSynth.Sequencing;
using Pooshit.AudioSynth.Sequencing.Timeline;
using Pooshit.AudioSynth.Synthesis;

namespace Uberkarl {

    /// <summary>
    /// Smoke-test BGM node: pulls a looping MIDI performance live from Pooshit.AudioSynth's
    /// <see cref="RealtimeSequencer"/> into a Godot <see cref="AudioStreamGenerator"/> each frame.
    /// </summary>
    public partial class BgmPlayer : AudioStreamPlayer {

        const string Sf2Path = "res://audio/florestan_gm.sf2";
        const string MidiPath = "res://audio/test_song.mid";
        const int SampleRate = 44100;
        const int Channels = 2;
        const int BlockFrames = 512;
        const int MaxVoices = 64;
        const float BufferLengthSeconds = 0.5f;
        const float LogIntervalSeconds = 1.0f;

        RealtimeSequencer sequencer;
        AudioStreamGeneratorPlayback playback;
        float[] scratchFloat;
        Vector2[] scratchVector;
        long totalFramesPushed;
        long framesPushedSinceLog;
        float peakSinceLog;
        double sumSquaresSinceLog;
        double logAccumulator;

        /// <summary>Loads the soundfont and MIDI, builds the realtime sequencer, and starts the generator stream.</summary>
        public override void _Ready() {
            try {
                using System.IO.Stream sf2Stream = System.IO.File.OpenRead(ProjectSettings.GlobalizePath(Sf2Path));
                SoundBank bank = new Sf2SoundBankLoader(SampleRate).Load(sf2Stream);

                using System.IO.Stream midiStream = System.IO.File.OpenRead(ProjectSettings.GlobalizePath(MidiPath));
                TimedMessageSequence sequence = new TimedMessageSequence(MidiFile.Read(midiStream));

                SynthesizerOptions options = new SynthesizerOptions(SampleRate, Channels, BlockFrames, MaxVoices,
                    ReverbSettings.Default, globalReverb: false, ChorusSettings.Default, globalChorus: false, masterGain: 1f);
                Synthesizer synth = new Synthesizer(options, bank.GetPatch(0, 0));

                CompiledSchedule schedule = MidiTimelineImporter.Import(sequence, SampleRate).Compile();
                long releaseTailFrames = (long)(MidiSequencer.ReleaseTailSeconds * SampleRate);
                long lastEventOffset = schedule.Count > 0 ? schedule.Entries[schedule.Count - 1].SampleOffset : 0;
                long loopEnd = lastEventOffset + releaseTailFrames;
                sequencer = new RealtimeSequencer(schedule, synth, bank, releaseTailFrames, loopStart: 0, loopEnd: loopEnd);

                Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferLengthSeconds };
                Play();
                playback = (AudioStreamGeneratorPlayback)GetStreamPlayback();

                int maxFrames = (int)(SampleRate * BufferLengthSeconds) + 1;
                scratchFloat = new float[maxFrames * Channels];
                scratchVector = new Vector2[maxFrames];
            } catch (Exception ex) {
                GD.PrintErr($"BgmPlayer: failed to initialize ({ex.GetType().Name}): {ex.Message}");
            }
        }

        /// <summary>Pulls one block from the sequencer, converts it to stereo frames, and feeds the generator.</summary>
        public override void _Process(double delta) {
            if (sequencer == null || playback == null)
                return;

            int framesAvailable = Math.Min(playback.GetFramesAvailable(), scratchVector.Length);
            if (framesAvailable <= 0)
                return;

            Span<float> destination = scratchFloat.AsSpan(0, framesAvailable * Channels);
            int samplesWritten = sequencer.Read(destination);
            int framesWritten = samplesWritten / Channels;

            for (int i = 0; i < framesWritten; i++) {
                float left = scratchFloat[2 * i];
                float right = scratchFloat[2 * i + 1];
                scratchVector[i] = new Vector2(left, right);
                sumSquaresSinceLog += (double)(left * left) + (double)(right * right);
                float sampleAbs = Math.Max(Math.Abs(left), Math.Abs(right));
                if (sampleAbs > peakSinceLog)
                    peakSinceLog = sampleAbs;
            }
            playback.PushBuffer(scratchVector.AsSpan(0, framesWritten).ToArray());

            totalFramesPushed += framesWritten;
            framesPushedSinceLog += framesWritten;
            logAccumulator += delta;
            if (logAccumulator >= LogIntervalSeconds) {
                double meanSquare = framesPushedSinceLog > 0 ? sumSquaresSinceLog / (framesPushedSinceLog * 2) : 0;
                GD.Print($"BgmPlayer: framesPushed={framesPushedSinceLog} totalFramesPushed={totalFramesPushed} " +
                    $"peak={peakSinceLog.ToString("F4", CultureInfo.InvariantCulture)} " +
                    $"rms={Math.Sqrt(meanSquare).ToString("F4", CultureInfo.InvariantCulture)} skips={playback.GetSkips()}");
                logAccumulator = 0;
                framesPushedSinceLog = 0;
                peakSinceLog = 0f;
                sumSquaresSinceLog = 0;
            }
        }
    }
}
