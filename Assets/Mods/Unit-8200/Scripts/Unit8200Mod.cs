#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AI.Customers.CustomerEntries;
using BAModAPI;
using BigAmbitions.Rivals;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using Buildings;
using Buildings.BuildingTypes.Shared.BusinessRequirement;
using Entities;
using Helpers;
using JimmysUnityUtilities;
using Localizor;
using UnityEngine;
using UnityEngine.UI.Extensions.HelpSystem;

[assembly: RegisterModClass(typeof(Unit8200Mod))]
[assembly: RegisterModClass(typeof(Unit8200CityMod))]

[ModEntryOnInitializationLoad]
public sealed class Unit8200Mod : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        Unit8200Content.Start();
        Unit8200SkillKit.RegisterWhenReady();
        Unit8200Help.Start();
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        Unit8200Help.Stop();
        Unit8200SkillKit.Unregister();
        Unit8200Content.Stop();
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class Unit8200CityMod : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        Unit8200Content.EnsureRegistered();
        Unit8200Content.RefreshMarketAndBusinesses();
        Unit8200SkillKit.BindIntoWorld();
        Unit8200Help.EnsureRegistered();
        Unit8200InfrastructureBilling.Start();
        Unit8200Rivalry.Start();
        Unit8200LegendaryCandidates.Start();
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        Unit8200LegendaryCandidates.Stop();
        Unit8200Rivalry.Stop();
        Unit8200InfrastructureBilling.Stop();
        Unit8200SkillKit.Unbind();
        return Task.CompletedTask;
    }
}

internal static class Unit8200Ids
{
    internal const string BusinessType = "unit-8200:businesstype_osintservice";
    internal const string HackerSkill = "unit-8200:skill_hacker";
    internal const string HourlyFee = "unit-8200:itemname_hourlyosintfee";
    internal const string ServerRequirement = "unit-8200:businessrequirement_serverinfrastructure";
    internal const string InfrastructureTransaction = "unit-8200:transaction_serverinfrastructure";

    internal const string WebDevelopmentBusiness = "ba:businesstype_webdevelopmentagency";
    internal const string ProgrammerSkill = "ba:skill_programmer";
    internal const string ProgrammerFee = "ba:itemname_hourlyprogrammerfee";

    internal const string BladeServer = "it-services:itemname_bladeserver";
    internal const string RackServer = "it-services:itemname_rackserver";
    internal const string LegacyRackServer = "it-services:itemname_serverrack";
    internal const string MainframeServer = "it-services:itemname_mainframeserver";

    internal static readonly string[] ServerItems =
    {
        BladeServer,
        RackServer,
        LegacyRackServer,
        MainframeServer,
    };
}

internal static class Unit8200Content
{
    private const float DefaultHourlyFee = 300f;
    private static bool _enabled;
    private static Item? _fee;
    private static BusinessType? _business;
    private static SpecificItemsInBuildingBySqm? _serverRequirement;

    internal static void Start()
    {
        _enabled = true;
        EnsureRegistered();
        GlobalEvents.RegisterOnGameLoadedCallback(EnsureRegistered);
    }

    internal static void Stop()
    {
        _enabled = false;

        if (_business != null)
            ModdingAPI.UnregisterModBusinessType(_business);
        if (_fee != null)
            ItemsGetter.UnregisterModItem(_fee.itemName);

        if (_business != null)
            UnityEngine.Object.Destroy(_business);
        if (_fee != null)
            UnityEngine.Object.Destroy(_fee);
        if (_serverRequirement != null)
            UnityEngine.Object.Destroy(_serverRequirement);

        _business = null;
        _fee = null;
        _serverRequirement = null;
    }

