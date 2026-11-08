using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.UI.SessionEnd;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;

namespace WhosYourKiller
{
	// The server cannot answer profile requests for bots (they are not stored), and the game hides the profile button for AI killers.
	// We snapshot a bot's gear when it kills us or when we kill it, and answer the profile request locally with a mocked profile.
	internal static class KillerPatches
	{
		private const string KeyPrefix = "wyk-";
		private const int MaxSnapshots = 300;

		// key -> mocked profile; the oldest are dropped past MaxSnapshots
		private static readonly Dictionary<string, OtherPlayerProfileDescriptor> Snapshots = new Dictionary<string, OtherPlayerProfileDescriptor>();
		private static readonly Queue<string> SnapshotOrder = new Queue<string>();

		private static bool IsOurKey(string accountId) => accountId != null && accountId.StartsWith(KeyPrefix, StringComparison.Ordinal);

		private static void Clear()
		{
			Snapshots.Clear();
			SnapshotOrder.Clear();
		}

		// A new raid starts: drop the snapshots of the previous one (its death screen and kill list are long gone)
		internal class RaidStartPatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));

			[PatchPrefix]
			private static void Prefix(GameWorld __instance)
			{
				if (__instance is HideoutGameWorld || Snapshots.Count == 0)
				{
					return;
				}

				Plugin.Verbose($"Raid started: cleared {Snapshots.Count} snapshots");
				Clear();
			}
		}

		internal static string KeyFor(string profileId) => KeyPrefix + profileId;

		internal static bool HasSnapshot(string key) => key != null && Snapshots.ContainsKey(key);

		// Mocked profile of a bot, keyed by its profile id. Returns the key.
		private static string Capture(Profile botProfile, Player bot, string what)
		{
			var key = KeyFor(botProfile.Id);
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

			// Invented career stats and achievements, for PMC bots
			if (FlavorStats.GetsCareer(botProfile))
			{
				FlavorStats.Apply(descriptor, botProfile.Id, ProfileInfo.GetLevel(info.Experience), info.Experience, info.PrestigeLevel, FlavorStats.GetPersonality(bot));
			}

			if (!Snapshots.ContainsKey(key))
			{
				SnapshotOrder.Enqueue(key);
				while (SnapshotOrder.Count > MaxSnapshots)
				{
					Snapshots.Remove(SnapshotOrder.Dequeue());
				}
			}

			Snapshots[key] = descriptor;
			Plugin.Verbose($"Snapshot of {what} '{info.Nickname}' (role {info.Settings?.Role}, side {info.Side}) stored as {key}");
			return key;
		}

		internal class SnapshotPatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.OnBeenKilledByAggressor));

			[PatchPostfix]
			private static void Postfix(Player __instance, IPlayer aggressor)
			{
				try
				{
					if (__instance == null || aggressor == null)
					{
						return;
					}

					// We died to a bot: snapshot the killer and give the killer record our key
					if (__instance.IsYourPlayer)
					{
						var stats = __instance.Profile?.EftStats?.Aggressor;
						if (!aggressor.IsAI || stats == null || aggressor.Profile == null)
						{
							return;
						}

						stats.AccountId = Capture(aggressor.Profile, aggressor as Player, "killer");
						return;
					}

					// We killed a bot: snapshot the victim for the kill list
					if (aggressor.IsYourPlayer && __instance.IsAI && __instance.Profile != null)
					{
						Capture(__instance.Profile, __instance, "victim");
					}
				}
				catch (Exception e)
				{
					Plugin.Log.LogError($"Snapshot failed: {e}");
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

		// Make the rows of the post-raid kill list clickable: a click opens the victim's (mocked) profile
		internal class KillListPatch : ModulePatch
		{
			protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(KillListVictim), nameof(KillListVictim.Show));

			[PatchPostfix]
			private static void Postfix(KillListVictim __instance, VictimStats victim, bool knownName)
			{
				try
				{
					var row = __instance.GetComponent<VictimRowClick>();
					var key = victim == null ? null : KeyFor(victim.ProfileId);
					var known = knownName || victim?.Side == EPlayerSide.Savage;   // the same rule the row uses to show the name

					if (key == null || !Snapshots.ContainsKey(key) || (!known && !Plugin.ViewUnknownVictims.Value))
					{
						if (row != null)
						{
							row.Key = null;
						}

						return;
					}

					if (row == null)
					{
						row = __instance.gameObject.AddComponent<VictimRowClick>();
					}

					row.Key = key;
				}
				catch (Exception e)
				{
					Plugin.Log.LogError($"Kill list row failed: {e}");
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
				Plugin.Verbose($"Answered profile request {accountId} with the mocked profile");
				return false;
			}
		}
	}
}
