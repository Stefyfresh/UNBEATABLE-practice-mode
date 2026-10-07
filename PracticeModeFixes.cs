using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using Rhythm;
using UnityEngine;

namespace PracticeMode
{
    // -------------------- Fixes to audio file timing --------------------

    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.HandleCreateProgrammerSound))]
    internal class SoundCreationTranspiler
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Add MODE.ACCURATETIME to the Mode parameter when creating the programmer sound

            bool found = false;
            foreach (var instruction in instructions)
            {
                // transpiler nonsense
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int num && num == 66050) // 66050 = MODE.LOOP_NORMAL | MODE.CREATECOMPRESSEDSAMPLE | MODE.NONBLOCKING
                {
                    found = true;
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 82434); // 82434 = MODE.LOOP_NORMAL | MODE.CREATECOMPRESSEDSAMPLE | MODE.NONBLOCKING | MODE.ACCURATETIME
                }
                else if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int num2 && num2 == 65794) // 65794 = MODE.LOOP_NORMAL | MODE.CREATESAMPLE | MODE.NONBLOCKING
                {
                    found = true;
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 82178); // 82178 = MODE.LOOP_NORMAL | MODE.CREATESAMPLE | MODE.NONBLOCKING | MODE.ACCURATETIME
                }
                else
                {
                    yield return instruction;
                }
            }

            if (found)
            {
                PracticeMode.Logger.LogDebug("Successfully patched sound mode info.");
            }
            else
            {
                PracticeMode.Logger.LogError("Could not find sound mode info to patch!");
            }
        }
    }



    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.GetSongDuration))]
    internal class GetSongDurationTranspiler
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Add MODE.ACCURATETIME to the Mode parameter when getting song duration

            bool found = false;
            foreach (var instruction in instructions)
            {
                // transpiler nonsense
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int num && num == 8448) // 8448 = MODE.CREATESAMPLE | MODE.OPENONLY
                {
                    found = true;
                    instruction.operand = 24832; // 24832 = MODE.CREATESAMPLE | MODE.OPENONLY | MODE.ACCURATETIME
                    yield return instruction;
                }
                else
                {
                    yield return instruction;
                }
            }

            if (found)
            {
                PracticeMode.Logger.LogDebug("Successfully patched sound mode info for duration.");
            }
            else
            {
                PracticeMode.Logger.LogError("Could not find sound mode info for duration to patch!");
            }
        }
    }



    // -------------------- Fixes to offset --------------------

    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.Started), MethodType.Getter)]
    internal class RhythmTrackerStartedPatch
    {
        static bool Prefix(ref RhythmTracker __instance, ref bool __result)
        {
            if (!PracticeMode.offsetFixEnabled.Value) return true;

            __result = __instance.currentPosition >= 0f && __instance.GetSampleLoadState() == LOADING_STATE.LOADED && !__instance.isPreload && !__instance.isLoadingSamples;
            return false;
        }
    }



    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.FixedUpdate))]
    [HarmonyPriority(Priority.First)]
    internal class BetterOffset
    {
        static bool Prefix(ref RhythmTracker __instance)
        {
            if (!PracticeMode.offsetFixEnabled.Value) return true;


            // normal code
            if (__instance.pausePosition != -1f) __instance.pitch = 1f - Mathf.Min(1f, (__instance.currentPosition - __instance.pausePosition) / __instance.pauseLength);
            else __instance.pitch = 1f;



            // Get paused state
            __instance.instance.getPaused(out bool paused);

            // Get actual song position
            float currentTimelinePos = AudioSettingController.GetCustomPosition(__instance);

            // Get corrected deltaTime
            float correctionFactor = (Time.timeScale == 0) ? 0 : __instance.TimeScale / Time.timeScale;

            // Update position
            // Time.unscaledDeltaTime * __instance.TimeScale instead of Time.deltaTime to fix the end slowdown
            AudioSettingController.songTrackerPosition += 1000 * Time.deltaTime * correctionFactor * __instance.pitch;

            // Update position directly when error is high
            if (currentTimelinePos > 0 && Math.Abs(currentTimelinePos - AudioSettingController.songTrackerPosition) >= 125 && !paused)
            {
                AudioSettingController.songTrackerPosition = currentTimelinePos;
            }

            if (currentTimelinePos > 0 && !paused) AudioSettingController.songTrackerPosition = Mathf.SmoothDamp(AudioSettingController.songTrackerPosition, currentTimelinePos, ref __instance.currentCorrectionVelocity, 0.30f);


            // Perform rounding
            __instance.currentPosition = Mathf.RoundToInt(AudioSettingController.songTrackerPosition);



            // normal code
            if (__instance.pausePosition != -1f) __instance.currentPosition = Mathf.Min(__instance.currentPosition, __instance.pausePosition + __instance.pauseLength);
            if (__instance.countdown != null && __instance.countdownBeat >= 0 && Mathf.CeilToInt((__instance.countdown.length - __instance.CountdownPosition) / __instance.countdown.beatLength) == __instance.countdownBeat)
            {
                __instance.countdownBeat--;
                switch (__instance.countdownBeat)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 5:
                    case 7:
                        RuntimeManager.PlayOneShot(__instance.tickEvent, default(Vector3));
                        break;
                    case 4:
                    case 6:
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }
    }



    // -------------------- Fixes to playback stage videos --------------------

    [HarmonyPatch(typeof(RhythmMVPlayer))]
    [HarmonyPatch("OnPrepareCompleted")]
    internal class MVPlayerOnPrepareCompletedPatch
    {
        static bool Prefix(ref RhythmMVPlayer __instance)
        {
            if (PracticeMode.practiceEnabled && __instance.controller)
            {
                __instance.player.time = (double)Mathf.Max((__instance.controller.songTracker.Position - __instance._song.VideoStartTime) / 1000f, 0f);
                __instance._isPlaying = true;
                __instance._isSeeking = true;
                __instance._canPlay = true;
                return false;
            }
            return true;
        }
    }



    [HarmonyPatch(typeof(RhythmMVPlayer))]
    [HarmonyPatch("OnSeekCompleted")]
    internal class MVPlayerOnSeekCompletedPatch
    {
        static bool Prefix(ref RhythmMVPlayer __instance)
        {
            if (PracticeMode.practiceEnabled && __instance.controller)
            {
                __instance.player.Play();
                return false;
            }
            return true;
        }
    }
}