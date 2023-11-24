using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Environments;
using CommandLine;
using Noggog;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using DynamicData.Kernel;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UDPatcher
{
    public class Program
    {
        public static Lazy<UDPatchSettings> _settings = null!;
        public static UDPatchSettings Settings => _settings.Value;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "UD_Patch.esp")
                .SetAutogeneratedSettings(
                            nickname: "Settings",
                            path: "settings.json",
                            out _settings,
                            true)
                .Run(args);
        }

        /// <summary>
        /// Using Settings, finds the UD Render script name from the input Inventory script name via simple match.
        /// Only the input script's name (<paramref name="zadName"/>) is considered.
        /// </summary>
        /// <param name="zadName">Name of the input Inventory script</param>
        /// <returns>Corresponding UD Render script</returns>
        private static string? GetUdScriptNameFromZad(string zadName)
        {
            foreach (var zadGroup in Settings.RenderScriptSettings.ScriptMatches)
            {
                if (zadGroup.Value.Contains(zadName))
                {
                    return zadGroup.Key;
                }
            }
            return null;
        }


        /// <summary>
        /// Using Settings, finds all extra rules which apply to a UD Render script with name <paramref name="udName"/>
        /// </summary>
        /// <param name="udName">UD Render script name</param>
        /// <returns>All applicable rules</returns>
        private static List<UDOtherSettings> GetOtherRulesFromUd(string udName)
        {
            return Settings.RenderScriptSettings.OtherMatches.FindAll(rule => rule.InputScripts.Contains(udName));
        }

        /// <inheritdoc cref="GetUdScriptNameFromOtherRule(UDOtherSettings, IArmorGetter)" path="//remarks | //returns"/>
        /// <summary>
        /// Applies <paramref name="kwRules"/> to given <paramref name="armorKeywords"/> until all have been applied 
        /// or the <paramref name="kwRules"/> are no longer 
        /// applicable to the given
        /// script (i.e. resulting script is no longer among <paramref name="inputScripts"/>)
        /// 
        /// </summary>
        /// <seealso cref="GetUdScriptNameFromKw(UDKwSettings, IEnumerable{IFormLinkGetter{IKeywordGetter}}?)">
        /// Refer to this method for details on how the rules are applied
        /// </seealso>
        /// <param name="kwRules">Rules to apply sequentially</param>
        /// <param name="inputScripts">Scripts which <paramref name="kwRules"/> apply to</param>
        /// <param name="armorKeywords">Armor's keywords to apply <paramref name="kwRules"/> to</param>
        /// <exception cref="Exception">Thrown when applying any rule fails</exception>
        private static string? GetUdScriptNameFromKws(List<UDKwSettings> kwRules, IEnumerable<string> inputScripts, 
            IEnumerable<IFormLinkGetter<IKeywordGetter>>? armorKeywords)
        {
            string? newName = null;
            var newNames = new SortedList<int, string>();
            foreach (var kwRule in kwRules) { 
                try
                {
                    newName = GetUdScriptNameFromKw(kwRule, armorKeywords);
                } catch (Exception ex)
                {
                    throw new Exception($"Failed on KW rule with output {kwRule.OutputScript}", ex);
                }
                if (newName != null)
                {
                    newNames[-kwRule.Priority] = newName;
                }
            }
            if (!newNames.Any())
            {
                return null;
            } else
            {
                return newNames.First().Value;
            } 
        }

        /// <inheritdoc cref="GetUdScriptNameFromOtherRule(UDOtherSettings, IArmorGetter)" path="//remarks | //returns"/>
        /// <summary>
        /// Using Settings, applies <paramref name="kwRule"/> to <paramref name="armorKeywords"/> by checking if any Keywords in 
        /// <paramref name="kwRule"/> are also in <paramref name="armorKeywords"/>.
        /// </summary>
        /// <param name="kwRule">Rule to apply</param>
        /// <param name="armorKeywords">Armor's keywords</param>
        /// <exception cref="Exception">Throws if <paramref name="kwRule"/> has no OutputScript</exception>
        private static string? GetUdScriptNameFromKw(UDKwSettings kwRule, IEnumerable<IFormLinkGetter<IKeywordGetter>>? armorKeywords)
        {
            if (kwRule.OutputScript == null)
            {
                throw new Exception("Output Script of Keyword Match not defined");
            }
            else if (armorKeywords == null)
            {
                return null;
            }
            else if (kwRule.Keywords.Intersect(armorKeywords).Any())
            {
                return kwRule.OutputScript;
            }
            else
            {
                return null;
            }
        }

        /// <inheritdoc cref="GetUdScriptNameFromOtherRule(UDOtherSettings, IArmorGetter)" path="//remarks | //returns"/>
        /// <summary>
        /// Using Settings, applies <paramref name="nameRule"/> to <paramref name="armorName"/> by checking if the
        /// Search Text regular expression
        /// matches <paramref name="armorName"/>
        /// </summary>
        /// <param name="nameRule">Rule to apply</param>
        /// <param name="armorName">EditorID of Armor</param>
        private static string? GetUdScriptNameFromSearchRule(UDNameSearchSettings nameRule, string armorName)
        {
            if (Regex.IsMatch(armorName, nameRule.SearchText, RegexOptions.IgnoreCase))
            {
                return nameRule.OutputScript;              
            }
            else
            {
                return null;
            }
        }

        /// <inheritdoc cref="GetUdScriptNameFromOtherRule(UDOtherSettings, IArmorGetter)" path="//remarks | //returns"/>
        /// <summary>
        /// Using Settings, applies <paramref name="nameRules"/> to <paramref name="armorName"/> until all have been applied,
        /// or the rules no longer apply to a resulting script (i.e. it's not among <paramref name="inputScripts"/>)
        /// </summary>
        /// <seealso cref="GetUdScriptNameFromSearchRule(UDNameSearchSettings, string)">Refer to 
        /// this method for details on how each rule is applied</seealso>
        /// <param name="nameRules">Name Rules to apply</param>
        /// <param name="inputScripts">UD Render scripts to which these rules apply</param>
        /// <param name="armorName">Armor's EditorID</param>
        /// <exception cref="Exception">Throws if it catches from applying a rule</exception>
        private static string? GetUdScriptNameFromSearchRules(IEnumerable<UDNameSearchSettings> nameRules, IEnumerable<string> inputScripts, string armorName)
        {
            string? newUdName = null;
            var newNames = new SortedList<int, string>();
            foreach (var rule in nameRules)
            {
                try
                {
                    newUdName = GetUdScriptNameFromSearchRule(rule, armorName);
                } catch (Exception ex)
                {
                    throw new Exception($"Failed on word '{rule.SearchText}'", ex);
                }
                if (newUdName != null)
                {
                    newNames[-rule.Priority] = newUdName;
                }
            }
            if (!newNames.Any())
            {
                return null;
            } else
            {
                return newNames.First().Value;
            }
        }

        /// <inheritdoc cref="GetUdScriptNameFromOtherRules(string, IArmorGetter)" path="//remarks"/>
        /// <summary>
        /// Applies all sub-rules of <paramref name="otherRule"/> to <paramref name="armor"/>
        /// </summary>
        /// <param name="otherRule">Rule to apply</param>
        /// <param name="armor">Armor to apply <paramref name="otherRule"/> to</param>
        /// <returns>New UD Render script name, or <c>null</c> if no rules apply</returns>
        /// <exception cref="Exception">Throws if <paramref name="otherRule"/> has no input scripts</exception>
        /// <exception cref="Exception">Throws if any sub-rules throw</exception>
        private static string? GetUdScriptNameFromOtherRule(UDOtherSettings otherRule, IArmorGetter armor)
        {
            var inputScripts = otherRule.InputScripts;
            if (inputScripts == null || !inputScripts.Any())
            {
                throw new Exception("No Input Scripts found");
            }
            string? newUdName;
            try
            {
                newUdName = GetUdScriptNameFromKws(otherRule.KeywordMatch, inputScripts, armor.Keywords);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to match Keywords", e);
            }
            if (armor.EditorID == null ||
                (newUdName != null && !inputScripts.Contains(newUdName)))
            {
                return newUdName;
            }
            try
            {
                var newSearchUdName = GetUdScriptNameFromSearchRules(otherRule.NameMatch, inputScripts,
                    armor.EditorID);
                return newSearchUdName ?? newUdName;
            } catch (Exception e)
            {
                throw new Exception("Failed to match Search rule", e);
            }
        }

        /// <summary>
        /// Finds all <see cref="UDOtherSettings"/> which apply to <paramref name="udName"/>, applies 
        /// them sequentially to <paramref name="armor"/> until all have been applied, or the UD
        /// Render script name has changed
        /// </summary>
        /// <remarks>
        /// Uses <see cref="Settings"/>
        /// </remarks>
        /// <param name="udName">Initial name of UD Render script</param>
        /// <param name="armor">Armor to apply rules to</param>
        /// <returns>New UD Render script name, or <paramref name="udName"/> if no rules applied</returns>
        /// <exception cref="Exception">Throws if catches exception from applying rule</exception>
        private static string GetUdScriptNameFromOtherRules(string udName, IArmorGetter armor)
        {
            List<UDOtherSettings> otherRules = GetOtherRulesFromUd(udName);
            string? newUdName;
            foreach (var otherRule in otherRules) {
                try
                {
                    newUdName = GetUdScriptNameFromOtherRule(otherRule, armor);
                } catch(Exception e)
                {
                    throw new Exception("Failed to apply Other Rules", e);
                }
                if (newUdName != null && newUdName != udName)
                {
                    return newUdName;
                }
            }
            return udName;
        }

        /// <summary>
        /// Finds a UD Render script name which corresponds to <paramref name="armor"/> and
        /// <paramref name="zadName"/> after applying all corresponding rules as defined in
        /// <see cref="Settings"/>
        /// </summary>
        /// <seealso cref="GetUdScriptNameFromZad(string)">
        /// Refer to this method for how the initial UD Render script name is determined
        /// from <paramref name="zadName"/>
        /// </seealso>
        /// <seealso cref="GetUdScriptNameFromOtherRules(string, IArmorGetter)">
        /// Refer to this method for how the final UD Render script name is determined from
        /// the initial UD Render script
        /// </seealso>
        /// <param name="armor">Armor to apply rules to</param>
        /// <param name="zadName">Initial script name ot apply rules to</param>
        /// <returns>UD Render script name, or <c>null</c> if none could be found</returns>
        /// <exception cref="Exception">Thrown if rules end up looping</exception>
        public static string? GetUdScriptNameFromArmor(IArmorGetter armor, string zadName)
        {
            var udName = GetUdScriptNameFromZad(zadName);
            if (udName == null)
            {
                Console.WriteLine($"Could not find direct UD match for script {zadName} of " +
                    $"Armor {armor}");
                return null;
            }
            var loopedNames = new HashSet<string>() { udName };
            string newUdName = udName;
            string prevNewUdName = string.Empty;
            while (prevNewUdName != newUdName)
            {
                prevNewUdName = newUdName;
                newUdName = GetUdScriptNameFromOtherRules(newUdName, armor);
                if (prevNewUdName != newUdName && loopedNames.Contains(newUdName))
                {
                    throw new Exception($"Found looping rule for Armor {armor} (from Script " +
                        $"{prevNewUdName} to Script {newUdName}");
                }
                loopedNames.Add(newUdName);
            }
            return newUdName;
        }

        /// <summary>
        /// Checks if an <see cref="IScriptEntryGetter"/> in <paramref name="armorScripts"/> is
        /// present in <paramref name="searchScripts"/>
        /// </summary>
        /// <param name="armorScripts">Scripts to search in</param>
        /// <param name="searchScripts">Scripts to search by</param>
        /// <returns>A script in both <paramref name="armorScripts"/> and <paramref name="searchScripts"/></returns>
        public static IScriptEntryGetter? FindArmorScript(IEnumerable<IScriptEntryGetter> armorScripts,
            IEnumerable<string> searchScripts)
        {
            foreach (var script in armorScripts)
            {
                if (searchScripts.Contains(script.Name)) return script;
            }
            return null;
        }

        /// <summary>
        /// Searches through all <see cref="Settings"/> to find all UD Render script names
        /// </summary>
        /// <returns>All UD Render script names</returns>
        public static HashSet<string> GetAllUdScriptNamesFromSettings()
        {
            var renderSettings = Settings.RenderScriptSettings;
            var allNames = new HashSet<string>(renderSettings.ScriptMatches.Keys);
            foreach(var otherRule in renderSettings.OtherMatches)
            {
                allNames.UnionWith(otherRule.InputScripts);
            }
            return allNames;
        }


        /// <summary>
        /// Finds all Zad script names in <paramref name="otherSettings"/>
        /// (based on <see cref="UDOtherSetting.OutputScript"/>)
        /// </summary>
        /// <param name="otherSettings">The settings to search through</param>
        /// <returns>All Zad script names found in <paramref name="otherSettings"/></returns>
        public static HashSet<string> GetZadNamesFromRules(IEnumerable<UDOtherSetting> otherSettings)
        {
            return otherSettings.Select(setting => setting.OutputScript).ToHashSet();
        }

        /// <summary>
        /// Finds all Zad script names in <see cref="Settings"/>
        /// </summary>
        /// <seealso cref="GetZadNamesFromRules(IEnumerable{UDOtherSetting})">
        /// Refer to this for details on how they are extracted from each set of rules
        /// </seealso>
        /// <returns>All Zad script names in all settings</returns>
        public static HashSet<string> GetAllZadScriptNamesFromSettings()
        {
            var renderSettings = Settings.RenderScriptSettings;
            var allNames = new HashSet<string>();
            foreach(var zadMatches in renderSettings.ScriptMatches.Values)
            {
                allNames.UnionWith(zadMatches);
            }
            foreach(var otherRule in renderSettings.OtherMatches)
            {
                var kwMatches = otherRule.KeywordMatch.Select(match => (UDOtherSetting)match);
                var nameMatches = otherRule.NameMatch.Select(match => (UDOtherSetting)match);
                allNames.UnionWith(GetZadNamesFromRules(kwMatches));
                allNames.UnionWith(GetZadNamesFromRules(nameMatches));
            }
            return allNames;
        }

        /// <summary>
        /// Copies an Inventory script (<paramref name="original"/>) into a new Render script
        /// with appropriate properties
        /// </summary>
        /// <remarks>
        /// These properties are found in <seealso cref="Settings.RenderScriptSettings.ScriptValues"/>
        /// </remarks>
        /// <param name="original">The inventory script to copy</param>
        /// <returns>A new script appropriate for use as a Render script</returns>
        public static ScriptEntry CopyInvScriptToRender(IScriptEntryGetter original)
        {
            var propSettings = Settings.RenderScriptSettings.ScriptValues;
            var validPropNames = new HashSet<string>(propSettings.Keys);// { "deviceInventory", "libs", "zad_DeviousDevice" };
            Dictionary<string, string> replacementPropNames = propSettings.Where(setting => setting.Value != null).ToDictionary()!; 
            //{            { "zad_DeviousDevice", "UD_DeviceKeyword"}};
            var newScript = original.DeepCopy();
            newScript.Properties.RemoveWhere(prop => !validPropNames.Contains(prop.Name));
            foreach (var prop in newScript.Properties)
            {
                if (replacementPropNames.TryGetValue(prop.Name, out var newName))
                {
                    prop.Name = newName;
                }
            }
            return newScript;
        }

        /// <summary>
        /// Gets corresponding UD Inventory script name from Zad Inventory script
        /// name (<paramref name="zadInvName"/>)
        /// </summary>
        /// <remarks>
        /// Uses <see cref="Settings"/> to determine matches
        /// </remarks>
        /// <param name="zadInvName">Zad Inventory script name</param>
        /// <returns>Corresponding UD Inventory script name, or <c>null</c> if none found</returns>
        public static string? GetUDInvFromZadInv(string zadInvName)
        {
            foreach (var rule in Settings.InventoryScriptSettings.ScriptMatches)
            {
                if (rule.Value.Contains(zadInvName))
                {
                    return rule.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Un-smart record getter for quickly grabbing "constants." Designed
        /// to throw exception upon failure.
        /// </summary>
        /// <typeparam name="T">Getter type</typeparam>
        /// <param name="linkCache">pre-defined Link Cache</param>
        /// <param name="mod">mod to search through</param>
        /// <param name="formId">raw FormID</param>
        /// <returns>The record in question</returns>
        public static T DumbRecordGetter<T> (ILinkCache linkCache, ModKey mod, uint formId)
            where T : ISkyrimMajorRecordGetter
        {
            return linkCache.Resolve(new FormKey(mod, formId), typeof(T)).Cast<T>();
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var UDScripts = GetAllUdScriptNamesFromSettings();
            var zadScripts = GetAllZadScriptNamesFromSettings();

            const string DDI_NAME = "Devious Devices - Integration.esm";
            ModKey ddiMod = ModKey.FromFileName(DDI_NAME);

            const string UD_NAME = "UnforgivingDevices.esp";
            ModKey udMod = ModKey.FromFileName(UD_NAME);

            var shortenedLoadOrder = state.LoadOrder.PriorityOrder.Where(
                mod =>
                Settings.ModsToPatch.Contains(mod.ModKey)
                );
            Console.WriteLine($"Our mods: {string.Join(", ", shortenedLoadOrder)}");
            var shortenedLoadOrderFuller = state.LoadOrder.ListedOrder.Where(mod =>
                Settings.ModsToPatch.Contains(mod.ModKey) || mod.ModKey == ddiMod || mod.ModKey == udMod
                );
            var idLinkCache = shortenedLoadOrderFuller.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>(LinkCachePreferences.Default);

            var consts = new UDImportantConstantsFound(Settings.IMPORTANTCONSTANTS, idLinkCache);

            void addKeywords(Armor armor)
            {
                var keywords = new ExtendedList<IKeywordGetter>() { consts.udKw!, consts.udPatchKw! };
                if (Settings.UseModes)
                {
                    keywords.Add(consts.udPatchNoModeKw!);
                }
                if (armor.Keywords == null)
                {
                    armor.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                }
                foreach (var keyword in keywords)
                {
                    var kwLink = keyword.ToLinkGetter();
                    if (!armor.Keywords.Contains(kwLink))
                    {
                        armor.Keywords.Add(kwLink);
                    }
                }
            }
            int totalPatched = 0;
            int newDevices = 0;
            foreach (IArmorGetter invArmorGetter in shortenedLoadOrder.Armor().WinningOverrides())
            {
                if (invArmorGetter.Keywords == null)
                {
                    continue;
                } else if (invArmorGetter.VirtualMachineAdapter == null || invArmorGetter.VirtualMachineAdapter.Scripts == null)
                {
                    continue;
                }
                if (invArmorGetter.Keywords.Contains(consts.zadInvKeyword!))
                {
                    // find the script the armour's using
                    var invCurrentScripts = invArmorGetter.VirtualMachineAdapter.Scripts;
                    var invUDScript = FindArmorScript(invCurrentScripts, UDScripts);
                    var invZadScript = FindArmorScript(invCurrentScripts, zadScripts);
                    var invFinalScript = invZadScript != null ? invZadScript : invUDScript;
                    if (invFinalScript == null)
                    {
                        Console.WriteLine("penigs");
                        continue;
                    }
                    var renderDevice = invFinalScript
                        .Properties
                        .Where(prop => prop.Name == "deviceRendered")
                        .FirstOrDefault()!
                        .Cast<IScriptObjectPropertyGetter>()
                        .Object
                        .Cast<IArmorGetter>();
                    IArmorGetter renderArmor;
                    if (renderDevice.TryResolveContext<ISkyrimMod, ISkyrimModGetter, IArmor, IArmorGetter>(idLinkCache, out var foundArmor))
                    {
                        renderArmor = foundArmor.Record;
                        Console.WriteLine($"using {foundArmor.Record.EditorID} found in {foundArmor.ModKey}");
                    } else
                    {
                        Console.WriteLine($"Invalid render target {renderDevice.FormKey} for inventory item {invArmorGetter.EditorID} ({invArmorGetter.FormKey})");
                        continue;
                    }
                    IScriptEntryGetter? renderUDScript = null;
                    var renderArmorOverride = state.PatchMod.Armors.GetOrAddAsOverride(renderArmor);
                    if (renderArmorOverride == null)
                    {
                        Console.WriteLine("video peningns");
                        continue;
                    }
                    if (renderArmorOverride.Keywords == null)
                    {
                        renderArmorOverride.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                    }
                    if (renderArmorOverride.VirtualMachineAdapter == null)
                    {
                        renderArmorOverride.VirtualMachineAdapter = new VirtualMachineAdapter();
                    } else
                    {
                        renderUDScript = FindArmorScript(renderArmorOverride.VirtualMachineAdapter!.Scripts, UDScripts);
                    }
                    if (invUDScript == null)
                    {
                        var invArmorOverride = state.PatchMod.Armors.GetOrAddAsOverride(invArmorGetter);
                        if (invArmorOverride.VirtualMachineAdapter == null)
                        {
                            throw new Exception("wtf???");
                        }
                        if (invArmorOverride.Keywords == null)
                        {
                            invArmorOverride.Keywords = new();
                        }
                        invArmorOverride.Keywords.Add(consts.udInvKeyword!);
                        var invScript = invArmorOverride.VirtualMachineAdapter.Scripts.Where(script => script.Name == invFinalScript.Name).Single();
                        
                        var UDCDProp = new ScriptObjectProperty();
                        UDCDProp.Name = "UDCDmain";
                        UDCDProp.Flags = ScriptProperty.Flag.Edited;
                        UDCDProp.Object = consts.udMainQst!.ToLink();

                        var newInvScriptName = GetUDInvFromZadInv(invFinalScript.Name);
                        if (newInvScriptName == null)
                        {
                            Console.WriteLine($"Could not find UD Inventory Script corresponding to {invFinalScript}");
                            continue;
                        }
                        invScript.Name = newInvScriptName;
                        invScript.Properties.Add(UDCDProp);

                        var newRenderScriptName = GetUdScriptNameFromArmor(renderArmorOverride, invFinalScript.Name);
                        if (newRenderScriptName == null)
                        {
                            Console.WriteLine($"Unable to find corresponding renderScript for {invFinalScript.Name} ({renderArmor})");
                            continue;
                        }
                        var newRenderScript = CopyInvScriptToRender(invFinalScript);
                        newRenderScript.Name = newRenderScriptName;

                        if (renderUDScript == null)
                        {
                            renderArmorOverride.VirtualMachineAdapter.Scripts.Add(newRenderScript);
                            addKeywords(renderArmorOverride);
                            Console.WriteLine($"---Device {renderArmorOverride} patched!");
                            totalPatched++;
                        } else
                        {
                            Console.WriteLine($"WARNING: Render device {renderArmorOverride} already has UD script! Creating new render device!");
                            newDevices++;
                            var newRenderArmor = state.PatchMod.Armors.DuplicateInAsNewRecord(renderArmorOverride);
                            newRenderArmor.EditorID = newRenderArmor.EditorID + "_AddedRenderDevice";
                            var newRenderArmorScripts = newRenderArmor.VirtualMachineAdapter!.Scripts;
                            newRenderArmorScripts[newRenderArmorScripts.FindIndex(script => script.Name == renderUDScript.Name)] = newRenderScript;
                            invScript.Properties[invScript.Properties.FindIndex(prop => prop.Name == "deviceRendered")].Cast<ScriptObjectProperty>().Object = newRenderArmor.ToLink();
                            Console.WriteLine($"------NEW DEVICE {newRenderArmor} CREATED!------");
                        }
                    } else if (renderUDScript == null)
                    {
                        Console.WriteLine($"Device with patched INV but not patched REND detected. Patching renderDevice {renderArmor}.");
                        var newRenderScriptName = GetUdScriptNameFromArmor(renderArmorOverride, "zadequipscript");
                        if (newRenderScriptName == null)
                        {
                            continue;
                        }
                        var newRenderScript = CopyInvScriptToRender(invFinalScript);
                        newRenderScript.Name = newRenderScriptName;
                        renderArmorOverride.VirtualMachineAdapter.Scripts.Add(newRenderScript);
                        addKeywords(renderArmorOverride);
                        Console.WriteLine($"Repatched RenderDevice {renderArmor} of InventoryDevice {invArmorGetter}");
                    }
                }
            }
        }
    }
}
