using System;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game;
using UBOAT.Game.Core.Serialization;

namespace UboatToolkit
{
    // Entry point: the game calls OnLoaded() once when the mod is loaded.
    [NonSerializedInGameState]
    public class ToolkitLoader : IUserMod
    {
        public const string Tag = "[UboatToolkit]";
        public static Harmony Harmony;

        public void OnLoaded()
        {
            try
            {
                Settings.Load();
                Harmony = new Harmony("djdomi.uboat.toolkit");
                Harmony.PatchAll(typeof(ToolkitLoader).Assembly);
                var go = new GameObject(Tag);
                go.AddComponent<ToolkitRunner>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                Debug.Log(Tag + " loaded.");
            }
            catch (Exception e)
            {
                Debug.Log(Tag + " failed to load.");
                Debug.LogException(e);
            }
        }
    }
}
