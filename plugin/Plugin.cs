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
		internal static ConfigEntry<bool> ViewUnknownVictims;

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

			ViewUnknownVictims = Config.Bind("Kill list", "View unknown victims", false, "Also let you click kill list rows whose name the game hides ('???'). By default only rows with a visible name are clickable, as in live Tarkov.");

			new KillerPatches.SnapshotPatch().Enable();
			new KillerPatches.KillListPatch().Enable();
			new KillerPatches.ShowButtonPatch().Enable();
			new KillerPatches.MockProfilePatch().Enable();
			new StatsPanelPatch().Enable();

			Log.LogInfo($"{PluginName} {PluginVersion} loaded");
		}
	}
}