    internal static void EnsureRegistered()
    {
        if (!_enabled || _business != null)
            return;

        var feeTemplate = ItemsGetter.GetByName(Unit8200Ids.ProgrammerFee, true);
        var businessTemplate = BusinessTypeHelper.GetData(Unit8200Ids.WebDevelopmentBusiness);
        if (feeTemplate == null || businessTemplate == null)
        {
            Debug.LogWarning("[Unit-8200] Vanilla office templates are not ready. Registration will retry when the game loads.");
            return;
        }

        if (!Unit8200SkillKit.IsAvailable)
        {
            Debug.LogError("[Unit-8200] SkillKit is required; OSINT Service registration is deferred.");
            return;
        }

        var availableServers = Unit8200Ids.ServerItems
            .Where(itemName => ItemsGetter.GetByName(itemName, true) != null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (availableServers.Length == 0)
        {
            Debug.LogError("[Unit-8200] IT Business Expansion is required: no supported server item was found.");
            return;
        }

        _fee = UnityEngine.Object.Instantiate(feeTemplate);
        _fee.name = "HourlyOSINTServiceFee";
        _fee.itemName = Unit8200Ids.HourlyFee;
        SetPrivateFloat(_fee, "defaultMarketPrice", DefaultHourlyFee);
        ItemsGetter.RegisterModItem(_fee);
        ItemHelper.ClearPriceCaches();

        _serverRequirement = CreateServerRequirement(
            Unit8200Ids.ServerItems.Distinct(StringComparer.Ordinal).ToArray());
        _business = UnityEngine.Object.Instantiate(businessTemplate);
        _business.name = "OSINTService";
        _business.businessTypeName = Unit8200Ids.BusinessType;
        _business.spawnCustomers = false;
        _business.businessProducts = new[]
        {
            new BusinessProduct { itemName = Unit8200Ids.HourlyFee, impact = 1f },
        };
        _business.productSources = Array.Empty<string>();
        _business.employeePrimarySkills = new[] { Unit8200Ids.HackerSkill };
        _business.aliases = new[] { "OSINT", "Cybersecurity", "Threat Intelligence" };
        _business.businessRequirements = businessTemplate.businessRequirements != null
            ? new List<BusinessRequirement>(businessTemplate.businessRequirements)
            : new List<BusinessRequirement>();
        _business.businessRequirements.Add(_serverRequirement);

        if (!ModdingAPI.RegisterModBusinessType(_business))
        {
            Debug.LogError("[Unit-8200] Could not register the OSINT Service business type.");
            ItemsGetter.UnregisterModItem(_fee.itemName);
            UnityEngine.Object.Destroy(_business);
            UnityEngine.Object.Destroy(_fee);
            UnityEngine.Object.Destroy(_serverRequirement);
            _business = null;
            _fee = null;
            _serverRequirement = null;
            return;
        }

        Debug.Log("[Unit-8200] Registered OSINT Service, Hacker work, and server infrastructure requirements.");
    }

    internal static void RefreshMarketAndBusinesses()
    {
        if (_business == null)
            return;

        ProductMarketHelper.UpdateMarketDemand(Unit8200Ids.HourlyFee, null, null);
        ProductMarketHelper.FillProvidersDictionary(false);
        ProductMarketHelper.UpdateMarketDemands(null);

        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return;

        foreach (var registration in current.BuildingRegistrations)
        {
            if (registration == null || !registration.RentedByPlayer ||
                !string.Equals(registration.businessTypeName, Unit8200Ids.BusinessType, StringComparison.Ordinal))
                continue;

            BusinessHelper.UpdateCustomerCapacity(registration);
            registration.cachedAvailableProducts ??= new List<string>();
            if (!registration.cachedAvailableProducts.Contains(Unit8200Ids.HourlyFee))
                registration.cachedAvailableProducts.Add(Unit8200Ids.HourlyFee);
            CustomerEntriesHelper.UpdateCustomerEntriesForPlayerBusiness(registration, TimeHelper.GetDayOfWeek());
        }
    }

    private static SpecificItemsInBuildingBySqm CreateServerRequirement(string[] serverItems)
    {
        var requirement = ScriptableObject.CreateInstance<SpecificItemsInBuildingBySqm>();
        requirement.name = "OSINTServerInfrastructureRequirement";
        requirement.businessRequirementName = Unit8200Ids.ServerRequirement;
        requirement.squareMetersPerItem = 75;
        requirement.maxItems = 20;
        ((SpecificItemsInBuilding)requirement).items = serverItems;
        SetRequirementField(requirement, "todoTaskItemName", serverItems[0]);
        SetRequirementField(requirement, "helpLink", Unit8200Help.HelpSlug);
        requirement.hideFlags = HideFlags.HideAndDontSave;
        return requirement;
    }

    private static void SetPrivateFloat(Item item, string fieldName, float value)
    {
        typeof(Item).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(item, value);
    }

    private static void SetRequirementField(BusinessRequirement requirement, string fieldName, string value)
    {
        typeof(BusinessRequirement)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(requirement, value);
    }
}

internal static class Unit8200SkillKit
{
    private const string HooksTypeName = "SkillKit.SkillHooks, SkillKit";
    private const string DefinitionTypeName = "SkillKit.SkillDefinition, SkillKit";

    private static bool _queued;
    private static bool _registered;
    private static bool _bindRequested;
    private static bool _missingLogged;

