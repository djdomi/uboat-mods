using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.Resources;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fix 'Requested asset at path "Icons/Activators/Goods Depository" (or "Icons/Equipment/Fuel") doesn't exist.':
    // the game has no picture for some port depots, tanks and items, and logs a red error with a long stack
    // each time a storage window, banner or info box shows them. Now: picture quietly, else the folder's
    // "Unknown" picture, else the activator "Unknown" picture - no red error.
    [HarmonyPatch]
    internal static class IconFix
    {
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(IconFix), nameof(Get));
        private static readonly string[] TypeNames = {
            "UBOAT.Game.UI.ActivatorDetailsUI", "UBOAT.Game.UI.DeviceInfoUI", "UBOAT.Game.UI.Storage.StorageTabUI",
            "UBOAT.Game.UI.Storage.StorageWindow", "UBOAT.Game.UI.Storage.StorageUICount", "UBOAT.Game.UI.Storage.StorageSlotUI",
            "UBOAT.Game.UI.Banners.BaseActivatorBanner", "UBOAT.Game.UI.CharacterSelectionUI",
            "UBOAT.Game.UI.StoryPlayerCharacterUI", "UBOAT.Game.UI.StoryPlayerCharacterSlotUI", "UBOAT.Game.UI.EquipmentUpgradeItemUI", "UBOAT.Game.UI.SkillTokenUI", "UBOAT.Game.UI.Map.PlayerShipMarkerController", "UBOAT.Game.UI.Map.EntityMarkerUI", "UBOAT.Game.UI.Banners.CharacterActivatorBanner" };

        private static bool IsCall(object operand) => operand is MethodInfo m && m.Name == "RetrieveAsset"
            && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Sprite) && m.GetParameters().Length == 3;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            foreach (var n in TypeNames)
            {
                var t = UboatToolkit.Util.FindType(n);
                if (t == null) continue;
                var types = new List<Type> { t };
                types.AddRange(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
                foreach (var ty in types)
                    foreach (var m in AccessTools.GetDeclaredMethods(ty))
                    {
                        if (m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters) continue;
                        try { if (PatchProcessor.ReadMethodBody(m).Any(i => IsCall(i.Value))) list.Add(m); } catch { }
                    }
            }
            return list;
        }

        public static Resource<Sprite> Get(ResourceManager rm, string path, object owner, bool logErrors)
        {
            if (!Settings.B("FixMissingIcons") || path == null) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            if (rm.AssetExists(path)) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            int i = path.LastIndexOf('/');
            if (i > 0) { var u = path.Substring(0, i) + "/Unknown"; if (rm.AssetExists(u)) return rm.RetrieveAsset<Sprite>(u, owner, false); }
            return rm.RetrieveAsset<Sprite>("Icons/Activators/Unknown", owner, false);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && IsCall(c.operand)) { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix 'GameObject is already being activated or deactivated.' when coming back from the character
    // screen: the main camera is switched off, and while Unity switches it off the free camera moves it
    // to a new parent (not allowed then). The move is now done one frame later instead.
    [HarmonyLib.HarmonyPatch]
    internal static class CameraParentFix
    {
        private static int inDisable;
        private static UnityEngine.Transform pendingT, pendingParent;
        private static bool pendingWorld;

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.MainCamera), "OnDisable"), HarmonyLib.HarmonyPrefix]
        private static void Enter() => inDisable++;

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.MainCamera), "OnDisable"), HarmonyLib.HarmonyFinalizer]
        private static System.Exception Leave(System.Exception __exception) { if (inDisable > 0) inDisable--; return __exception; }

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.FreeFlyCameraController), "StopController"), HarmonyLib.HarmonyTranspiler]
        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> T(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var setParent = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Transform), "SetParent", new[] { typeof(UnityEngine.Transform), typeof(bool) });
            var ours = HarmonyLib.AccessTools.Method(typeof(CameraParentFix), nameof(SetParent));
            foreach (var c in code)
            {
                if ((c.opcode == System.Reflection.Emit.OpCodes.Callvirt || c.opcode == System.Reflection.Emit.OpCodes.Call) && c.operand as System.Reflection.MethodInfo == setParent)
                { c.opcode = System.Reflection.Emit.OpCodes.Call; c.operand = ours; }
                yield return c;
            }
        }

        public static void SetParent(UnityEngine.Transform t, UnityEngine.Transform parent, bool worldStays)
        {
            if (inDisable > 0 && Settings.B("FixCameraParent")) { pendingT = t; pendingParent = parent; pendingWorld = worldStays; return; }
            t.SetParent(parent, worldStays);
        }

        internal static void RunPending()
        {
            if (pendingT == null) return;
            var t = pendingT; pendingT = null;
            try { if (t != null && pendingParent != null) t.SetParent(pendingParent, pendingWorld); } catch (System.Exception e) { Util.Log("Kamera-Fix: " + e.Message); }
        }
    }

    public class CameraParentFixModule : ModuleBase
    {
        public override void Frame() => CameraParentFix.RunPending();
    }
}

namespace UboatToolkit.Modules
{
    // 0.3.93 (DoMe: debug console still pops up about once a minute): many other UI parts (contact list, cursor
    // action icons, periscope overlays) ask for pictures the game does not ship. Each one wrote a red
    // "Requested asset at path ... doesn't exist." error, which opens the console. A missing picture is harmless:
    // for paths under Icons/ and Images/ the error is now only counted, not written.
    [HarmonyPatch]
    internal static class IconFix_MissingLog
    {
        internal static int Hidden;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Mono shares one method body for all class types (Sprite, Texture2D, ...): patching the Sprite version covers them all.
            var list = new List<MethodBase>();
            foreach (var t in new[] { typeof(DWS.Common.Resources.ResourceBase<Sprite>) })
            {
                var m = AccessTools.DeclaredMethod(t, "LogAssetMissing");
                if (m != null && !list.Contains(m)) list.Add(m);
            }
            var ab = UboatToolkit.Util.FindType("DWS.Common.Resources.AssetBundleContainedResource`1");
            if (ab != null)
                foreach (var a in new[] { typeof(Sprite) })
                {
                    try { var m = AccessTools.DeclaredMethod(ab.MakeGenericType(a), "LogAssetMissing"); if (m != null && !list.Contains(m)) list.Add(m); } catch { }
                }
            return list;
        }

        private static bool Prefix(object __instance)
        {
            try
            {
                if (!Settings.B("FixMissingIcons")) return true;
                var p = Traverse.Create(__instance).Property("Path").GetValue<string>();
                if (p == null || !(p.StartsWith("Icons/") || p.StartsWith("Images/"))) return true;
                if (Hidden++ < 20) Util.Log("Fehlendes Bild ohne roten Fehler übersprungen: " + p);
                return false;
            }
            catch { return true; }
        }
    }
}
