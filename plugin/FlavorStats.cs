using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Counters;
using HarmonyLib;

namespace WhosYourKiller
{
	// Invented-but-consistent career for "scav users" (scavs that carry a player-like name), seeded by the bot's profile id so the same bot always
	// shows the same numbers. Three steps, each one built on the previous:
	//   1. baseline stats from progress (prestige x 70 + level)
	//   2. the SAIN personality skews those stats
	//   3. achievements are derived from the stats (hard requirements first, chance only where the stats cannot decide)
	internal static class FlavorStats
	{
		// A scav user is a Savage assault bot that got a random PMC name in MainProfileNickname (BotGenerator.ShouldSimulatePlayerScav on the server)
		internal static bool IsScavUser(Profile profile)
		{
			var info = profile?.Info;
			return info != null && info.Side == EPlayerSide.Savage && info.Settings?.Role == WildSpawnType.assault && !string.IsNullOrEmpty(info.MainProfileNickname);
		}

		// ---- SAIN personality (soft dependency, through the reflection entry point SAIN provides) ----
		private static MethodInfo _getPersonality;
		private static bool _sainLooked;

		internal static string GetPersonality(Player bot)
		{
			try
			{
				if (!_sainLooked)
				{
					_sainLooked = true;
					_getPersonality = AccessTools.Method(AccessTools.TypeByName("SAIN.Interop.SAINExternal"), "GetPersonality");
					Plugin.Verbose(_getPersonality != null ? "SAIN found: personality will skew the killer's stats" : "SAIN not found: neutral stats");
				}

				var owner = bot?.AIData?.BotOwner;
				return _getPersonality != null && owner != null ? (string)_getPersonality.Invoke(null, new object[] { owner }) ?? "" : "";
			}
			catch (Exception e)
			{
				Plugin.Log.LogWarning($"Reading the SAIN personality failed: {e.Message}");
				return "";
			}
		}

		// ---- step 2: personality skew (multipliers on the baseline) ----
		private class Skew
		{
			public float Survival = 1, Kd = 1, Leave = 1, Run = 1, RaidLength = 1, Streak = 1;
			public float PmcKillShare = 1;   // share of kills that were PMCs
			public float Melee = 1;          // melee kills
			public float Aggro = 1;          // flashed kills, fights on purpose
			public float Explore = 1;        // exploring / transit / co-op extracts
			public float Coop = 1;           // Scav Co-Op extracts
		}

		private static Skew SkewFor(string personality)
		{
			switch (personality)
			{
				case "GigaChad": return new Skew { Survival = 1.25f, Kd = 2.4f, RaidLength = 1.05f, Streak = 1.3f, PmcKillShare = 1.6f, Melee = 1.5f, Aggro = 1.5f };
				case "Chad": return new Skew { Survival = 1.12f, Kd = 1.8f, RaidLength = 1.05f, Streak = 1.15f, PmcKillShare = 1.4f, Melee = 2f, Aggro = 1.5f };
				case "Wreckless": return new Skew { Survival = 0.7f, Kd = 1.6f, RaidLength = 0.55f, Run = 0.4f, Streak = 0.6f, PmcKillShare = 1.2f, Melee = 3f, Aggro = 3f, Explore = 0.6f };
				case "SnappingTurtle": return new Skew { Survival = 1.15f, Kd = 1.15f, RaidLength = 1.5f, Streak = 1.3f, Explore = 0.8f };
				case "Rat": return new Skew { Survival = 1.45f, Kd = 0.8f, Leave = 0.6f, Run = 1.7f, RaidLength = 1.55f, Streak = 1.8f, PmcKillShare = 0.6f, Melee = 1.2f, Explore = 1.8f, Coop = 1.5f };
				case "Timmy": return new Skew { Survival = 0.75f, Kd = 0.55f, Leave = 1.3f, RaidLength = 0.7f, Streak = 0.5f, PmcKillShare = 0.6f, Melee = 0.3f, Coop = 2.5f };
				case "Coward": return new Skew { Survival = 0.9f, Kd = 0.5f, Leave = 3.5f, Run = 2.2f, RaidLength = 0.55f, Streak = 0.9f, PmcKillShare = 0.5f, Melee = 0.3f, Explore = 1.6f, Coop = 2.5f };
				default: return new Skew();
			}
		}