    internal static bool IsAvailable => Type.GetType(HooksTypeName, throwOnError: false) != null;

    internal static void RegisterWhenReady()
    {
        if (_queued || _registered)
            return;

        try
        {
            var hooks = Type.GetType(HooksTypeName, throwOnError: false);
            var runWhenReady = hooks?.GetMethod("RunWhenReady", BindingFlags.Public | BindingFlags.Static);
            if (runWhenReady == null)
            {
                LogMissingOnce();
                return;
            }

            _queued = true;
            runWhenReady.Invoke(null, new object[] { (Action)OnReady });
        }
        catch (Exception exception)
        {
            _queued = false;
            Debug.LogWarning($"[Unit-8200] SkillKit is not ready ({exception.GetType().Name}); retrying on city load.");
        }
    }

    internal static void BindIntoWorld()
    {
        _bindRequested = true;
        RegisterWhenReady();
        if (_registered)
            InvokeHook("BindIntoWorld");
    }

    internal static void Unbind()
    {
        _bindRequested = false;
        if (_registered)
            InvokeHook("UnbindSkill", Unit8200Ids.HackerSkill);
    }

    internal static void Unregister()
    {
        _queued = false;
        _bindRequested = false;
        if (!_registered)
            return;

        InvokeHook("UnbindSkill", Unit8200Ids.HackerSkill);
        InvokeHook("Unregister", Unit8200Ids.HackerSkill);
        _registered = false;
    }

    private static void OnReady()
    {
        _queued = false;
        if (_registered)
            return;

        try
        {
            var hooks = Type.GetType(HooksTypeName, throwOnError: true)!;
            var definitionType = Type.GetType(DefinitionTypeName, throwOnError: true)!;
            var definition = Activator.CreateInstance(definitionType)!;

            SetField(definitionType, definition, "SkillName", Unit8200Ids.HackerSkill);
            SetField(definitionType, definition, "DisplayName", "Hacker");
            SetField(definitionType, definition, "CloneFromSkill", Unit8200Ids.ProgrammerSkill);
            SetField(definitionType, definition, "WorkstationCloneFrom", Unit8200Ids.ProgrammerSkill);
            SetField(definitionType, definition, "CloneWageMultiplier", 1.20f);
            SetField(definitionType, definition, "CloneTrainingCostMultiplier", 1.15f);
            SetField(definitionType, definition, "Businesses", new List<string> { Unit8200Ids.BusinessType });
            SetField(definitionType, definition, "SkipVanillaBusinessInherit", true);

            var register = hooks.GetMethod(
                "Register", BindingFlags.Public | BindingFlags.Static, null, new[] { definitionType }, null)
                ?? throw new MissingMethodException("SkillKit.SkillHooks.Register");
            register.Invoke(null, new[] { definition });
            _registered = true;

            Unit8200Content.EnsureRegistered();
            Unit8200Content.RefreshMarketAndBusinesses();

            if (_bindRequested)
                InvokeHook("BindIntoWorld");

            Debug.Log("[Unit-8200] Registered Hacker skill through SkillKit.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Unit-8200] Hacker skill registration failed: {exception}");
        }
    }

    private static void InvokeHook(string methodName, params object[] arguments)
    {
        try
        {
            Type.GetType(HooksTypeName, throwOnError: false)
                ?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, arguments);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Unit-8200] SkillKit call '{methodName}' failed: {exception.GetType().Name}.");
        }
    }

    private static void SetField(Type type, object instance, string fieldName, object value)
    {
        type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.SetValue(instance, value);
    }

    private static void LogMissingOnce()
    {
        if (_missingLogged)
            return;
        _missingLogged = true;
        Debug.LogError("[Unit-8200] SkillKit is required to hire Hacker employees.");
    }
}

internal static class Unit8200InfrastructureBilling
{
    private static readonly Dictionary<Address, int> LastBilledDay = new();
    private static bool _started;

    internal static void Start()
    {
        if (_started)
            return;
        _started = true;
        GlobalEvents.onNewDay += BillAll;
    }

    internal static void Stop()
    {
        if (!_started)
            return;
        _started = false;
        GlobalEvents.onNewDay -= BillAll;
        LastBilledDay.Clear();
    }

    private static void BillAll()
    {
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return;

        foreach (var registration in current.BuildingRegistrations)
            Bill(registration, current.Day);
    }

