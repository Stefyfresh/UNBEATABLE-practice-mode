using FMOD;
using FMOD.Studio;
using Rhythm;
using UnityEngine;

namespace PracticeMode
{
    public static class AudioSettingController
    {
        // public static float channelToChartSyncOffset = 0;
        public static readonly int channelToChartSyncOffset = 5;

        public static float songTrackerPosition;
        // public static readonly float syncOffsetMultiplier = Time.fixedDeltaTime / 0.8f;
        // public static readonly float maxSyncRange = 15;

        public static Channel channel;


        public static void Init(RhythmController controller)
        {
            // channelToChartSyncOffset = 0;
            songTrackerPosition = controller.songTracker.currentPosition;
        }

        public static float GetCustomPosition(RhythmTracker songTracker)
        {
            EventInstance instance = songTracker.instance;

            if (!instance.isValid()) return 0;

            instance.getTimelinePosition(out int timelinePosition);
            if (!songTracker.Started) return timelinePosition;

            if (instance.getChannelGroup(out ChannelGroup cg) == RESULT.OK)
            {
                if (cg.getGroup(0, out ChannelGroup subGroup) == RESULT.OK)
                {
                    if (subGroup.getNumChannels(out int numChan) == RESULT.OK && numChan > 0)
                    {
                        if (subGroup.getChannel(0, out channel) == RESULT.OK)
                        {
                            channel.getPosition(out uint chanPosition, TIMEUNIT.MS);
                            // channelToChartSyncOffset = Mathf.Clamp(channelToChartSyncOffset + (chanPosition - songTrackerPosition) * syncOffsetMultiplier, -maxSyncRange, maxSyncRange);
                            // return (int)chanPosition + Mathf.RoundToInt(channelToChartSyncOffset);
                            return (int)chanPosition + channelToChartSyncOffset;

                        }
                    }
                }
            }

            // Fallback if the channel is broken
            return timelinePosition;
        }


        public static void FixedUpdate(RhythmController controller)
        {
            if ((PracticeMode.hasUpdatedTime || !PracticeMode.practiceEnabled) && !PracticeMode.doLogging) return;

            EventInstance instance = controller.songTracker.instance;
            int startTime = PracticeMode.startTime;

            if (instance.isValid())
            {
                instance.getTimelinePosition(out int position);

                if (!controller.songTracker.Started) return;

                uint chanPosition = 0;
                if (instance.getChannelGroup(out ChannelGroup cg) == RESULT.OK)
                {
                    if (cg.getGroup(0, out ChannelGroup subGroup) == RESULT.OK)
                    {
                        if (subGroup.getNumChannels(out int numChan) == RESULT.OK && numChan > 0)
                        {
                            if (subGroup.getChannel(0, out channel) == RESULT.OK)
                            {
                                channel.getPosition(out chanPosition, TIMEUNIT.MS);
                                // RhythmController.Instance.StartCoroutine(SetPracticeAndFixOffset(channel, instance, startTime));

                                if (!PracticeMode.hasUpdatedTime && PracticeMode.practiceEnabled)
                                {
                                    channel.setPosition((uint)startTime, TIMEUNIT.MS);
                                    instance.setTimelinePosition(startTime);

                                    PracticeMode.hasUpdatedTime = true;

                                    instance.getTimelinePosition(out position);
                                    channel.getPosition(out chanPosition, TIMEUNIT.MS);
                                    PracticeMode.Logger.LogInfo($"Updated time! | {startTime}");
                                }
                            }
                        }
                    }
                    // if (PracticeMode.doLogging) PracticeMode.Logger.LogInfo($"Timeline: {position} | Channel: {chanPosition} | D: {position - chanPosition} | Tracker: {controller.songTracker.TimelinePosition} | D Tracker: {controller.songTracker.TimelinePosition - chanPosition} | Sync: {channelToChartSyncOffset:0.0}");
                    if (PracticeMode.doLogging) PracticeMode.Logger.LogInfo($"Timeline: {position} | Channel: {chanPosition} | D: {position - chanPosition} | Tracker: {controller.songTracker.TimelinePosition} | D Tracker: {controller.songTracker.TimelinePosition - chanPosition}");
                }
            }
        }

        // #pragma warning disable Harmony003 // Harmony non-ref patch parameters modified

        //         public static IEnumerator SetPracticeAndFixOffset(Channel channel, EventInstance instance, int startTime)
        //         {
        //             if (PracticeMode.hasUpdatedTime) yield break;

        //             channel.setPosition((uint)startTime, TIMEUNIT.MS);
        //             instance.setTimelinePosition(startTime);
        //             PracticeMode.hasUpdatedTime = true;
        //             PracticeMode.hasCorrectedTime = false;
        //             PracticeMode.Logger.LogInfo($"Updated time! | {startTime}");



        //             int frames = 20;
        //             int sum = 0;
        //             uint chanPosition;
        //             int position;
        //             while (frames > 0)
        //             {
        //                 channel.getPosition(out chanPosition, TIMEUNIT.MS);
        //                 instance.getTimelinePosition(out position);
        //                 if (position > 125 && chanPosition > 125)
        //                 {
        //                     sum += (int)chanPosition - position;

        //                     frames--;
        //                     yield return new WaitForFixedUpdate();

        //                 }
        //             }

        //             instance.getTimelinePosition(out int position2);

        //             instance.setTimelinePosition(position2 + sum / 20);
        //             PracticeMode.hasCorrectedTime = true;
        //             PracticeMode.Logger.LogInfo($"Corrected time! | {position2} | {sum / 20}");
        //         }
    }
}