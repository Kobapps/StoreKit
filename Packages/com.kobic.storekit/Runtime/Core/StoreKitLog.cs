using UnityEngine;

namespace StoreKit
{
    /// <summary>Centralized, prefixed logging for StoreKit. Verbose logs are gated by <see cref="StoreKitSettings.verboseLogging"/>.</summary>
    internal static class StoreKitLog
    {
        private const string Prefix = "[StoreKit] ";

        internal static bool Verbose;

        internal static void Info(string message)
        {
            if (Verbose)
            {
                Debug.Log(Prefix + message);
            }
        }

        internal static void Warn(string message) => Debug.LogWarning(Prefix + message);

        internal static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