		// ---- bosses (step 3 input): chance per raid to meet them, minimum progress, achievement ids ----
		private class Boss
		{
			public string Name, FirstId, Fifteen, Hundred;
			public float EncounterPerRaid;
			public int MinProgress;
			public int Kills;
		}

		private static List<Boss> NewBosses() => new List<Boss>
		{
			new Boss { Name = "Reshala", FirstId = "6513ee11a3dd9b6aa7159b4a", Fifteen = "65140c00b1c08b0feb216d50", EncounterPerRaid = 0.05f, MinProgress = 12 },
			new Boss { Name = "Shturman", FirstId = "6513ed89cf2f1c285e606068", Fifteen = "65140bbec31fcb0e163577b9", EncounterPerRaid = 0.05f, MinProgress = 15 },
			new Boss { Name = "Sanitar", FirstId = "6513efa1b49e3253755f47eb", Fifteen = "651411f1cf2f1c285e606423", EncounterPerRaid = 0.04f, MinProgress = 18 },
			new Boss { Name = "Killa", FirstId = "6512f1e3be73cc7f07358ed5", Fifteen = "65140ab8ec10ff011f17cc10", Hundred = "6514143d59647d2cb3213c93", EncounterPerRaid = 0.04f, MinProgress = 22 },
			new Boss { Name = "Kaban", FirstId = "6513f0a10dc723592b0f90cf", Fifteen = "651412b8c31fcb0e163577c5", EncounterPerRaid = 0.04f, MinProgress = 25 },
			new Boss { Name = "Tagilla", FirstId = "6513eb6e0dc723592b0f9095", Fifteen = "65140b55cf2f1c285e606414", EncounterPerRaid = 0.035f, MinProgress = 25 },
			new Boss { Name = "Glukhar", FirstId = "6513eec00dc723592b0f90cc", Fifteen = "65141032a3dd9b6aa7159ed3", EncounterPerRaid = 0.03f, MinProgress = 28 },
			new Boss { Name = "Kollontay", FirstId = "655b49bc91aa9e07687ae47c", Fifteen = "655b4a576689c676ce57acb6", EncounterPerRaid = 0.03f, MinProgress = 30 },
			new Boss { Name = "Partisan", FirstId = "66c328de9dc78468f4040f35", Fifteen = "66c32996b4c0c017a3319cc3", EncounterPerRaid = 0.02f, MinProgress = 30 },
			// the three goons are met together, each member is killed separately
			new Boss { Name = "Knight", FirstId = null, Fifteen = "6514134eec10ff011f17cc26", EncounterPerRaid = 0.04f, MinProgress = 28 },
			new Boss { Name = "Birdeye", FirstId = null, Fifteen = "6527d3aac656a951ad1528ce", EncounterPerRaid = 0.04f, MinProgress = 28 },
			new Boss { Name = "BigPipe", FirstId = null, Fifteen = "6527d2e2c656a951ad1528c3", EncounterPerRaid = 0.04f, MinProgress = 28 },
		};

		private const string Id_LongLiveTheKing = "651415feb49e3253755f4b68";
		private const string Id_Snowball = "6529097eccf6aa5f8737b3d0";
		private const string Id_ThreeOfThem = "6513f1feec10ff011f17c7ea";
		private const string Id_Pmc300 = "65141dd6303df252af1c72c9";
		private const string Id_ReadyForAnything = "65141cd2cf2f1c285e606449";
		private const string Id_BlindFury = "651419eea3dd9b6aa7159ee5";
		private const string Id_MakeANotch = "65141a3059647d2cb3213c9e";
		private const string Id_Usec50 = "65141b9859647d2cb3213ca2";
		private const string Id_Bear50 = "65141bdfcf2f1c285e606446";
		private const string Id_BestFriend = "6512ea46f7a078264a4376e4";
		private const string Id_Vagabond = "66c328aca91e7d66fa1b0b7b";
		private const string Id_Traveler = "6512eb3ddfb0ae1ee75a0376";
		private const string Id_Marathon = "66b493bc2d8cd3b5e90a3648";
		private const string Id_Firefly = "6514321bec10ff011f17ccac";
		private const string Id_JackOfAllTrades = "664f23e44702fd5db50ee732";
		private const string Id_Kappa = "664f1f8768508d74604bf556";
		private const string Id_AllWheelDrive = "675709bef4e2a2ce0f058f56";
		private const string Id_AllOnRed = "67c838a4c566b0028f0f2d07";

