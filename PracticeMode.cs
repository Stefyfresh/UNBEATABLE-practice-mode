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

namespace PracticeMode
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInProcess("UNBEATABLE.exe")]
    public class PracticeMode : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "net.stefyfresh.PracticeMode";
        public const string PLUGIN_NAME = "Stefyfresh Practice Mode";
        public const string PLUGIN_VERSION = "1.0.0";
        internal static new ManualLogSource Logger;
        public static bool practiceEnabled;
        public static int startTime;
        public static Dictionary<string, int> songOverrideInfos = new Dictionary<string, int>();
        public static EventInstance instance;
        public static bool hasUpdatedTime;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");
            var harmony = new Harmony(PLUGIN_GUID);
            harmony.PatchAll();
            ReadSongOverrides();
        }

        public static void ReadSongOverrides()
        {
            songOverrideInfos.Clear();
            if (!File.Exists(Application.persistentDataPath + "/practice-mode-settings.txt"))
            {
                File.WriteAllText(Application.persistentDataPath + "/practice-mode-settings.txt", $"// Practice Mode Settings Format:{Environment.NewLine}// Song Name:Timestamp in ms{Environment.NewLine}// The names are case insensitive, but they must exactly match the song title.{Environment.NewLine}// All lines starting with // are ignored.{Environment.NewLine}{Environment.NewLine}//Example Song Title:12345{Environment.NewLine}");
            }

            string[] lines = Regex.Split(File.ReadAllText(Application.persistentDataPath + "/practice-mode-settings.txt"), Environment.NewLine);

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

            if (saveChanges) File.WriteAllLines(Application.persistentDataPath + "/practice-mode-settings.txt", lines);
        }
    }



    [HarmonyPatch(typeof(RhythmController))]
    [HarmonyPatch("InitializeAndPlay")]
    internal class ControllerInitializePatch
    {
        static void Postfix(ref RhythmController __instance)
        {
            PracticeMode.practiceEnabled = false;
            PracticeMode.hasUpdatedTime = false;
            PracticeMode.ReadSongOverrides();

            int startTime;
            if (PracticeMode.songOverrideInfos.TryGetValue(__instance.beatmap.metadata.title.ToUpper(), out startTime))
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
                timings.RemoveAll((TimingPointInfo t) => !t.uninherited);

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
                EventInstance instance = (EventInstance)Traverse.Create(__instance.songTracker).Field("instance").GetValue();
                PracticeMode.instance = instance;
                instance.setTimelinePosition(seekTime);


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
                Traverse traverse = Traverse.Create(__instance.score);
                traverse.Field("totalNoteCount").SetValue(GetSignificantNoteCount(updatedNotes));
                traverse.Field("totalDodgesCount").SetValue(updatedNotes.Count(n => n.type == NoteType.Dodge));
                traverse.Field("scoreWeight").SetValue((float)RhythmConsts.MaxScoreWeight / __instance.score.GetMaxPossibleScore());
            }
            return;
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
    [HarmonyPatch("Update")]
    internal class ControllerUpdatePatch
    {
        static bool Prefix()
        {
            if (PracticeMode.hasUpdatedTime || !PracticeMode.practiceEnabled) return true;

            EventInstance instance = PracticeMode.instance;
            int startTime = PracticeMode.startTime;
            if (instance.isValid())
            {
                if (instance.getChannelGroup(out ChannelGroup cg) == RESULT.OK)
                {
                    cg.getNumGroups(out _);
                    cg.getGroup(0, out ChannelGroup subGroup);
                    subGroup.getNumChannels(out int numChan);
                    if (numChan > 0)
                    {
                        subGroup.getChannel(0, out Channel chan);
                        chan.setPosition((uint)startTime, TIMEUNIT.MS);
                        instance.setTimelinePosition(startTime);
                        PracticeMode.hasUpdatedTime = true;
                    }
                }
            }
            return true;
        }
    }



    [HarmonyPatch(typeof(HighScoreList))]
    [HarmonyPatch("IsScoreSaveable")]
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
}