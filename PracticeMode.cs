using BepInEx;
using BepInEx.Logging;
using System.Collections.Generic;
using HarmonyLib;
using Rhythm;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using FMOD.Studio;
using FMOD;
using System.Reflection.Emit;
using TMPro;
using System.Collections;
using FMODUnity;
using BepInEx.Configuration;

namespace PracticeMode
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInProcess("UNBEATABLE.exe")]
    public class PracticeMode : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.stefyfresh.PracticeMode";
        public const string PLUGIN_NAME = "Practice Mode";
        public const string PLUGIN_VERSION = "1.3.0";
        internal static new ManualLogSource Logger;
        public static readonly string settingsFilePath = "practice-mode-settings.txt";

        // Data
        public static int startTime;
        public static Dictionary<string, int> songOverrideInfos = [];
        public static EventInstance instance;

        // States
        public static bool practiceEnabled;
        public static bool hasUpdatedTime;
        // public static bool hasCorrectedTime;
        // public static bool isReady;

        // Config
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> offsetFixEnabled;
        public static bool doLogging = false;


        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");

            modEnabled = Config.Bind(
                "General",
                "EnablePractice",
                true,
                "Enables the ability to change where to start in a song using the information in the practice settings file."
            );

            offsetFixEnabled = Config.Bind(
                "General",
                "EnableOffsetFix",
                true,
                "Enables a fix for offset that can cause offset issues during practice mode.\nApplies to all songs regardless of practice mode, but mostly affects practice sessions."
            );

            var harmony = new Harmony(PLUGIN_GUID);
            harmony.PatchAll();

            ReadSongOverrides();
        }

        public static void ReadSongOverrides()
        {
            songOverrideInfos.Clear();
            if (!File.Exists(Path.Combine(Application.persistentDataPath, settingsFilePath)))
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, settingsFilePath), $"// Practice Mode Settings Format:{Environment.NewLine}// Song Name:Timestamp in ms{Environment.NewLine}// The names are case insensitive, but they must exactly match the song title.{Environment.NewLine}// All lines starting with // are ignored.{Environment.NewLine}{Environment.NewLine}//Example Song Title:12345{Environment.NewLine}");
            }

            string[] lines = Regex.Split(File.ReadAllText(Path.Combine(Application.persistentDataPath, settingsFilePath)), Environment.NewLine);

            bool saveChanges = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!(line == "") && !line.StartsWith("//"))
                {
                    // Valid line, so parse it
                    try
                    {
                        string[] strings = line.Split([':']);
                        string song = strings[0];
                        int startTime = int.Parse(strings[1].Trim());

                        songOverrideInfos.Add(song.ToUpper(), startTime);
                    }
                    catch (Exception)
                    {
                        lines[i] = $"//{lines[i]}  //[Practice Mode Settings Parser] invalid line! Auto-commented out.";
                        saveChanges = true;
                    }
                }
            }

            if (saveChanges) File.WriteAllLines(Path.Combine(Application.persistentDataPath, settingsFilePath), lines);
        }
    }



    [HarmonyPatch(typeof(RhythmController))]
    [HarmonyPatch(nameof(RhythmController.InitializeAndPlay))]
    internal class ControllerInitializePatch
    {
        static void Postfix(ref RhythmController __instance)
        {
            // Reset state
            PracticeMode.practiceEnabled = false;
            PracticeMode.hasUpdatedTime = false;
            // AudioSettingController.Init(__instance);

            // Exit if not enabled
            if (!PracticeMode.modEnabled.Value) return;

            PracticeMode.ReadSongOverrides();

            // Check current song
            if (PracticeMode.songOverrideInfos.TryGetValue(__instance.beatmap.metadata.title.ToUpper(), out int startTime))
            {
                PracticeMode.startTime = startTime;
                PracticeMode.practiceEnabled = true;
            }

            if (PracticeMode.practiceEnabled)
            {
                // Check if audio is valid
                if (GetSongDuration(__instance) == -1)
                {
                    PracticeMode.practiceEnabled = false;
                    return;
                }

                // Sanitize start time
                int desiredTime = PracticeMode.startTime;
                if (desiredTime < 0) desiredTime = 0;
                if (desiredTime > __instance.notes.Last().time) desiredTime = (int)__instance.notes.Last().time - 200;

                // Load timing points
                TimingPointInfo countdownTiming = null;
                List<TimingPointInfo> timings = new List<TimingPointInfo>(__instance.beatmap.timingPoints);
                timings.RemoveAll(t => !t.uninherited);

                int index = 0;

                // while index in range and current point is less than desired time and next point is also less than desired time, or current is the last point
                while (index < timings.Count && timings[index].time <= desiredTime && index != timings.Count - 1 && timings[index + 1].time <= desiredTime)
                {
                    index++;
                }
                countdownTiming = timings[index];

                // Set timing
                // desiredTime = when the first notes should reach the player
                // seekTime = time to start the audio, time to start the countdown
                int seekTime = desiredTime - 500 - Mathf.RoundToInt(8 * countdownTiming.beatLength);
                PracticeMode.startTime = seekTime;

                // Calculate additional delay for song to be on beat
                float measureProgress = Mathf.Repeat(seekTime - countdownTiming.time, countdownTiming.beatLength) / countdownTiming.beatLength;
                int additionalTime = Mathf.RoundToInt((1 - measureProgress) * countdownTiming.beatLength);
                if (measureProgress * countdownTiming.beatLength < 20) additionalTime = 0;


                // Get FMOD instance
                EventInstance instance = __instance.songTracker.instance;
                PracticeMode.instance = instance;
                instance.setPaused(true);
                // instance.setTimelinePosition(seekTime);


                // Countdown
                __instance.songTracker.AddCountdown(500 + 8f * countdownTiming.beatLength + additionalTime, desiredTime + additionalTime, countdownTiming.beatLength);
                __instance.song.countdownBeatLength = countdownTiming.beatLength;


                // Remove notes that are before the start time
                while (__instance.notes.Count > 0 && __instance.notes.Peek().time <= desiredTime + additionalTime + FileStorage.options.rhythmTrackerPositionOffset)
                {
                    __instance.notes.Dequeue();
                }

                // Process flips
                FlipInfo flipInfo = null;
                while (__instance.flips.Count > 0 && __instance.flips.Peek().time <= seekTime)
                {
                    flipInfo = __instance.flips.Dequeue();

                    if (flipInfo.toggleCenter) __instance.cameraIsCentered = !__instance.cameraIsCentered;
                    else __instance.player.side = __instance.player.side.GetOpposite();
                }

                if (flipInfo != null)
                {
                    if (__instance.cameraIsCentered) __instance.cameraObject.SetTargetPoint(__instance.centerCameraTargetPoint);
                    else if (__instance.player.side == Side.Right) __instance.cameraObject.SetTargetPoint(__instance.rightCameraTargetPoint);
                    else if (__instance.player.side == Side.Left) __instance.cameraObject.SetTargetPoint(__instance.leftCameraTargetPoint);

                    __instance.player.ChangeSide(__instance.player.side);

                    bool toggleCenter = flipInfo.toggleCenter;
                    __instance.indicatingFlip = false;
                }


                // Update the score so it actually means something useful
                List<NoteInfo> updatedNotes = __instance.notes.ToList();
                __instance.score.totalNoteCount = GetSignificantNoteCount(updatedNotes);
                __instance.score.totalDodgesCount = updatedNotes.Count(n => n.type == NoteType.Dodge);
                __instance.score.scoreWeight = RhythmConsts.MaxScoreWeight / __instance.score.GetMaxPossibleScore();
            }
            // AudioSettingController.Init(__instance);
        }

        private static int GetSongDuration(RhythmController rhythm)
        {
            uint songLengthFMOD;
            if (rhythm.parser.loadFromJeffBezos && JeffBezosController.rhythmProgression is ArcadeProgression arcadeProgression)
            {
                if (arcadeProgression.isCustomChart)
                {
                    // Custom song
                    songLengthFMOD = RhythmTracker.GetSongDuration(PlaySource.FromFile, arcadeProgression.customAudioPath);
                }
                else
                {
                    songLengthFMOD = RhythmTracker.GetSongDuration(PlaySource.FromTable, arcadeProgression.GetSongName());
                }

                if (songLengthFMOD > 0)
                {
                    // Song was properly loaded, use length from FMOD
                    return (int)songLengthFMOD;
                }
            }
            return -1;
        }

        private static int GetSignificantNoteCount(List<NoteInfo> noteInfos)
        {
            int count = 0;
            NoteInfo prevNoteInfo = null;
            foreach (NoteInfo checkNoteInfo in noteInfos)
            {
                if (checkNoteInfo.type != NoteType.Freestyle || prevNoteInfo == null || prevNoteInfo.type != NoteType.Freestyle || checkNoteInfo.side != prevNoteInfo.side)
                {
                    count++;
                }
                prevNoteInfo = checkNoteInfo;
            }
            return count;
        }
    }



    [HarmonyPatch(typeof(RhythmController))]
    [HarmonyPatch(nameof(RhythmController.FixedUpdate))]
    internal class ControllerUpdatePatch
    {
        // This actually sets the audio playback time
        static bool Prefix(ref RhythmController __instance)
        {
            if (PracticeMode.modEnabled.Value) AudioSettingController.FixedUpdate(__instance);
            return true;
        }
    }



    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.AddCountdown))]
    internal class AddCountdownPatch
    {
        // This actually sets the audio playback time
        static void Postfix(ref RhythmTracker __instance)
        {
            AudioSettingController.songTrackerPosition = __instance.currentPosition;
        }
    }



    [HarmonyPatch(typeof(HighScoreScreenArcade))]
    [HarmonyPatch(nameof(HighScoreScreenArcade.OnScoreScreenUpdated))]
    internal class OnScoreScreenUpdatedPatch
    {
        static bool Prefix(ref HighScoreScreenArcade __instance)
        {
            if (!PracticeMode.practiceEnabled) return true;

            // Show practice mode as a modifier
            GameObject practiceModifierGO = UnityEngine.Object.Instantiate(__instance.modifierPrefab, __instance.modifierPrefab.transform.parent);
            practiceModifierGO.SetActive(true);
            practiceModifierGO.GetComponentInChildren<TextMeshProUGUI>().text = "<cspace=0.2em>>" + "Practice Mode";
            return true;
        }
    }



    [HarmonyPatch(typeof(HighScoreList))]
    [HarmonyPatch(nameof(HighScoreList.IsScoreSaveable))]
    internal class DisableScoreSaving
    {
        static bool Prefix(ref bool __result)
        {
            if (PracticeMode.practiceEnabled)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }



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



    [HarmonyPatch(typeof(RhythmTracker))]
    [HarmonyPatch(nameof(RhythmTracker.Started), MethodType.Getter)]
    internal class RhythmTrackerStartedPatch
    {
        static bool Prefix(ref RhythmTracker __instance, ref bool __result)
        {
            if (!PracticeMode.offsetFixEnabled.Value) return true;

            __result = __instance.currentPosition >= 0f && __instance.GetSampleLoadState() == LOADING_STATE.LOADED && !__instance.isPreload && !__instance.isLoadingSamples;
            // if (PracticeMode.practiceEnabled) __result = PracticeMode.isReady;
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

            if (currentTimelinePos > 0 && !paused)
            {
                AudioSettingController.songTrackerPosition = Mathf.SmoothDamp(AudioSettingController.songTrackerPosition, currentTimelinePos, ref __instance.currentCorrectionVelocity, 0.30f);
            }

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
}