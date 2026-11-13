using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Messages;

namespace UboatToolkit.Modules
{
    // QoL (DoMe 2026-11-13): every radio message is readable. The game shows "Message Encrypted" when the message's
    // EncryptionMethod is neither Raw nor the player's country's method (RadioStory.GetFormattedMessageContents and
    // JournalMessageUI.Start). While those two methods run, the player's country temporarily uses the message's method.
    internal static class RadioDecrypt
    {
        private static readonly System.Reflection.FieldInfo EncF = AccessTools.Field(typeof(Country), "encryptionMethod");
        private static bool logged;

        internal static object Swap(Country c, IMessage m)
        {
            if (c == null || m == null || EncF == null || !Settings.B("RadioDecryptAll")) return null;
            var own = EncF.GetValue(c);
            if (Equals(own, m.EncryptionMethod) || m.EncryptionMethod == EncryptionMethod.Raw) return null;
            EncF.SetValue(c, m.EncryptionMethod);
            if (!logged) { logged = true; Util.Log("Funk: fremd verschlüsselter Spruch entschlüsselt (" + m.EncryptionMethod + ")"); }
            return own;
        }

        internal static void Restore(Country c, object own)
        {
            if (c != null && own != null && EncF != null) EncF.SetValue(c, own);
        }

        internal static Country PlayerCountry(object proxy)
        {
            var c = proxy != null ? Traverse.Create(proxy).Property("Country").GetValue<Country>() : null;
            if (c == null && Util.PlayerShip != null) c = Util.PlayerShip.Country;
            return c;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Stories.RadioStory), "GetFormattedMessageContents")]
    internal static class RadioDecrypt_StoryPatch
    {
        private static void Prefix(object __instance, IMessage message, out object[] __state)
        {
            __state = null;
            try
            {
                var c = RadioDecrypt.PlayerCountry(Traverse.Create(__instance).Field("playerShip").GetValue());
                var own = RadioDecrypt.Swap(c, message);
                if (own != null) __state = new object[] { c, own };
            }
            catch { }
        }

        private static System.Exception Finalizer(System.Exception __exception, object[] __state)
        {
            if (__state != null) RadioDecrypt.Restore((Country)__state[0], __state[1]);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.Journal.JournalMessageUI), "Start")]
    internal static class RadioDecrypt_JournalPatch
    {
        private static void Prefix(object __instance, out object[] __state)
        {
            __state = null;
            try
            {
                var t = Traverse.Create(__instance);
                var c = RadioDecrypt.PlayerCountry(t.Field("playerShip").GetValue());
                var own = RadioDecrypt.Swap(c, t.Field("message").GetValue<IMessage>());
                if (own != null) __state = new object[] { c, own };
            }
            catch { }
        }

        private static System.Exception Finalizer(System.Exception __exception, object[] __state)
        {
            if (__state != null) RadioDecrypt.Restore((Country)__state[0], __state[1]);
            return __exception;
        }
    }
}