    private static void Bill(BuildingRegistration? registration, int day)
    {
        if (registration == null || !registration.RentedByPlayer ||
            !string.Equals(registration.businessTypeName, Unit8200Ids.BusinessType, StringComparison.Ordinal) ||
            registration.itemInstances == null ||
            LastBilledDay.TryGetValue(registration.Address, out var billedDay) && billedDay == day)
            return;

        var cost = 0f;
        var serverCount = 0;
        foreach (var item in registration.itemInstances.Values)
        {
            if (item == null)
                continue;
            var itemCost = DailyCost(item.itemName);
            if (itemCost <= 0f)
                continue;
            cost += itemCost;
            serverCount++;
        }

        if (cost <= 0f)
            return;

        var data = new Dictionary<string, string>
        {
            { "businessName", registration.BusinessName },
            { "serverCount", serverCount.ToString() },
        };
        var transaction = new TransactionInfo(Unit8200Ids.InfrastructureTransaction, data, false);
        if (GameManager.ChangeMoneySafe(-cost, transaction, null, registration.Address, false, true))
            LastBilledDay[registration.Address] = day;
    }

    private static float DailyCost(string itemName)
    {
        return itemName switch
        {
            Unit8200Ids.BladeServer => 40f,
            Unit8200Ids.RackServer => 80f,
            Unit8200Ids.LegacyRackServer => 80f,
            Unit8200Ids.MainframeServer => 160f,
            _ => 0f,
        };
    }
}

internal static class Unit8200Rivalry
{
    private const int PoachIntervalDays = 7;
    private const int PoachDeadlineDays = 3;
    private const int PoachRaisePercentage = 25;
    private static readonly Dictionary<Address, int> LastAttemptDay = new();
    private static bool _started;

    internal static void Start()
    {
        if (_started)
            return;
        _started = true;
        GlobalEvents.onNewDay += TryRivalPoach;
    }

    internal static void Stop()
    {
        if (!_started)
            return;
        _started = false;
        GlobalEvents.onNewDay -= TryRivalPoach;
        LastAttemptDay.Clear();
    }

    private static void TryRivalPoach()
    {
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null || current.Day < PoachIntervalDays || current.Day % PoachIntervalDays != 0)
            return;

        try
        {
            foreach (var registration in current.BuildingRegistrations.Where(IsPlayerOsintBusiness))
            {
                if (LastAttemptDay.TryGetValue(registration.Address, out var lastDay) && lastDay == current.Day)
                    continue;
                LastAttemptDay[registration.Address] = current.Day;

                var hackers = EmployeeHelper.GetEmployeeInstances()
                    .Where(employee => employee != null && !employee.IsCandidate && employee.IsPoachable &&
                        employee.assignedAddress.Equals(registration.Address) && HasHackerSkill(employee))
                    .OrderBy(employee => employee.satisfaction)
                    .ToList();
                if (hackers.Count == 0)
                    continue;

                var averageSatisfaction = hackers.Average(employee => employee.satisfaction);
                if (!ShouldAttemptPoach(averageSatisfaction, UnityEngine.Random.value))
                    continue;

                var rivalId = RivalsHelper.GetSpecialRivalByNeighborhood(registration.Neighborhood)?.rivalData?.id;
                if (string.IsNullOrEmpty(rivalId))
                    rivalId = RivalsHelper.GetRandomSpecialRivalId(true, true);
                if (string.IsNullOrEmpty(rivalId))
                    continue;

                hackers[0].PoachByRival(rivalId, PoachDeadlineDays, PoachRaisePercentage);
                Debug.Log($"[Unit-8200] A rival attempted to poach {hackers[0].characterData?.name} from {registration.BusinessName}.");
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Unit-8200] Rival poaching event skipped: {exception.GetType().Name}.");
        }
    }

    internal static bool ShouldAttemptPoach(float averageSatisfaction, float randomValue)
    {
        var chance = Mathf.Lerp(0.35f, 0.10f, Mathf.Clamp01(averageSatisfaction / 100f));
        return randomValue < chance;
    }

    private static bool IsPlayerOsintBusiness(BuildingRegistration registration) =>
        registration != null && registration.RentedByPlayer &&
        string.Equals(registration.businessTypeName, Unit8200Ids.BusinessType, StringComparison.Ordinal);

    internal static bool HasHackerSkill(EmployeeInstance employee) =>
        employee.HasSkill(Unit8200Ids.HackerSkill);
}

