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

        public void OnLoaded()
        {
            try
            {
                Settings.Load();
                var harmony = new Harmony("djdomi.uboat.toolkit");
                harmony.PatchAll(typeof(ToolkitLoader).Assembly);
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
