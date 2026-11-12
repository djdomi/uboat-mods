using HarmonyLib;
using ScopeItem = UBOAT.Game.Scene.Items.Periscope;
using System.Collections.Generic;
using System.Reflection;
using System;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.UI.Periscope;
using UBOAT.Game.UI;
using UnityEngine.UI;
using UnityEngine;

namespace UboatToolkit.Modules.ScopeUi
{
    // Periscope screen fixes: view mode and zoom are remembered per periscope and re-applied when it
    // opens again (dropdowns show the real state), the station button lights for the observation
    // periscope and takes you to the periscope you used last, colour-filter mismatch is logged.
    internal static class PuiSettings
    {
        public static bool Enabled => Settings.B("ScopeRemember");
        public static bool RememberViewAndZoom => Settings.B("ScopeRememberView");
        public static bool StationButtonLastPeriscope => Settings.B("ScopeStationLast");
    }

    internal static class PuiMemory
    {
        internal const string AttackKind = "Attack Periscope";
        internal const string ObservationKind = "Observation Periscope";
        private static readonly Dictionary<string, int> ViewModes = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Zooms = new Dictionary<string, int>(StringComparer.Ordinal);
        private static string _lastKind;
        internal static string LastKind { get { return _lastKind; } }
        internal static int ViewModeOf(bool fullScreen, bool closer)
        {
            if (fullScreen) return 2;
            return closer ? 1 : 0;
        }
        internal static int ClampZoom(int index, int presetCount)
        {
            if (presetCount <= 0) return -1;
            if (index < 0) return 0;
            return index >= presetCount ? presetCount - 1 : index;
        }
        internal static string KindOf(string typeName)
        {
            if (typeName == null) return null;
            if (typeName.StartsWith(AttackKind, StringComparison.Ordinal)) return AttackKind;
            if (typeName.StartsWith(ObservationKind, StringComparison.Ordinal)) return ObservationKind;
            return null;
        }
        internal static void RecordViewMode(string typeName, int index)
        {
            if (string.IsNullOrEmpty(typeName) || index < 0 || index > 2) return;
            ViewModes[typeName] = index;
        }
        internal static void RecordZoom(string typeName, int index)
        {
            if (string.IsNullOrEmpty(typeName) || index < 0) return;
            Zooms[typeName] = index;
        }
        internal static int ViewModeFor(string typeName)
        {
            int v;
            return typeName != null && ViewModes.TryGetValue(typeName, out v) ? v : -1;
        }
        internal static int ZoomFor(string typeName)
        {
            int v;
            return typeName != null && Zooms.TryGetValue(typeName, out v) ? v : -1;
        }
        internal static void NoteOpened(string typeName)
        {
            string kind = KindOf(typeName);
            if (kind != null) _lastKind = kind;
        }
        internal static void Clear()
        {
            ViewModes.Clear();
            Zooms.Clear();
            _lastKind = null;
        }
    }


    internal static class PuiPatches
    {
        internal static readonly MethodInfo SetToggle =
            AccessTools.Method(typeof(TorpedoAttackStationsViewSwitchUI), "SetToggle");
        internal static readonly FieldInfo UiValidation =
            AccessTools.Field(typeof(TorpedoAttackStationsViewSwitchUI), "uiValidation");
        internal static readonly MethodInfo SetController =
            AccessTools.Method(typeof(TorpedoAttackStationsViewSwitchUI), "SetExternalCameraController");
        
