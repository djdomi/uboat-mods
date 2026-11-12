// Ausguck (Teil des UBOAT Toolkit): LrlPlayer. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlPlayer
    {
#pragma warning disable 649
        [Inject]
        private static PlayerShip injectedPlayerShip;

        [Inject]
        private static Weather injectedWeather;

        [Inject]
        private static TimeCompressionController injectedTimeCompression;
#pragma warning restore 649

        internal static TimeCompressionController TimeCompression
        {
            get { return injectedTimeCompression; }
        }

        internal static void TryInject()
        {
            try
            {
                InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());

                if (injectedPlayerShip != null)
                    LrlLog.Info("player ship bound via injection");
                else
                    LrlLog.Throttled("ship-unbound", 30f, "injection ran but PlayerShip is still null");
            }
            catch (Exception ex)
            {
                LrlLog.Error("injection failed", ex);
            }
        }

        internal static PlayerShip Ship
        {
            get
            {
                if (injectedPlayerShip != null)
                    return injectedPlayerShip;

                return UnityEngine.Object.FindObjectOfType<PlayerShip>();
            }
        }

        internal static Weather Weather
        {
            get
            {
                if (injectedWeather != null)
                    return injectedWeather;

                return UnityEngine.Object.FindObjectOfType<Weather>();
            }
        }

        internal static Weather InjectedWeather
        {
            get { return injectedWeather; }
        }
    }
}
