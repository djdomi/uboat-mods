using HarmonyLib;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Tasks;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Access to game singletons that are only injected into game classes (read via their static fields).
    internal static class Game
    {
        private static TimeCompressionController tcc;
        private static SandboxPlayerWolfpack wolfpack;
        private static SavesManager saves;
        private static BackgroundTasksManager tasks;

        public static TimeCompressionController TimeCompression =>
            tcc ?? (tcc = Traverse.Create(typeof(DisallowTimeCompressionDuringAircraftAttack)).Field("timeCompressionController").GetValue<TimeCompressionController>());

        public static SandboxPlayerWolfpack Wolfpack =>
            wolfpack ?? (wolfpack = Traverse.Create(typeof(UBOAT.Game.UI.Notifications.ShallowAreaNotification)).Field("wolfpack").GetValue<SandboxPlayerWolfpack>());

        public static SavesManager Saves =>
            saves ?? (saves = Traverse.Create(typeof(LeakBlockingBySailors)).Field("savesManager").GetValue<SavesManager>());

        public static BackgroundTasksManager Tasks =>
            tasks ?? (tasks = Traverse.Create(typeof(ExpandedSchedulesController)).Field("backgroundTasksManager").GetValue<BackgroundTasksManager>());

        public static void StopTimeCompression(string why)
        {
            var t = TimeCompression;
            if (t == null || !t.TimeCompression) return;
            t.TimeCompression = false;
            ToolkitRunner.ShowNotice(why);
        }

        public static bool CanSave()
        {
            var s = Saves;
            return s != null && !s.IsLoading && !s.HasSaveBlockadesExcept(null, out _);
        }

        public static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        public static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }
}
