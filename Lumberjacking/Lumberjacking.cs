using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using SkillManager;
using UnityEngine;

namespace Lumberjacking;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Lumberjacking : BaseUnityPlugin
{
	private const string ModName = "Lumberjacking";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "org.bepinex.plugins.lumberjacking";

	private static readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<float> lumberjackingYieldFactor = null!;
	private static ConfigEntry<float> damageToTreesFactor = null!;
	private static ConfigEntry<float> damageFromTreesFactor = null!;
	private static ConfigEntry<float> forestMovementSpeedFactor = null!;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0
	}
	
	private static Skill lumberjacking = null!;

	public void Awake()
	{
		lumberjacking = new Skill("Lumberjacking", (Sprite)null!);
		lumberjacking.Description.English("Increases damage dealt to trees, item yield from trees and reduces damage taken from falling trees. Increases movement speed in forests.");
		lumberjacking.Name.German("Holzfällerei");
		lumberjacking.Description.German("Erhöht den Schaden an Bäumen, sowie die Ausbeute von Bäumen und reduziert den erlittenen Schaden durch Baumschlag. Erhöht die Bewegungsgeschwindigkeit in Wäldern.");
		
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		lumberjackingYieldFactor = config("1 - General", "Tree item yield modifier at level 100", 2f, new ConfigDescription("Item yield from trees will be modified by this value at skill level 100.", new AcceptableValueRange<float>(1f, 5f)));
		damageToTreesFactor = config("1 - General", "Damage to trees modifier at level 100", 3f, new ConfigDescription("Damage dealt to trees will be modified by this value at skill level 100.", new AcceptableValueRange<float>(1f, 5f)));
		damageFromTreesFactor = config("1 - General", "Tree fall damage modifier at level 100", 0.3f, new ConfigDescription("Damage from falling trees will be modified by this value at skill level 100.", new AcceptableValueRange<float>(0.00f, 1f)));
		forestMovementSpeedFactor = config("1 - General", "Movement speed modifier in forests at level 100", 1.1f, new ConfigDescription("Movement speed in forests will be modified by this value at skill level 100.", new AcceptableValueRange<float>(1f, 2f)));

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch(typeof(Skills), nameof(Skills.Awake))]
	public class RemoveWoodcutting
	{
		[UsedImplicitly]
		public static void Prefix(Skills __instance)
		{
			__instance.m_skills.RemoveAll(s =>
			{
				if (s.m_skill == Skills.SkillType.WoodCutting)
				{
					Skills.SkillDef lumberjackingDef = (Skills.SkillDef)lumberjacking.GetType().GetField("skillDef", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lumberjacking);
					lumberjackingDef.m_icon = s.m_icon;
					return true;
				}

				return false;
			});
		}
	}

	[HarmonyPatch]
	class RemoveWoodcuttingFromSkillFunctions
	{
		public static IEnumerable<MethodBase> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(Skills), nameof(Skills.CheatRaiseSkill)),
			AccessTools.DeclaredMethod(typeof(Skills), nameof(Skills.CheatResetSkill)),
		};

		public static void Prefix(ref string name)
		{
			if (name == "Woodcutting")
			{
				name += " ";
			}
		}
	}
	
	[HarmonyPatch(typeof(Skills), nameof(Skills.GetSkill))]
	public class ReturnDummyWoodcuttingSkill
	{
		public static Skills.Skill? lastSkill = null;
		
		[UsedImplicitly]
		public static bool Prefix(Skills.SkillType skillType, ref Skills.Skill __result)
		{
			if (skillType == Skills.SkillType.WoodCutting)
			{
				__result = lastSkill = new Skills.Skill(new Skills.SkillDef { m_skill = Skills.SkillType.None, m_increseStep = 0 });
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Skills), nameof(Skills.GetSkillDef))]
	public class ReturnDummyWoodcuttingSkillDef
	{
		[UsedImplicitly]
		public static bool Prefix(Skills.SkillType type, ref Skills.SkillDef __result)
		{
			if (type == Skills.SkillType.WoodCutting)
			{
				__result = new Skills.SkillDef { m_skill = Skills.SkillType.None, m_increseStep = 0 };
				return false;
			}

			return true;
		}
	}
	
	[HarmonyPatch(typeof(Skills), nameof(Skills.IsSkillValid))]
	public class MarkWoodcuttingSkillInvalid
	{
		[UsedImplicitly]
		public static bool Prefix(Skills.SkillType type, ref bool __result)
		{
			if (!PreserveWoodcuttingSkillValueLoad.Loading && type == Skills.SkillType.WoodCutting)
			{
				__result = false;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Skills), nameof(Skills.Load))]
	public class PreserveWoodcuttingSkillValueLoad
	{
		public static bool Loading = false;
		private static void Prefix() => Loading = true;

		private static void Finalizer()
		{
			Loading = false;
			PreserveWoodcuttingSkillValueSave.woodCuttingToSave = ReturnDummyWoodcuttingSkill.lastSkill;
		}
	}

	[HarmonyPatch(typeof(Skills), nameof(Skills.Save))]
	public class PreserveWoodcuttingSkillValueSave
	{
		public static Skills.Skill? woodCuttingToSave = null;
		private static void Prefix(Skills __instance)
		{
			if (woodCuttingToSave is not null)
			{
				woodCuttingToSave.m_info.m_skill = Skills.SkillType.WoodCutting;
				__instance.m_skillData[Skills.SkillType.WoodCutting] = woodCuttingToSave;
			}
		}

		private static void Finalizer(Skills __instance) => __instance.m_skillData.Remove(Skills.SkillType.WoodCutting);
	}

	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	public class RemoveWoodcuttingAutocomplete
	{
		[UsedImplicitly]
		public static void Postfix()
		{
			void RemoveWoodcutting(Terminal.ConsoleCommand command)
			{
				Terminal.ConsoleOptionsFetcher fetcher = command.m_tabOptionsFetcher;
				command.m_tabOptionsFetcher = () =>
				{
					List<string> options = fetcher();
					options.Remove("WoodCutting");
					return options;
				};
			}

			RemoveWoodcutting(Terminal.commands["raiseskill"]);
			RemoveWoodcutting(Terminal.commands["resetskill"]);
		}
	}

	[HarmonyPatch(typeof(ImpactEffect), nameof(ImpactEffect.OnCollisionEnter))]
	public class SetTreeFlag
	{
		public static bool hitByTree = false;
		private static void Prefix(ImpactEffect __instance)
		{
			if (__instance.GetComponent<TreeLog>())
			{
				hitByTree = true;
			}
		}

		private static void Finalizer() => hitByTree = false;
	}

	[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
	public class ReduceDamageTakenFromTrees
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (SetTreeFlag.hitByTree && __instance is Player player)
			{
				hit.ApplyModifier(1 - player.m_nview.GetZDO().GetFloat("Lumberjacking Skill Factor") * (1 - damageFromTreesFactor.Value));
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	public class PlayerAwake
	{
		private static void Postfix(Player __instance)
		{
			__instance.m_nview.Register("Lumberjacking IncreaseSkill", (long _, int factor) => __instance.RaiseSkill("Lumberjacking", factor));
		}
	}
	
	[HarmonyPatch(typeof(Player), nameof(Player.Update))]
	public class PlayerUpdate
	{
		private static void Postfix(Player __instance)
		{
			if (__instance == Player.m_localPlayer)
			{
				__instance.m_nview.GetZDO().Set("Lumberjacking Skill Factor", __instance.GetSkillFactor("Lumberjacking"));
			}
		}
	}

	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
	private class IncreaseDamageDone
	{
		private static void Prefix(SEMan __instance, ref HitData hitData)
		{
			if (__instance.m_character is Player player)
			{
				hitData.m_damage.m_chop *= 1 + player.GetSkillFactor("Lumberjacking") * damageToTreesFactor.Value;
			}
		}
	}	

	[HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), typeof(int))]
	private class IncreaseItemYield
	{
		[UsedImplicitly]
		[HarmonyPriority(Priority.VeryLow)]
		private static void Postfix(ref List<GameObject> __result)
		{
			if (!SetLumberjackingFlagTreeLog.IsLumberjacking)
			{
				return;
			}

			List<GameObject> tmp = new();
			foreach (GameObject item in __result)
			{
				float amount = 1 + SetLumberjackingFlagTreeLog.LumberjackingFactor * (lumberjackingYieldFactor.Value - 1);

				for (int num = Mathf.FloorToInt(amount + Random.Range(0f, 1f)); num > 0; --num)
				{
					tmp.Add(item);
				}
			}

			__result = tmp;
		}
	}

	[HarmonyPatch(typeof(TreeLog), nameof(TreeLog.RPC_Damage))]
	public static class SetLumberjackingFlagTreeLog
	{
		public static bool IsLumberjacking = false;
		public static float LumberjackingFactor;

		private static void Prefix(TreeLog __instance, HitData hit)
		{
			IsLumberjacking = true;
			LumberjackingFactor = hit.GetAttacker()?.m_nview.GetZDO()?.GetFloat("Lumberjacking Skill Factor") ?? 0;

			if (hit.m_toolTier >= __instance.m_minToolTier && hit.m_damage.m_chop > 0 && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner() && hit.GetAttacker() is Player player)
			{
				player.m_nview.InvokeRPC("Lumberjacking IncreaseSkill", 1);
			}
		}

		private static void Finalizer() => IsLumberjacking = false;
	}
	
	[HarmonyPatch(typeof(TreeBase), nameof(TreeBase.RPC_Damage))]
	public static class SetLumberjackingFlagTreeBase
	{
		private static void Prefix(TreeBase __instance, HitData hit)
		{
			SetLumberjackingFlagTreeLog.IsLumberjacking = true;
			SetLumberjackingFlagTreeLog.LumberjackingFactor = hit.GetAttacker()?.m_nview.GetZDO()?.GetFloat("Lumberjacking Skill Factor") ?? 0;

			if (hit.m_toolTier >= __instance.m_minToolTier && hit.m_damage.m_chop > 0 && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner() && hit.GetAttacker() is Player player)
			{
				player.m_nview.InvokeRPC("Lumberjacking IncreaseSkill", 1);
			}
		}

		private static void Finalizer() => SetLumberjackingFlagTreeLog.IsLumberjacking = false;
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetRunSpeedFactor))]
	public static class IncreaseRunMovementSpeedInForest
	{
		private static void Postfix(Player __instance, ref float __result)
		{
			if (WorldGenerator.InForest(__instance.transform.position))
			{
				__result *= 1 + __instance.GetSkillFactor("Lumberjacking") * (forestMovementSpeedFactor.Value - 1);
			}
		}
	}
	
	[HarmonyPatch(typeof(Player), nameof(Player.GetJogSpeedFactor))]
	public static class IncreaseJogMovementSpeedInForest
	{
		private static void Postfix(Player __instance, ref float __result)
		{
			if (WorldGenerator.InForest(__instance.transform.position))
			{
				__result *= 1 + __instance.GetSkillFactor("Lumberjacking") * (forestMovementSpeedFactor.Value - 1);
			}
		}
	}
}
