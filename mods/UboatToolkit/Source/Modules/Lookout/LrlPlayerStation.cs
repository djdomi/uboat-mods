// Ausguck (Teil des UBOAT Toolkit): LrlPlayerStation. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlPlayerStation
    {
        private const string GenericModeName = "Generic";

        private static bool _resolved;
        private static bool _usable;
        private static string _whyNot = "not resolved yet";

        private static PropertyInfo _cameraInstance;
        private static FieldInfo _cameraInstanceField;
        private static PropertyInfo _currentMode;
        private static PropertyInfo _externalController;
        private static Type _genericControllerType;
        private static FieldInfo _cameraGenericController;
        private static bool _externalControllerIsStatic;

        private static int _cachedFrame = -1;
        private static ObservationSpot _cachedSpot;
        private static Component _cameraComponent;
        private static readonly System.Collections.Generic.List<ObservationSpot> HintBuffer =
            new System.Collections.Generic.List<ObservationSpot>();
        private static string _cachedText = "unknown";

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var cameraType = AccessTools.TypeByName("UBOAT.Game.Scene.Camera.MainCamera");
                if (cameraType == null)
                {
                    _whyNot = "MainCamera type not found";
                    return;
                }

                _cameraInstance = AccessTools.Property(cameraType, "Instance");
                _cameraInstanceField = AccessTools.Field(cameraType, "Instance");
                _currentMode = AccessTools.Property(cameraType, "CurrentMode");

                _genericControllerType =
                    AccessTools.TypeByName("UBOAT.Game.Scene.Camera.GenericCameraController");
                if (_genericControllerType == null)
                {
                    _whyNot = "GenericCameraController type not found";
                    return;
                }

                _cameraGenericController = AccessTools.Field(cameraType, "genericController");

                _externalController = AccessTools.Property(_genericControllerType, "ExternalController");
                if (_externalController == null)
                {
                    _whyNot = "ExternalController property not found";
                    return;
                }

                var getter = _externalController.GetGetMethod(true);
                _externalControllerIsStatic = getter != null && getter.IsStatic;

                if (_currentMode == null)
                {
                    _whyNot = "MainCamera.CurrentMode not found";
                    return;
                }

                _usable = true;
                _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        private static object CameraInstance()
        {
            if (_cameraInstance != null)
            {
                var getter = _cameraInstance.GetGetMethod(true);
                if (getter != null && getter.IsStatic)
                    return _cameraInstance.GetValue(null, null);
            }

            if (_cameraInstanceField != null && _cameraInstanceField.IsStatic)
                return _cameraInstanceField.GetValue(null);

            return null;
        }

        private static void Refresh()
        {
            if (_cachedFrame == Time.frameCount)
                return;

            _cachedFrame = Time.frameCount;
            _cachedSpot = null;
            _cameraComponent = null;
            _cachedText = "unknown";

            Resolve();

            if (!_usable)
            {
                _cachedText = "unknown (" + _whyNot + ")";
                return;
            }

            var stage = "start";

            try
            {
                stage = "MainCamera.Instance";
                var camera = CameraInstance();

                if (camera == null || IsDeadUnityObject(camera))
                {
                    _cachedText = "unknown (no live MainCamera instance)";
                    return;
                }

                _cameraComponent = camera as Component;

                stage = "MainCamera.CurrentMode";
                var mode = _currentMode.GetValue(camera, null);
                var modeText = mode == null ? "null" : mode.ToString();

                if (!GenericModeName.Equals(modeText))
                {
                    _cachedText = "none (camera mode " + modeText + ")";
                    return;
                }

                stage = "GenericCameraController.ExternalController";
                object controller = null;
                try
                {
                    if (_externalControllerIsStatic)
                    {
                        controller = _externalController.GetValue(null, null);
                    }
                    else
                    {
                        object instance = null;

                        if (_cameraGenericController != null)
                            instance = _cameraGenericController.GetValue(camera);

                        if (instance == null || IsDeadUnityObject(instance))
                            instance = UnityEngine.Object.FindObjectOfType(_genericControllerType);

                        if (instance != null && !IsDeadUnityObject(instance))
                            controller = _externalController.GetValue(instance, null);
                    }
                }
                catch (Exception inner)
                {
                    var realInner = inner is TargetInvocationException && inner.InnerException != null
                        ? inner.InnerException
                        : inner;
                    _cachedText = "none (camera mode Generic; ExternalController unreadable: " +
                                  realInner.GetType().Name + ")";
                    return;
                }

                if (controller == null)
                {
                    _cachedText = "none (camera mode Generic, no external controller)";
                    return;
                }

                var component = controller as Component;
                if (component == null)
                {
                    _cachedText = "unrecognised (" + controller.GetType().Name + ")";
                    return;
                }

                stage = "GetComponentInParent<ObservationSpot>";
                _cachedSpot = component.GetComponentInParent<ObservationSpot>();
                _cachedText = _cachedSpot != null
                    ? _cachedSpot.Name
                    : "a station that is not an ObservationSpot (" + component.GetType().Name + ")";
            }
            catch (Exception ex)
            {
                var real = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                _cachedText = "unknown (" + real.GetType().Name + " at " + stage + ")";
            }
        }

        private static bool IsDeadUnityObject(object o)
        {
            var unityObject = o as UnityEngine.Object;
            return unityObject != null ? unityObject == null : false;
        }

        internal static string CameraProximityHint(PlayerShip ship)
        {
            if (ship == null)
                return null;

            Refresh();

            var cam = _cameraComponent;
            if (cam == null)
                return null;

            try
            {
                var camPos = cam.transform.position;

                HintBuffer.Clear();
                LrlProviders.GetAll(ship, HintBuffer);

                ObservationSpot best = null;
                var bestDist = float.MaxValue;

                for (var i = 0; i < HintBuffer.Count; i++)
                {
                    var spot = HintBuffer[i];
                    if (spot == null)
                        continue;

                    var d = (spot.transform.position - camPos).magnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = spot;
                    }
                }

                HintBuffer.Clear();

                if (best == null)
                    return null;

                return string.Format("nearestSpotToCamera={0} ({1:0.0}m)", best.Name, bestDist);
            }
            catch (Exception ex)
            {
                HintBuffer.Clear();
                return "nearestSpotToCamera=unknown (" + ex.GetType().Name + ")";
            }
        }

        internal static string Describe()
        {
            Refresh();
            return _cachedText;
        }

        internal static bool NamedAStation
        {
            get
            {
                Refresh();
                return _cachedSpot != null;
            }
        }

        internal static bool IsAt(ObservationSpot spot)
        {
            if (spot == null)
                return false;

            Refresh();
            return ReferenceEquals(_cachedSpot, spot);
        }
    }
}