internal static class Unit8200LegendaryCandidates
{
    private const float LegendaryChance = 0.01f;
    private const float MinimumHackerSkill = 85f;
    private const float WageMultiplier = 2f;
    private static readonly string[] Names =
    {
        "Kevin Mitnick",
        "George Hotz",
        "Joanna Rutkowska",
        "Mudge",
        "Tsutomu Shimomura",
    };
    // ponytail: IDs are session-scoped; add save metadata only if candidate rerolls become exploitable.
    private static readonly HashSet<string> SeenCandidateIds = new(StringComparer.Ordinal);
    private static bool _started;

    internal static void Start()
    {
        if (_started)
            return;
        _started = true;
        GlobalEvents.onNewHour += Scan;
        Scan();
    }

    internal static void Stop()
    {
        if (!_started)
            return;
        _started = false;
        GlobalEvents.onNewHour -= Scan;
        SeenCandidateIds.Clear();
    }

    private static void Scan()
    {
        try
        {
            var employees = EmployeeHelper.GetEmployeeInstances();
            if (employees == null)
                return;

            var usedNames = employees
                .Where(employee => employee?.characterData != null)
                .Select(employee => employee.characterData.name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in employees.Where(employee => employee != null && employee.IsCandidate && Unit8200Rivalry.HasHackerSkill(employee)))
            {
                if (string.IsNullOrEmpty(candidate.id) || !SeenCandidateIds.Add(candidate.id) || UnityEngine.Random.value >= LegendaryChance)
                    continue;

                var name = Names.FirstOrDefault(candidateName => !usedNames.Contains(candidateName));
                if (name == null || candidate.characterData == null)
                    return;

                candidate.characterData.name = name;
                candidate.hourlyWage *= WageMultiplier;
                var skillIncrease = MinimumHackerSkill - candidate.GetSkillValue(Unit8200Ids.HackerSkill);
                if (skillIncrease > 0f)
                    candidate.IncreaseSkill(Unit8200Ids.HackerSkill, skillIncrease);
                usedNames.Add(name);
                Debug.Log($"[Unit-8200] Legendary Hacker candidate available: {name}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Unit-8200] Legendary candidate scan skipped: {exception.GetType().Name}.");
        }
    }
}

internal static class Unit8200Help
{
    internal const string HelpSlug = "businesstypes-unit8200-osintservice";
    private const string CategoryKey = "common_business_types";
    private static bool _languageHooked;
    private static bool _injectScheduled;

    internal static void Start()
    {
        if (!_languageHooked)
        {
            _languageHooked = true;
            LocalizorManager.OnLanguageChanged += Schedule;
        }
        Schedule();
    }

    internal static void Stop()
    {
        if (_languageHooked)
        {
            _languageHooked = false;
            LocalizorManager.OnLanguageChanged -= Schedule;
        }
        RemovePage();
    }

    internal static void EnsureRegistered() => Schedule();

    private static void Schedule()
    {
        if (_injectScheduled)
            return;
        _injectScheduled = true;
        CoroutineUtility.RunAfterOneFrame(InjectPage);
    }

    private static void InjectPage()
    {
        _injectScheduled = false;
        var helpSystem = InstanceBehavior<HelpSystem>.Instance;
        if (helpSystem == null || GetCategories(helpSystem) is not { } categories)
            return;

        var category = categories.FirstOrDefault(entry =>
            string.Equals(entry.CategoryLocalizorKey, CategoryKey, StringComparison.Ordinal));
        if (category == null || category.Pages.Any(page => string.Equals(page.Slug, HelpSlug, StringComparison.Ordinal)))
            return;

        category.Pages.Add(new HelpStructurePageEntry
        {
            Slug = HelpSlug,
            PageLocalizorKeyPrefix = Unit8200Ids.BusinessType,
        });
        Reload(helpSystem);
    }

    private static void RemovePage()
    {
        var helpSystem = InstanceBehavior<HelpSystem>.Instance;
        if (helpSystem == null || GetCategories(helpSystem) is not { } categories)
            return;

        var removed = categories
            .Where(entry => entry.Pages != null)
            .SelectMany(entry => entry.Pages)
            .Where(page => string.Equals(page.Slug, HelpSlug, StringComparison.Ordinal))
            .ToList();
        foreach (var page in removed)
            categories.ForEach(entry => entry.Pages?.Remove(page));
        if (removed.Count > 0)
            Reload(helpSystem);
    }

    private static List<HelpStructureGroupEntry>? GetCategories(HelpSystem helpSystem)
    {
        return typeof(HelpSystem)
            .GetField("_categories", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(helpSystem) as List<HelpStructureGroupEntry>;
    }

    private static void Reload(HelpSystem helpSystem)
    {
        typeof(HelpSystem)
            .GetMethod("LoadCategories", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(helpSystem, null);
    }
}