		private static readonly string[] EventIds =
		{
			"670feb95a4e71050310cc14b", // Complete Remission
			"670febed5ee0fc738a0965a4", // Fatal Outcome
			"660fe21454670811e304c045", // Maslenitsa
			"66742c003a67b164a300fcbf", // A Key to Salvation
			"675998a894008342eb04e47f", // Khorovod
			"66e2a7e5919bad697104f4b3", // Highway to the Danger Zone
		};

		// ---- helpers ----
		private static int StableSeed(string text)
		{
			unchecked
			{
				var hash = (int)2166136261;
				foreach (var c in text ?? "")
				{
					hash = (hash ^ c) * 16777619;
				}
				return hash;
			}
		}

		private static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

		private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

		// Number of events with the given average (Knuth for small averages, normal approximation for large ones)
		private static int Poisson(Random rng, double lambda)
		{
			if (lambda <= 0)
			{
				return 0;
			}

			if (lambda > 30)
			{
				var u1 = 1.0 - rng.NextDouble();
				var u2 = rng.NextDouble();
				var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
				return Math.Max(0, (int)Math.Round(lambda + Math.Sqrt(lambda) * normal));
			}

			var limit = Math.Exp(-lambda);
			var product = rng.NextDouble();
			var count = 0;
			while (product > limit)
			{
				count++;
				product *= rng.NextDouble();
			}

			return count;
		}

