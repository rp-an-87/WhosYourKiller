using System;
using System.Reflection;
using EFT;
using EFT.Counters;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WhosYourKiller
{
	// The stats panel shows "average life span" as online time divided by raids. Our online time includes menu time, which would inflate it,
	// so for mocked profiles we show raid time divided by raids instead.
	internal class StatsPanelPatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OverallStatsPanel), nameof(OverallStatsPanel.FillStats));

		[PatchPostfix]
		private static void Postfix(OverallStatsPanel __instance, CountersCollection counters)
		{
			try
			{
				var total = Traverse.Create(__instance).Field("_totalInGameTime").GetValue<long>();
				if (!FlavorStats.RaidSecondsByTotal.TryGetValue(total, out var raidSeconds))
				{
					return;
				}

				var sessions = counters.GetAllLong(new object[] { CounterTag.Sessions });
				if (sessions > 0)
				{
					__instance._averageLifetimeLabel.text = OverallStatsPanel.AvgLifeTimeText(raidSeconds / sessions);
				}
			}
			catch (Exception e)
			{
				Plugin.Log.LogError($"Average life span patch failed: {e}");
			}
		}
	}
}