        internal static IEnumerable<string> MissingStationMembers()
        {
            if (SetToggle == null) yield return "TorpedoAttackStationsViewSwitchUI.SetToggle";
            if (UiValidation == null) yield return "TorpedoAttackStationsViewSwitchUI.uiValidation";
            if (SetController == null) yield return "TorpedoAttackStationsViewSwitchUI.SetExternalCameraController";
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnViewModeSelected")]
    internal static class RecordViewModePatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            if (!PuiSettings.Enabled) return;
            try
            {
                if (___mainCamera == null || ___mainCamera.GenericController == null) return;
                var pc = ___mainCamera.GenericController.ExternalController as PeriscopeController;
                if (pc != null && pc.Periscope != null) PuiMemory.RecordViewMode(pc.Periscope.TypeName, index);
            }
            catch (Exception ex) { PuiRestore.Fail("recording the view mode", ex); }
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnZoomSelected")]
    internal static class RecordZoomPatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            if (!PuiSettings.Enabled) return;
            try
            {
                if (___mainCamera == null || ___mainCamera.GenericController == null) return;
                var pc = ___mainCamera.GenericController.ExternalController as PeriscopeController;
                if (pc != null && pc.Periscope != null) PuiMemory.RecordZoom(pc.Periscope.TypeName, index);
            }
            catch (Exception ex) { PuiRestore.Fail("recording the zoom", ex); }
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnEnable")]
    internal static class RestoreOnOpenPatch
    {
        private static void Postfix(PeriscopeUI __instance, MainCamera ___mainCamera,
                                    TextMeshProDropdown ___viewModeDropdown, TextMeshProDropdown ___zoomDropdown)
        {
            if (!PuiSettings.Enabled) return;
            try { PuiRestore.OnOpened(__instance, ___mainCamera, ___viewModeDropdown, ___zoomDropdown); }
            catch (Exception ex) { PuiRestore.Fail("restoring the periscope view", ex); }
        }
    }
    [HarmonyPatch(typeof(TorpedoAttackStationsViewSwitchUI), "ValidateToggleState")]
    internal static class LightStationButtonPatch
    {
        private static void Postfix(TorpedoAttackStationsViewSwitchUI __instance, MainCamera ___mainCamera,
                                    Toggle ___periscopeToggle)
        {
            if (!PuiSettings.Enabled || !PuiSettings.StationButtonLastPeriscope) return;
            try
            {
                if (___periscopeToggle == null || PuiPatches.SetToggle == null || PuiPatches.UiValidation == null) return;
                var pc = PuiRestore.ExternalOf(___mainCamera) as PeriscopeController;
                if (pc == null || pc.Periscope == null) return;
                if (PuiMemory.KindOf(pc.Periscope.TypeName) != PuiMemory.ObservationKind) return;
                bool was = (bool)PuiPatches.UiValidation.GetValue(__instance);
                PuiPatches.UiValidation.SetValue(__instance, true);
                try { PuiPatches.SetToggle.Invoke(__instance, new object[] { ___periscopeToggle }); }
                finally { PuiPatches.UiValidation.SetValue(__instance, was); }
            }
            catch (Exception ex) { PuiRestore.Fail("lighting the station button", ex); }
        }
    }
    [HarmonyPatch(typeof(TorpedoAttackStationsViewSwitchUI), "OnPeriscopeToggleValueChanged")]
    internal static class StationButtonTargetPatch
    {
        private static bool Prefix(bool value, bool ___uiValidation, PlayerShip ___playerShip)
        {
            if (!PuiSettings.Enabled || !PuiSettings.StationButtonLastPeriscope) return true;
            if (___uiValidation || !value) return true;
            try
            {
                string kind = PuiMemory.LastKind;
                if (kind == null || ___playerShip == null || PuiPatches.SetController == null) return true;
                ScopeItem scope = ___playerShip.GetEquipment<ScopeItem>(kind);
                if (scope == null || !scope.enabled) return true;
                PuiPatches.SetController.Invoke(null, new object[] { scope });
                if (PuiRestore.Verbose) Util.Log("Sehrohr-Anzeige: station button -> " + kind);
                return false;
            }
            catch (Exception ex)
            {
                PuiRestore.Fail("choosing the station button's periscope", ex);
                return true;
            }
        }
    }


    internal static class PuiRestore
    {
        private const string ModuleId = "PeriscopeUIFix";
        private static readonly MethodInfo ViewHandler =
            AccessTools.Method(typeof(PeriscopeUI), "OnViewModeSelected", new[] { typeof(int) });
        private static readonly MethodInfo ZoomHandler =
            AccessTools.Method(typeof(PeriscopeUI), "OnZoomSelected", new[] { typeof(int) });
        private static readonly FieldInfo DropdownValue =
            AccessTools.Field(typeof(TextMeshProDropdown), "m_Value");
        private static readonly MethodInfo DropdownRefresh =
            AccessTools.Method(typeof(TextMeshProDropdown), "Refresh");
        private static readonly HashSet<string> WarnedFilters = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> FailedSites = new HashSet<string>(StringComparer.Ordinal);
        internal static bool Verbose { get { return Settings.B("ScopeUiLog"); } }
        internal static string MemberReport()
        {
            var missing = new List<string>();
            if (ViewHandler == null) missing.Add("PeriscopeUI.OnViewModeSelected");
            if (ZoomHandler == null) missing.Add("PeriscopeUI.OnZoomSelected");
            if (DropdownValue == null) missing.Add("TextMeshProDropdown.m_Value");
            if (DropdownRefresh == null) missing.Add("TextMeshProDropdown.Refresh");
            missing.AddRange(PuiPatches.MissingStationMembers());
            if (missing.Count == 0) return "all 7 members resolved";
            return "MISSING " + missing.Count + ": " + string.Join(", ", missing.ToArray());
        }
        internal static void Fail(string where, Exception ex)
        {
            if (!FailedSites.Add(where)) return;
            Util.Log("Sehrohr-Anzeige: " + where + " failed (further failures here are not logged): " + ex);
        }
        internal static IExternalController ExternalOf(MainCamera cam)
        {
            if (cam == null) return null;
            IExternalController ec = null;
            try { ec = cam.TransitionTargetModeDescriptor.ExternalController; }
            catch (Exception) { ec = null; }
            if (ec == null && cam.GenericController != null) ec = cam.GenericController.ExternalController;
            return ec;
        }
        internal static void OnOpened(PeriscopeUI ui, MainCamera cam,
                                      TextMeshProDropdown viewDropdown, TextMeshProDropdown zoomDropdown)
        {
            if (cam == null || cam.GenericController == null) return;
            var pc = cam.GenericController.ExternalController as PeriscopeController;
            if (pc == null) return;
            ScopeItem scope = pc.Periscope;
            if (scope == null) return;
            string name = scope.TypeName;
            PuiMemory.NoteOpened(name);
            CheckColourFilter(scope);
            if (!PuiSettings.RememberViewAndZoom) return;
            if (!scope.IsBinocular)
            {
                int want = PuiMemory.ViewModeFor(name);
                int live = PuiMemory.ViewModeOf(scope.FullScreenView, pc.ForceCloserView);
                if (want < 0)
                {
                    ShowSilently(viewDropdown, live);
                }
                else
                {
                    if (want != live && ViewHandler != null)
                    {
                        ViewHandler.Invoke(ui, new object[] { want });
                        if (Verbose)
                            Util.Log("Sehrohr-Anzeige: re-applied view mode " + want + " (was " + live + ") on " + name);
                    }
                    ShowSilently(viewDropdown, want);
                }
            }
            float[] presets = scope.FovPresets;
            int count = presets == null ? 0 : presets.Length;
            if (count == 0) return;
            int wantZoom = PuiMemory.ZoomFor(name);
            if (wantZoom >= 0)
            {
                wantZoom = PuiMemory.ClampZoom(wantZoom, count);
                int liveZoom = scope.FovIndex;
                if (wantZoom != liveZoom && ZoomHandler != null)
                {
                    ZoomHandler.Invoke(null, new object[] { wantZoom });
                    if (Verbose)
                        Util.Log("Sehrohr-Anzeige: re-applied zoom " + wantZoom + " (was " + liveZoom + ") on " + name);
                }
            }
            ShowSilently(zoomDropdown, PuiMemory.ClampZoom(scope.FovIndex, count));
        }
        private static void ShowSilently(TextMeshProDropdown dropdown, int index)
        {
            if (dropdown == null || index < 0 || DropdownValue == null || DropdownRefresh == null) return;
            if ((int)DropdownValue.GetValue(dropdown) == index) return;
            DropdownValue.SetValue(dropdown, index);
            DropdownRefresh.Invoke(dropdown, null);
        }
        private static void CheckColourFilter(ScopeItem scope)
        {
            string[] names = scope.FilterNames;
            if (names == null || names.Length == 0 || scope.PeriscopeCamera == null) return;
            PeriscopeIME ime = scope.PeriscopeCamera.GetComponent<PeriscopeIME>();
            if (ime == null) return;
            string current = ime.ColorFilterName;
            if (current != null && Array.IndexOf(names, current) >= 0) return;
            string shown = current ?? "<null>";
            if (!WarnedFilters.Add(scope.TypeName + "|" + shown)) return;
            Util.Log("Sehrohr-Anzeige: colour filter '" + shown + "' on " + scope.TypeName +
                             " is not in its list (" + string.Join(", ", names) +
                             "); the filter dropdown may not match the view");
        }
    }

}