		// ---- the whole thing ----
		internal static void Apply(OtherPlayerProfileDescriptor descriptor, string profileId, int level, int prestige, string personality)
		{
			var rng = new Random(StableSeed(profileId));
			var skew = SkewFor(personality);
			var progress = prestige * 70 + level;
			var nowUnix = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			// ---------- step 1: baseline, step 2: skew ----------
			var ageDays = Math.Max(5, (int)(progress * Range(rng, 3f, 6f)));
			var registered = nowUnix - ageDays * 86400;
			descriptor.Info.RegistrationDate = registered;

			var skill = 1f + Math.Min(progress, 100) / 200f;                 // better players kill a little more as they progress
			var pmcRaids = Math.Max(3, (int)(progress * Range(rng, 5f, 9f)));
			var scavRaids = Math.Max(2, (int)(pmcRaids * Range(rng, 0.25f, 0.6f)));

			var survivalRate = Clamp(Range(rng, 0.40f, 0.55f) * skew.Survival, 0.15f, 0.85f);
			var kd = Clamp(Range(rng, 0.9f, 1.4f) * skill * skew.Kd, 0.3f, 6f);
			var leaveRate = Clamp(0.06f * Range(rng, 0.7f, 1.3f) * skew.Leave, 0f, 0.4f);
			var runRate = Clamp(0.08f * Range(rng, 0.7f, 1.3f) * skew.Run, 0f, 0.4f);
			var minutes = Range(rng, 18f, 28f) * skew.RaidLength;

			var pmc = BuildCareer(rng, pmcRaids, survivalRate, kd, leaveRate, runRate, minutes, skew.Streak);
			var scav = BuildCareer(rng, scavRaids, Clamp(survivalRate * Range(rng, 0.8f, 1.1f), 0.1f, 0.85f), kd * Range(rng, 0.7f, 1.0f), leaveRate, runRate, minutes * 0.8f, skew.Streak);

			descriptor.PmcStats.Eft = pmc.ToDescriptor();
			descriptor.ScavStats.Eft = scav.ToDescriptor();

			// kinds of kills, taken from the PMC career
			var pmcKills = (int)(pmc.Kills * Clamp(Range(rng, 0.15f, 0.28f) * skew.PmcKillShare, 0.05f, 0.6f));
			var usecKills = (int)(pmcKills * Range(rng, 0.4f, 0.6f));
			var bearKills = pmcKills - usecKills;
			var meleeKills = (int)(pmc.Kills * Range(rng, 0.005f, 0.02f) * skew.Melee);

			// ---------- step 3: achievements ----------
			var earned = new Dictionary<MongoID, int>();
			var when = new Dictionary<string, float>();   // 0 = registration, 1 = now; keeps dependent achievements after the ones they need

			void Give(string id, float after = 0f)
			{
				var f = Clamp(after + (1f - after) * (float)rng.NextDouble(), 0.02f, 1f);
				when[id] = f;
				earned[id] = registered + (int)(f * (nowUnix - registered));
			}

			bool Held(string id) => earned.ContainsKey(id);
			bool Roll(float chance) => rng.NextDouble() < chance;

			// bosses: expected kills = raids x meeting chance x chance to win, then a random actual count
			var bosses = NewBosses();
			var winChance = Clamp(0.15f + 0.12f * kd, 0.1f, 0.6f);
			foreach (var boss in bosses)
			{
				boss.Kills = progress < boss.MinProgress ? 0 : Poisson(rng, pmcRaids * boss.EncounterPerRaid * winChance);
			}

			foreach (var boss in bosses)
			{
				if (boss.FirstId != null && boss.Kills >= 1)
				{
					Give(boss.FirstId);
				}
			}

			// the goons count as one fight for the "first time" achievement: all three have to be down, in one raid
			var goons = bosses.FindAll(b => b.FirstId == null);
			var allGoons = goons.TrueForAll(b => b.Kills >= 1);

			foreach (var boss in bosses)
			{
				if (boss.Kills >= 15 && (boss.FirstId == null || Held(boss.FirstId)))
				{
					Give(boss.Fifteen, boss.FirstId != null ? when[boss.FirstId] : 0.3f);
				}

				if (boss.Hundred != null && boss.Kills >= 100 && Held(boss.Fifteen))
				{
					Give(boss.Hundred, when[boss.Fifteen]);
				}
			}

			if (allGoons)
			{
				var fewest = Math.Min(goons[0].Kills, Math.Min(goons[1].Kills, goons[2].Kills));
				if (Roll(Clamp(0.15f + fewest * 0.08f, 0.15f, 0.9f)))
				{
					Give(Id_ThreeOfThem);
				}
			}

			// every boss once = every boss in the list (goons included)
			if (bosses.TrueForAll(b => b.Kills >= 1))
			{
				Give(Id_LongLiveTheKing, 0.5f);
				if (pmc.Streak >= 12 && Roll(0.6f))
				{
					Give(Id_Snowball, when[Id_LongLiveTheKing]);
				}
			}

			// hard facts from the stats
			if (pmc.Survived >= 300) Give(Id_Pmc300, 0.5f);
			if (pmc.Streak >= 10) Give(Id_ReadyForAnything, 0.2f);
			if (meleeKills >= 30) Give(Id_MakeANotch, 0.3f);
			if (usecKills >= 50) Give(Id_Usec50, 0.3f);
			if (bearKills >= 50) Give(Id_Bear50, 0.3f);

			// things the stats make more or less likely
			if (pmc.Kills >= 50 && Roll(1f - (float)Math.Exp(-pmc.Kills * 0.004 * skew.Aggro))) Give(Id_BlindFury);
			if (progress >= 15 && Roll(1f - (float)Math.Exp(-pmcRaids * 0.004 * skew.Coop))) Give(Id_BestFriend);
			if (pmcRaids >= 30 && Roll(1f - (float)Math.Exp(-pmcRaids * 0.006 * skew.Explore))) Give(Id_Vagabond);
			if (pmc.Survived >= 60 && Roll(Clamp((pmc.Survived - 60) / 400f * skew.Explore, 0f, 0.8f))) Give(Id_Traveler, 0.3f);
			if (pmcRaids >= 150 && Roll(Clamp(0.05f * skew.Explore * skew.Aggro, 0f, 0.4f))) Give(Id_Marathon, 0.4f);

			// quest lines, which depend on how far the account has come
			if (Roll(Clamp((progress - 30) / 40f, 0f, 0.85f))) Give(Id_Firefly);
			if (Roll(Clamp((progress - 28) / 40f, 0f, 0.8f))) Give(Id_AllWheelDrive);
			if (Roll(Clamp((progress - 38) / 50f, 0f, 0.6f))) Give(Id_AllOnRed);
			if (Roll(Clamp((progress - 45) / 50f, 0f, 0.7f))) Give(Id_JackOfAllTrades, 0.3f);
			if (Roll(Clamp((progress - 55) / 60f, 0f, 0.6f))) Give(Id_Kappa, 0.5f);

			// event task lines: flat chance for anyone who has been around for a while
			if (progress >= 30)
			{
				foreach (var id in EventIds)
				{
					if (Roll(0.12f)) Give(id);
				}
			}

			descriptor.AchievementsData = earned;
			Plugin.Verbose($"Flavor stats for {profileId}: progress {progress} (level {level}, prestige {prestige}), personality '{(personality.Length == 0 ? "none" : personality)}', " +
				$"{pmcRaids}+{scavRaids} raids, survival {pmc.Survived}/{pmcRaids}, K/D {(pmc.Deaths > 0 ? (float)pmc.Kills / pmc.Deaths : pmc.Kills):0.00}, streak {pmc.Streak}, {earned.Count} achievements");
		}

