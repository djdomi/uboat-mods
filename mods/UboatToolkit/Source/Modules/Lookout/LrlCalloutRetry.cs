// Ausguck (Teil des UBOAT Toolkit): LrlCalloutRetry. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlCalloutRetry
    {
        private sealed class Entry
        {
            public SandboxGroup Observator;
            public GroupObservation Observation;
            public object ContactType;
            public string Line;
            public float FirstDrop;
            public float NextAttempt;
            public int Attempts;
        }

        internal static readonly MethodInfo SayReport =
            AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");

        internal static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly List<Entry> Queue = new List<Entry>();

        internal static void Clear() { Queue.Clear(); }

        internal static SandboxGroup CurrentObservator;
        internal static GroupObservation CurrentObservation;

        internal static bool Retrying;
        internal static bool LastRetryVoiced;

        private static bool _warned;

        internal static bool Active
        {
            get { return LrlSettings.Enabled && LrlSettings.RetryDroppedCallouts; }
        }

        internal static void Enqueue(string line)
        {
            if (!Active || CurrentObservation == null || CurrentObservator == null)
                return;

            for (var i = 0; i < Queue.Count; i++)
                if (ReferenceEquals(Queue[i].Observation, CurrentObservation))
                    return;

            object type = null;
            try
            {
                if (PerceivedField != null)
                    type = PerceivedField.GetValue(CurrentObservation);
            }
            catch (Exception)
            {
                type = null;
            }

            var now = Time.unscaledTime;
            Queue.Add(new Entry
            {
                Observator = CurrentObservator,
                Observation = CurrentObservation,
                ContactType = type,
                Line = line,
                FirstDrop = now,
                NextAttempt = now + Mathf.Max(1f, LrlSettings.RetryIntervalSeconds),
                Attempts = 0
            });

            LrlLog.Info("callout queued for retry: \"" + line + "\" - re-attempting every " +
                        LrlSettings.RetryIntervalSeconds.ToString("0") + " s for up to " +
                        LrlSettings.RetryWindowSeconds.ToString("0") + " s");
        }

        internal static void Tick(float now)
        {
            if (Queue.Count == 0)
                return;

            for (var i = Queue.Count - 1; i >= 0; i--)
            {
                var e = Queue[i];
                if (now < e.NextAttempt)
                    continue;

                if (!StillValid(e))
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout retry abandoned - contact no longer held: \"" + e.Line + "\"");
                    continue;
                }

                if (now - e.FirstDrop > LrlSettings.RetryWindowSeconds)
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout retry gave up after " + e.Attempts + " attempts / " +
                                (now - e.FirstDrop).ToString("0") + " s: \"" + e.Line + "\"");
                    continue;
                }

                e.Attempts++;
                e.NextAttempt = now + Mathf.Max(1f, LrlSettings.RetryIntervalSeconds);

                if (Attempt(e))
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout VOICED on retry " + e.Attempts + ", " +
                                (now - e.FirstDrop).ToString("0") + " s after it was dropped: \"" + e.Line + "\"");
                }
            }
        }

        private static bool StillValid(Entry e)
        {
            if (e.Observator == null || e.Observation == null)
                return false;
            if (e.Observation.IsLost)
                return false;

            var group = e.Observation.Group;
            if (group == null)
                return false;

            return ReferenceEquals(e.Observator.GetObservation(group), e.Observation);
        }

        private static bool Attempt(Entry e)
        {
            object original = null;
            var swapped = false;

            Retrying = true;
            LastRetryVoiced = false;

            try
            {
                if (SayReport == null)
                    return true;   // cannot retry at all - drop the entry

                if (e.ContactType != null && PerceivedField != null)
                {
                    original = PerceivedField.GetValue(e.Observation);
                    PerceivedField.SetValue(e.Observation, e.ContactType);
                    swapped = true;
                }

                SayReport.Invoke(null, new object[] { e.Observator, e.Observation });
                return LastRetryVoiced;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("callout retry failed - entry dropped", ex);
                }
                return true;
            }
            finally
            {
                Retrying = false;
                if (swapped)
                {
                    try { PerceivedField.SetValue(e.Observation, original); }
                    catch (Exception) { }
                }
            }
        }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport")]
    internal static class LrlCalloutContextPatch
    {
        private static void Prefix(SandboxGroup observator, GroupObservation observation)
        {
            LrlCalloutRetry.CurrentObservator = observator;
            LrlCalloutRetry.CurrentObservation = observation;
        }

        private static void Finalizer()
        {
            LrlCalloutRetry.CurrentObservator = null;
            LrlCalloutRetry.CurrentObservation = null;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "DetectionReportInternal")]
    internal static class LrlCalloutRetryHook
    {
        private static void Postfix(string statementId1, bool __result)
        {
            if (statementId1 == null || !statementId1.StartsWith("Group Contact"))
                return;

            if (LrlCalloutRetry.Retrying)
            {
                if (__result)
                    LrlCalloutRetry.LastRetryVoiced = true;
                return;
            }

            if (!__result)
                LrlCalloutRetry.Enqueue(statementId1);
        }
    }
}
