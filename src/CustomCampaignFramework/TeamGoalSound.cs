using System;
using System.Collections.Generic;
using System.Globalization;
using CampaignCore;
using Events;
using HarmonyLib;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

namespace EndlessMode;

internal static class TeamGoalSound
{
    private sealed class Sound { internal string Data; internal byte[] Bytes; internal FMOD.Sound Handle; internal bool Loaded; internal float Volume; }
    private static readonly Dictionary<string, Sound> Sounds = new();
    private static FMOD.Channel channel;
    private static bool playing;
    private static float authoredVolume = 1;
    internal static void Register(string id, string encoded, string volume)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Sounds.TryGetValue(id, out var old) && old.Data == encoded)
        { old.Volume = Volume(volume); return; }
        if (old != null) { if (old.Loaded) { Stop(); old.Handle.release(); } Sounds.Remove(id); }
        if (string.IsNullOrWhiteSpace(encoded)) return;
        try
        {
            if (encoded.Length > GoalSoundWave.MaxBytes * 4 / 3 + 4) throw new ArgumentException("Goal sound is too large.");
            byte[] bytes = Convert.FromBase64String(encoded); GoalSoundWave.Decode(bytes);
            Sounds[id] = new Sound { Data = encoded, Bytes = bytes, Volume = Volume(volume) };
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[GoalSound] Cannot load " + id + ": " + ex.Message); }
    }
    private static float Volume(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result) ? Math.Clamp(result / 100f, 0, 1) : 1;
    private static void Require(FMOD.RESULT result)
    { if (result != FMOD.RESULT.OK) throw new InvalidOperationException("FMOD goal audio: " + result); }
    internal static void Stop() { if (playing) channel.stop(); playing = false; }
    internal static void ReleaseSamples()
    {
        Stop();
        foreach (var sound in Sounds.Values)
            if (sound.Loaded) { sound.Handle.release(); sound.Loaded = false; }
    }
    internal static void RefreshVolume(Settings.SystemSettings settings)
    {
        if (playing && settings != null)
            channel.setVolume(authoredVolume * Math.Clamp(settings.master, 0, 1) * Math.Clamp(settings.sfx, 0, 1));
    }
    internal static bool Play(SoundManager manager, GoalScoredArg goal)
    {
        if (goal == null || goal.goalType != NHL94.GoalType.GOOD) return false;
        string id = goal.scorer?.Team?._teamData?.id;
        // A new goal ends any preceding custom celebration, including when
        // the other team uses its normal sound.
        Stop();
        if (id == null || !Sounds.TryGetValue(id, out var sound)) return false;
        if (!sound.Loaded)
        {
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "t2t-goal-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                System.IO.File.WriteAllBytes(file, sound.Bytes);
                // Synchronous sample load: FMOD owns the samples before the
                // temporary file is removed. No Unity AudioListener is needed.
                Require(RuntimeManager.CoreSystem.createSound(file, FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF | FMOD.MODE._2D, out sound.Handle));
                sound.Loaded = true;
            }
            finally { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); }
        }
        Require(RuntimeManager.CoreSystem.playSound(sound.Handle, default, true, out channel));
        playing = true;
        try
        {
            float mix = 1;
            var settings = UnityEngine.Object.FindObjectOfType<Settings.SystemSettings>();
            if (settings != null) mix = Math.Clamp(settings.master, 0, 1) * Math.Clamp(settings.sfx, 0, 1);
            authoredVolume = sound.Volume;
            Require(channel.setVolume(authoredVolume * mix)); Require(channel.setPaused(false));
            return true;
        }
        catch { Stop(); throw; }
    }
}

[HarmonyPatch(typeof(SoundManager), "OnGoalScored")]
internal static class TeamGoalSoundScope
{
    [ThreadStatic] internal static SoundManager Active;
    [HarmonyPrefix] private static void Prefix(SoundManager __instance, GoalScoredArg __0, out SoundManager __state)
    {
        __state = Active; Active = null;
        try { if (TeamGoalSound.Play(__instance, __0)) Active = __instance; }
        catch (Exception ex) { Plugin.Log.LogWarning("[GoalSound] Playback failed: " + ex.Message); }
    }
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, SoundManager __state)
    { Active = __state; return __exception; }
}

// Keep native crowd/music parameter updates, but silence the goal event before
// native code starts it. Restrict this to this handler and this exact event GUID.
[HarmonyPatch(typeof(RuntimeManager), nameof(RuntimeManager.CreateInstance), typeof(EventReference))]
internal static class MuteReplacedGoalEvent
{
    [HarmonyPostfix] private static void Postfix(EventReference __0, ref EventInstance __result)
    {
        var manager = TeamGoalSoundScope.Active;
        if (manager != null && __0.Guid.Equals(manager.goalScoredEvent.Guid))
        {
            var result = __result.setVolume(0);
            if (result != FMOD.RESULT.OK) { TeamGoalSound.Stop(); Plugin.Log.LogError("[GoalSound] Native goal mute failed; using native sound: " + result); }
        }
    }
}

[HarmonyPatch(typeof(SoundManager), "OnDestroy")]
internal static class ReleaseGoalAudio
{
    [HarmonyPrefix] private static void Prefix() => TeamGoalSound.ReleaseSamples();
}

[HarmonyPatch]
internal static class GoalAudioVolume
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Settings.SystemSettings), "SetMasterVolume");
        yield return AccessTools.Method(typeof(Settings.SystemSettings), "SetSfxVolume");
    }
    [HarmonyPostfix] private static void Postfix(Settings.SystemSettings __instance) => TeamGoalSound.RefreshVolume(__instance);
}