		// ---- one career (PMC or scav) ----
		private class Career
		{
			public int Raids, Survived, Left, Runners, Mia, Killed, Deaths, Kills, Streak;
			public long Seconds;

			public ProfileStatsDescriptor ToDescriptor()
			{
				var counters = new CountersCollection(new Dictionary<CountersCollection.Identifier, long>());
				counters.SetLong(Raids, CounterTag.Sessions);
				counters.SetLong(Survived, CounterTag.ExitStatus, ExitStatus.Survived);
				counters.SetLong(Left, CounterTag.ExitStatus, ExitStatus.Left);
				counters.SetLong(Runners, CounterTag.ExitStatus, ExitStatus.Runner);
				counters.SetLong(Mia, CounterTag.ExitStatus, ExitStatus.MissingInAction);
				counters.SetLong(Killed, CounterTag.ExitStatus, ExitStatus.Killed);
				counters.SetLong(Deaths, CounterTag.Deaths);
				counters.SetLong(Kills, CounterTag.Kills);
				counters.SetLong(Streak, CounterTag.LongestWinStreak);

				return new ProfileStatsDescriptor
				{
					OverallCounters = new CounterCollectionDescriptor(counters),
					TotalInGameTime = Seconds,
				};
			}
		}

		private static Career BuildCareer(Random rng, int raids, float survivalRate, float kd, float leaveRate, float runRate, float minutes, float streakSkew)
		{
			var c = new Career { Raids = raids };
			c.Survived = (int)(raids * survivalRate);
			c.Left = (int)(raids * leaveRate);
			c.Runners = (int)(raids * runRate);
			c.Mia = (int)(raids * 0.03f * Range(rng, 0.5f, 1.5f));

			// the exit types must add up to the raids, so the leftovers are deaths; shrink the optional ones if they overshoot
			while (c.Survived + c.Left + c.Runners + c.Mia > raids)
			{
				if (c.Left > 0) c.Left--;
				else if (c.Runners > 0) c.Runners--;
				else if (c.Mia > 0) c.Mia--;
				else c.Survived--;
			}

			c.Killed = raids - c.Survived - c.Left - c.Runners - c.Mia;
			c.Deaths = c.Killed + c.Mia;
			c.Kills = (int)(Math.Max(1, c.Deaths) * kd);

			// longest streak of survivals: for survival rate s over n raids the expected longest run is about ln(n(1-s)) / ln(1/s)
			if (c.Survived > 0)
			{
				var s = Clamp(c.Survived / (float)raids, 0.05f, 0.95f);
				var expected = Math.Max(1.0, Math.Log(Math.Max(2.0, raids * (1.0 - s))) / Math.Log(1.0 / s));
				c.Streak = Math.Max(1, Math.Min(c.Survived, (int)Math.Round(expected * Range(rng, 0.7f, 1.4f) * streakSkew)));
			}

			c.Seconds = (long)(raids * minutes * 60f);
			return c;
		}
	}
}
