using UnityEngine;

namespace Eights;

internal static class BombBeepAudio
{
    private static AudioClip? _clip;

    internal static AudioClip GetClip()
    {
        if (_clip != null)
        {
            return _clip;
        }

        const int sampleRate = 44100;
        const float duration = 0.12f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            float progress = index / (float)sampleCount;
            float envelope = Mathf.Sin(progress * Mathf.PI);
            samples[index] = Mathf.Sin(2f * Mathf.PI * 880f * index / sampleRate)
                * envelope * 0.35f;
        }

        _clip = AudioClip.Create("SndtatBombBeep", sampleCount,
            1, sampleRate, false);
        _clip.SetData(samples, 0);
        return _clip;
    }

    internal static void ConfigureSource(AudioSource source, float volume)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2.5f;
        source.maxDistance = 55f;
        source.dopplerLevel = 0f;
        source.volume = Mathf.Clamp01(volume);
    }

    internal static void PlayAtPosition(Vector3 position, float volume)
    {
        AudioClip clip = GetClip();
        GameObject beepObject = new("EightsBombBeep");
        beepObject.transform.position = position;
        AudioSource source = beepObject.AddComponent<AudioSource>();
        ConfigureSource(source, volume);
        source.PlayOneShot(clip);
        Object.Destroy(beepObject, clip.length + 0.1f);
    }
}