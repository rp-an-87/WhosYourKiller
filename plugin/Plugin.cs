using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace WhosYourKiller
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	public class Plugin : BaseUnityPlugin
	{
		public const string PluginGuid = "RPAN87.WhosYourKiller";
		public const string PluginName = "WhosYourKiller";
		public const string PluginVersion = "1.0.0";

		internal static ManualLogSource Log;
		internal static ConfigEntry<bool> VerboseLogging;

		internal static void Verbose(string message)
		{
			if (VerboseLogging != null && VerboseLogging.Value)
			{
				Log.LogInfo(message);
			}
		}

		private void Awake()
		{
			Log = Logger;
			VerboseLogging = Config.Bind("General", "Verbose logging", true, "Log what the plugin does (killer snapshot, mock profile). Warnings and errors are always logged.");

			new KillerPatches.SnapshotPatch().Enable();
			new KillerPatches.ShowButtonPatch().Enable();
			new KillerPatches.MockProfilePatch().Enable();

			Log.LogInfo($"{PluginName} {PluginVersion} loaded");
		}
	}
}
