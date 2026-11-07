using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.UI.SessionEnd;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;

namespace WhosYourKiller
{
	// The game nulls AccountId/ProfileId in the killer record when the killer is AI, so the "watch profile" button never shows for bot kills
	// and the server could not answer anyway (bots are not stored). We snapshot the bot's gear at the moment it kills us, give the killer record a
	// private key, show the button, and answer the profile request locally with a mocked profile.
	internal static class KillerPatches
	{
		private const string KeyPrefix = "wyk-";

		// key -> mocked profile of the bot that killed us. Only the last kill matters, but a few are kept in case of several deaths in one session.
		private static readonly Dictionary<string, OtherPlayerProfileDescriptor> Snapshots = new Dictionary<string, OtherPlayerProfileDescriptor>();

		private static bool IsOurKey(string accountId) => accountId != null && accountId.StartsWith(KeyPrefix, StringComparison.Ordinal);

		internal class SnapshotPatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.OnBeenKilledByAggressor));

			[PatchPostfix]
			private static void Postfix(Player __instance, IPlayer aggressor)
			{
				try
				{
					if (__instance == null || !__instance.IsYourPlayer || aggressor == null || !aggressor.IsAI)
					{
						return;
					}

					var stats = __instance.Profile?.EftStats?.Aggressor;
					var botProfile = aggressor.Profile;
					if (stats == null || botProfile == null)
					{
						return;
					}

					var key = KeyPrefix + botProfile.Id;
					var info = botProfile.Info;

					var descriptor = new OtherPlayerProfileDescriptor
					{
						AccountId = key,
						Info = new OtherPlayerProfile.OtherPlayerInfo
						{
							Nickname = info.Nickname,
							Side = info.Side,
							Experience = info.Experience,
							PrestigeLevel = info.PrestigeLevel,
							MemberCategory = info.MemberCategory,
							SelectedMemberCategory = info.SelectedMemberCategory,
							RegistrationDate = info.RegistrationDate,
						},
						AchievementsData = new Dictionary<MongoID, int>(),
						Customization = new Dictionary<EBodyModelPart, MongoID>(botProfile.Customization),
						Equipment = new InventoryEquipmentDescriptor(botProfile.Inventory.Equipment),
						PmcStats = new ProfileStatsSeparatorDescriptor { Eft = new ProfileStatsDescriptor() },
						ScavStats = new ProfileStatsSeparatorDescriptor { Eft = new ProfileStatsDescriptor() },
						Skills = new SkillsDescriptor(),
						FavoriteItems = new FlatItem[0],
					};

					// Invented career stats and achievements, only for scav users (player-like scavs)
					if (FlavorStats.IsScavUser(botProfile))
					{
						FlavorStats.Apply(descriptor, botProfile.Id, ProfileInfo.GetLevel(info.Experience), info.PrestigeLevel, FlavorStats.GetPersonality(aggressor as Player));
					}

					Snapshots[key] = descriptor;
					stats.AccountId = key;
					Plugin.Verbose($"Snapshot of killer '{info.Nickname}' (role {info.Settings?.Role}, side {info.Side}) stored as {key}");
				}
				catch (Exception e)
				{
					Plugin.Log.LogError($"Killer snapshot failed: {e}");
				}
			}
		}

		// Show the watch-profile button for our own key (the game only shows it when ProfileId is set)
		internal class ShowButtonPatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() =>
				AccessTools.Method(typeof(SessionResultExitStatus), nameof(SessionResultExitStatus.Show),
					new[] { typeof(Profile), typeof(PlayerVisualRepresentation), typeof(ESideType), typeof(ExitStatus), typeof(TimeSpan), typeof(IEftSession), typeof(bool) });

			[PatchPostfix]
			private static void Postfix(SessionResultExitStatus __instance, Profile activeProfile, ExitStatus exitStatus)
			{
				try
				{
					var aggressor = activeProfile?.EftStats?.Aggressor;
					Plugin.Verbose($"Death screen: exit {exitStatus}, aggressor '{aggressor?.Name}', account '{aggressor?.AccountId}', profile '{aggressor?.ProfileId}', snapshots {Snapshots.Count}");
					if (exitStatus != ExitStatus.Killed || aggressor == null)
					{
						return;
					}

					// The end screen may use a copy of the profile without the key we set: match the killer by name instead
					if (!IsOurKey(aggressor.AccountId))
					{
						var found = false;
						foreach (var pair in Snapshots)
						{
							if (pair.Value.Info.Nickname == aggressor.Name)
							{
								aggressor.AccountId = pair.Key;
								found = true;
							}
						}

						if (!found)
						{
							return;
						}
					}

					var button = __instance._showProfileButton;
					button.Show(__instance.CG_Show, null, null, true, 0);
					Plugin.Verbose("Showed the watch-profile button for a bot killer");
				}
				catch (Exception e)
				{
					Plugin.Log.LogError($"Show button failed: {e}");
				}
			}
		}

		// Answer the profile request locally for our keys instead of asking the server
		internal class MockProfilePatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ClientBackendSession), nameof(ClientBackendSession.GetOtherPlayerProfile));

			[PatchPrefix]
			private static bool Prefix(string accountId, ref Task<Result<OtherPlayerProfileDescriptor>> __result)
			{
				if (!IsOurKey(accountId) || !Snapshots.TryGetValue(accountId, out var descriptor))
				{
					return true;
				}

				__result = Task.FromResult(new Result<OtherPlayerProfileDescriptor> { Value = descriptor });
				Plugin.Verbose($"Answered profile request {accountId} with the mocked killer profile");
				return false;
			}
		}
	}
}
