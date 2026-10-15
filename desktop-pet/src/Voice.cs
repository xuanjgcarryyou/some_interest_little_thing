using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Speech.Synthesis;

namespace DesktopPet
{
    enum Mouth { Closed, Small, A, O }

    // Plays a character's voice and reports which mouth shape to show right now.
    // Recorded clips drive the mouth from their loudness; text-to-speech drives it from SAPI visemes.
    class Voice : IDisposable
    {
        const int EnvelopeFps = 60;

        readonly Stopwatch clock = new Stopwatch();
        readonly Random rnd = new Random();
        SpeechSynthesizer tts;
        bool ttsUnavailable;
        Prompt ttsPrompt;
        Mouth ttsMouth;
        long ttsMouthUntilMs;
        SoundPlayer player;
        float[] envelope; // loudness of the current clip, 0..1 per 1/60 s
        Mouth loudShape = Mouth.A;
        bool wasLoud;

        public bool Muted;

        public bool IsSpeaking
        {
            get { return ttsPrompt != null || (envelope != null && clock.ElapsedMilliseconds * EnvelopeFps / 1000 < envelope.Length); }
        }

        // Plays the clip if there is one, otherwise reads the text aloud.
        public void Speak(byte[] clip, string text)
        {
            Stop();
            if (Muted) return;
            if (clip != null) PlayClip(clip);
            else if (!string.IsNullOrEmpty(text)) SpeakText(text);
        }

        public void Stop()
        {
            if (player != null) player.Stop();
            envelope = null;
            if (ttsPrompt != null)
            {
                ttsPrompt = null;
                tts.SpeakAsyncCancelAll();
            }
            ttsMouth = Mouth.Closed;
        }

        public Mouth CurrentMouth()
        {
            long now = clock.ElapsedMilliseconds;
            if (ttsPrompt != null) return now <= ttsMouthUntilMs ? ttsMouth : Mouth.Closed;
            if (envelope == null) return Mouth.Closed;

            long i = now * EnvelopeFps / 1000;
            if (i >= envelope.Length) { envelope = null; return Mouth.Closed; }
            float e = envelope[i];
            if (e < 0.12f) { wasLoud = false; return Mouth.Closed; }
            if (e < 0.4f) { wasLoud = false; return Mouth.Small; }
            if (!wasLoud) { loudShape = rnd.Next(3) == 0 ? Mouth.O : Mouth.A; wasLoud = true; } // new syllable
            return loudShape;
        }

        void PlayClip(byte[] wav)
        {
            float[] env = Envelope(wav);
            if (env == null) return;
            if (player != null) player.Dispose();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
            envelope = env;
            clock.Restart();
        }

        void SpeakText(string text)
        {
            if (ttsUnavailable) return;
            if (tts == null)
            {
                try
                {
                    tts = new SpeechSynthesizer();
                    if (!SelectVoice(tts))
                    {
                        // Without a Chinese voice the default (English) one would mangle the lines; stay silent.
                        tts.Dispose();
                        tts = null;
                        ttsUnavailable = true;
                        return;
                    }
                    tts.SetOutputToDefaultAudioDevice();
                    tts.VisemeReached += OnViseme;
                    tts.SpeakCompleted += delegate(object s, SpeakCompletedEventArgs e)
                    {
                        if (e.Prompt == ttsPrompt) { ttsPrompt = null; ttsMouth = Mouth.Closed; }
                    };
                }
                catch (Exception)
                {
                    tts = null; // no speech engine available: stay silent
                    ttsUnavailable = true;
                    return;
                }
            }
            clock.Restart();
            ttsMouthUntilMs = 0;
            ttsPrompt = tts.SpeakAsync(text.Replace("～", "").Replace("…", "，"));
        }

        void OnViseme(object sender, VisemeReachedEventArgs e)
        {
            ttsMouth = MapViseme(e.Viseme);
            ttsMouthUntilMs = clock.ElapsedMilliseconds + (long)e.Duration.TotalMilliseconds;
        }

        // SAPI viseme ids (0 = silence ... 21 = p/b/m) folded into our four mouth shapes.
        static Mouth MapViseme(int v)
        {
            switch (v)
            {
                case 0: case 21: return Mouth.Closed;
                case 1: case 2: case 9: case 11: return Mouth.A;
                case 3: case 7: case 8: case 10: return Mouth.O;
                default: return Mouth.Small;
            }
        }

        static bool SelectVoice(SpeechSynthesizer s)
        {
            foreach (InstalledVoice v in s.GetInstalledVoices())
            {
                if (v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("zh") && v.VoiceInfo.Gender == VoiceGender.Female)
                {
                    s.SelectVoice(v.VoiceInfo.Name);
                    return true;
                }
            }
            return false;
        }

        // RMS loudness per 1/60 s of a 16-bit PCM WAV, normalized so the loudest window is 1.
        static float[] Envelope(byte[] wav)
        {
            if (wav.Length < 44 || Ascii(wav, 0) != "RIFF" || Ascii(wav, 8) != "WAVE") return null;
            int channels = 0, rate = 0, bits = 0, dataStart = -1, dataLen = 0;
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string id = Ascii(wav, pos);
                int len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, pos + 10);
                    rate = BitConverter.ToInt32(wav, pos + 12);
                    bits = BitConverter.ToInt16(wav, pos + 22);
                }
                else if (id == "data")
                {
                    dataStart = pos + 8;
                    dataLen = Math.Min(len, wav.Length - dataStart);
                    break;
                }
                pos += 8 + len + (len & 1);
            }
            if (dataStart < 0 || bits != 16 || channels < 1 || rate <= 0) return null;

            int frameBytes = channels * 2, samples = dataLen / frameBytes, window = Math.Max(1, rate / EnvelopeFps);
            float[] env = new float[samples / window + 1];
            float max = 1e-6f;
            for (int w = 0; w < env.Length; w++)
            {
                double sum = 0;
                int end = Math.Min(samples, (w + 1) * window), count = 0;
                for (int s = w * window; s < end; s++, count++)
                {
                    double v = BitConverter.ToInt16(wav, dataStart + s * frameBytes);
                    sum += v * v;
                }
                env[w] = count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
                if (env[w] > max) max = env[w];
            }
            for (int w = 0; w < env.Length; w++) env[w] /= max;
            return env;
        }

        static string Ascii(byte[] b, int at) { return System.Text.Encoding.ASCII.GetString(b, at, 4); }

        public void Dispose()
        {
            Stop();
            if (player != null) player.Dispose();
            if (tts != null) tts.Dispose();
        }
    }
}
