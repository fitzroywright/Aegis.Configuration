using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed record RetiredConfigurationContractInfo(
    string ApplicationId,
    string DisplayName,
    string Version,
    string? InstanceId,
    DateTimeOffset? LastRegisteredAtUtc,
    DateTimeOffset RetiredAtUtc,
    string Reason,
    string RecordId = "",
    string Actor = "legacy / unknown");

public sealed class DirectoryContractStore(string directoryPath) : IConfigurationContractStore, IConfigurationContractRegistrationStore
{
    private const string RetiredCafeteriaApplicationId = "Cafeteria.Services";
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly ContractValidator validator = new();
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public async ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath)) return [];
        List<ApplicationConfigurationContract> contracts = [];
        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            ApplicationConfigurationContract? contract;
            try { await using FileStream stream = File.OpenRead(path); contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(stream, jsonOptions, cancellationToken); }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                contract = FailedContract(path, exception is JsonException ? "Stored contract contains invalid JSON." : "Stored contract cannot be read.");
            }
            contract ??= FailedContract(path, "Stored contract is empty.");
            if (string.IsNullOrWhiteSpace(contract.ApplicationId) || contract.Requirements is null || contract.Requirements.Any(x => x is null))
                contract = FailedContract(path, "Stored contract identity or requirements are invalid.");

            // Aegis.Cafeteria.Services owns the Cafeteria contract. Older releases briefly registered a smaller
            // contract as Cafeteria.Services. Keep accepting that old startup call during rolling upgrades, but
            // never expose it as a second application or allow it to become configuration authority.
            if (IsRetiredPublisher(contract.ApplicationId)) continue;

            // Existing stored contracts remain readable across schema migrations.
            // Contract validation is enforced on RegisterAsync; reads must not take the operator UI down
            // merely because an older application has not republished the latest contract shape yet.
            contracts.Add(contract);
        }
        return contracts;
    }

    public async ValueTask<ConfigurationContractRegistrationResult> RegisterAsync(ApplicationConfigurationContract contract, CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try { return await RegisterAsyncCore(contract, cancellationToken); }
        finally { mutationGate.Release(); }
    }

    private async ValueTask<ConfigurationContractRegistrationResult> RegisterAsyncCore(ApplicationConfigurationContract contract, CancellationToken cancellationToken = default)
    {
        ContractValidationResult validation = validator.Validate(contract);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));

        if (IsRetiredPublisher(contract.ApplicationId))
        {
            byte[] retiredSemanticContent = SemanticBytes(contract);
            string retiredHash = Convert.ToHexString(SHA256.HashData(retiredSemanticContent));
            return new(contract.ApplicationId, contract.Version, false, retiredHash, DateTimeOffset.UtcNow);
        }

        string safeApplicationId = EnsureSafeApplicationId(contract.ApplicationId);
        Directory.CreateDirectory(directoryPath);
        string path = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");

        // The server, not the application, owns registration time. Every authenticated successful registration is a heartbeat.
        // RuntimeEnvironment is observational telemetry, not contract semantics, so changing free disk/memory/IP data does not
        // falsely report that the application's configuration contract changed.
        ApplicationConfigurationContract stamped = contract with { LastRegisteredAtUtc = DateTimeOffset.UtcNow };
        byte[] semanticContent = SemanticBytes(contract);
        string hash = Convert.ToHexString(SHA256.HashData(semanticContent));
        bool changed = true;
        bool previouslyActive = File.Exists(path);
        if (previouslyActive)
        {
            byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken);
            try
            {
                ApplicationConfigurationContract? previous = JsonSerializer.Deserialize<ApplicationConfigurationContract>(existing, jsonOptions);
                if (previous is not null)
                {
                    byte[] previousSemantic = SemanticBytes(previous);
                    changed = !CryptographicOperations.FixedTimeEquals(SHA256.HashData(previousSemantic), SHA256.HashData(semanticContent));
                }
            }
            catch (JsonException) { changed = true; }
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(stamped, jsonOptions);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken); File.Move(temporaryPath, path, true); }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        if (!previouslyActive && (await ListRetiredAsync(cancellationToken: cancellationToken)).Any(x =>
            string.Equals(x.ApplicationId, contract.ApplicationId, StringComparison.OrdinalIgnoreCase)))
            await AuditAsync("Republished", contract.ApplicationId, "authenticated publisher", "Current publication is active; earlier retired revisions remain historical.", Path.GetFileName(path), cancellationToken);
        return new(contract.ApplicationId, contract.Version, changed, hash, stamped.LastRegisteredAtUtc.Value);
    }

    public async ValueTask<bool> RetireAsync(string applicationId, string reason, CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try { return await RetireAsyncCore(applicationId, reason, cancellationToken); }
        finally { mutationGate.Release(); }
    }

    private async ValueTask<bool> RetireAsyncCore(string applicationId, string reason, CancellationToken cancellationToken = default)
    {
        string safeApplicationId = EnsureSafeApplicationId(applicationId);
        string activePath = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");
        if (!File.Exists(activePath)) return false;

        string retiredDirectory = Path.Combine(directoryPath, "retired");
        Directory.CreateDirectory(retiredDirectory);
        string retiredPath = Path.Combine(
            retiredDirectory,
            $"{safeApplicationId}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{SanitizeReason(reason)}.configuration-contract.json");

        await Task.Yield();
        var audit = await AuditAsync("RetireRequested", applicationId, "lifecycle authority", reason, Path.GetFileName(retiredPath), cancellationToken);
        File.Move(activePath, retiredPath, false);
        await CompleteAuditAsync(audit, "Retired", cancellationToken);
        return true;
    }

    public async ValueTask<bool> PurgeAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try { return await PurgeAsyncCore(applicationId, cancellationToken); }
        finally { mutationGate.Release(); }
    }

    private async ValueTask<bool> PurgeAsyncCore(string applicationId, CancellationToken cancellationToken = default)
    {
        string safeApplicationId = EnsureSafeApplicationId(applicationId);
        bool removed = false;
        await AuditAsync("PurgeRequested", applicationId, "Operations lifecycle authority", "Contract purge through authenticated lifecycle API; registration audit records the administrator.", string.Join(", ", (await PreviewAsync(applicationId, cancellationToken)).Select(x => x.RecordId)), cancellationToken);
        string activePath = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");
        if (File.Exists(activePath))
        {
            File.Delete(activePath);
            removed = true;
        }

        string retiredDirectory = Path.Combine(directoryPath, "retired");
        if (Directory.Exists(retiredDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(
                retiredDirectory,
                $"{safeApplicationId}.*.configuration-contract.json",
                SearchOption.TopDirectoryOnly).Where(path => IsOwnedRevision(path, safeApplicationId)))
            {
                File.Delete(path);
                removed = true;
            }
        }

        await Task.Yield();
        return removed;
    }

    public ValueTask<int> CountRetiredAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string retiredDirectory = Path.Combine(directoryPath, "retired");
        if (!Directory.Exists(retiredDirectory)) return ValueTask.FromResult(0);

        string pattern = string.IsNullOrWhiteSpace(reason)
            ? "*.configuration-contract.json"
            : $"*.{SanitizeReason(reason)}.configuration-contract.json";
        int count = Directory.EnumerateFiles(retiredDirectory, pattern, SearchOption.TopDirectoryOnly).Count();
        return ValueTask.FromResult(count);
    }

    public async ValueTask<IReadOnlyList<RetiredConfigurationContractInfo>> ListRetiredAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        string retiredDirectory = Path.Combine(directoryPath, "retired");
        if (!Directory.Exists(retiredDirectory)) return [];

        string pattern = string.IsNullOrWhiteSpace(reason)
            ? "*.configuration-contract.json"
            : $"*.{SanitizeReason(reason)}.configuration-contract.json";

        List<RetiredConfigurationContractInfo> items = [];
        var retirementEvents = (await HistoryAsync(cancellationToken)).Where(x => x.Action == "Retired")
            .GroupBy(x => x.RecordId).ToDictionary(x => x.Key, x => x.OrderByDescending(a => a.AtUtc).First());
        foreach (string path in Directory.EnumerateFiles(retiredDirectory, pattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using FileStream stream = File.OpenRead(path);
            ApplicationConfigurationContract? contract;
            try
            {
                contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(
                    stream,
                    jsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                continue;
            }

            if (contract is null) continue;

            string fileName = Path.GetFileName(path);
            string retiredReason = string.IsNullOrWhiteSpace(reason)
                ? InferRetirementReason(fileName)
                : SanitizeReason(reason);

            retirementEvents.TryGetValue(fileName, out var retirementEvent);
            items.Add(new RetiredConfigurationContractInfo(
                contract.ApplicationId,
                contract.DisplayName,
                contract.Version,
                contract.InstanceId,
                contract.LastRegisteredAtUtc,
                retirementEvent?.AtUtc ?? File.GetLastWriteTimeUtc(path),
                retirementEvent?.Reason ?? retiredReason,
                fileName,
                retirementEvent?.Actor ?? "legacy / unknown"));
        }

        return items
            .OrderByDescending(x => x.RetiredAtUtc)
            .ToArray();
    }

    private static string InferRetirementReason(string fileName)
    {
        string stem = fileName.Replace(".configuration-contract.json", string.Empty, StringComparison.OrdinalIgnoreCase);
        string[] parts = stem.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[^1] : "retired";
    }

    public async ValueTask<IReadOnlyList<string>> ReconcileAsync(
        IReadOnlyCollection<string> activeApplicationIds,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> active = activeApplicationIds
            .Where(IsSafeApplicationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> retired = [];
        if (!Directory.Exists(directoryPath)) return retired;

        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.configuration-contract.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationConfigurationContract? contract;
            await using (FileStream stream = File.OpenRead(path))
            {
                contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(
                    stream,
                    jsonOptions,
                    cancellationToken);
            }

            if (contract is null || IsRetiredPublisher(contract.ApplicationId) || active.Contains(contract.ApplicationId))
                continue;

            if (await RetireAsync(contract.ApplicationId, "orphaned", cancellationToken))
                retired.Add(contract.ApplicationId);
        }

        return retired;
    }


    private static ApplicationConfigurationContract FailedContract(string path, string message)
    {
        string id = Path.GetFileName(path).Replace(".configuration-contract.json", "", StringComparison.OrdinalIgnoreCase);
        return new(id, id, "Unknown", [new("contract-load", "Contract loading", ConfigurationRequirementKind.Configuration,
            true, message, ConfigurationState: "Unknown")]);
    }

    public sealed record ContractAudit(DateTimeOffset AtUtc, string Action, string ApplicationId, string Actor, string Reason, string RecordId, string? Version = null, DateTimeOffset? LastPublishedAtUtc = null);
    public sealed record ContractRecord(string RecordId, string ApplicationId, string Version, bool Retired, string Fingerprint);

    public async ValueTask<IReadOnlyList<ContractRecord>> PreviewAsync(string applicationId, CancellationToken ct = default)
    {
        string id = EnsureSafeApplicationId(applicationId);
        List<ContractRecord> result = [];
        string active = Path.Combine(directoryPath, id + ".configuration-contract.json");
        var files = new List<(string Path, bool Retired)>();
        if (File.Exists(active)) files.Add((active, false));
        string retired = Path.Combine(directoryPath, "retired");
        if (Directory.Exists(retired)) files.AddRange(Directory.EnumerateFiles(retired, id + ".*.configuration-contract.json").Where(path => IsOwnedRevision(path, id)).Select(x => (x, true)));
        foreach (var file in files)
        {
            byte[] bytes = await File.ReadAllBytesAsync(file.Path, ct);
            string version = "Unreadable";
            try { version = JsonSerializer.Deserialize<ApplicationConfigurationContract>(bytes, jsonOptions)?.Version ?? "Unknown"; }
            catch (JsonException) { }
            result.Add(new(Path.GetFileName(file.Path), id, version, file.Retired, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return result;
    }

    public async ValueTask<bool> ManageAsync(string applicationId, string recordId, string fingerprint, string action, string actor, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new InvalidDataException("Administrator and reason are required.");
        await mutationGate.WaitAsync(ct);
        try
        {
            ContractRecord? record = (await PreviewAsync(applicationId, ct)).SingleOrDefault(x => x.RecordId == recordId && x.Fingerprint == fingerprint);
            if (record is null) return false; // stale preview or publication: never operate on a different revision
            string path = Path.Combine(directoryPath, record.Retired ? "retired" : "", recordId);
            if (action == "retire" && !record.Retired)
            {
                string target = $"{applicationId}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.administrator.configuration-contract.json";
                Directory.CreateDirectory(Path.Combine(directoryPath, "retired"));
                var audit = await AuditAsync("RetireRequested", applicationId, actor, reason, target, ct);
                File.Move(path, Path.Combine(directoryPath, "retired", target));
                await CompleteAuditAsync(audit, "Retired", ct);
            }
            else if (action == "restore" && record.Retired)
            {
                string target = Path.Combine(directoryPath, applicationId + ".configuration-contract.json");
                if (File.Exists(target)) throw new InvalidDataException("A current publication already exists. It must not be overwritten by historical data.");
                // Validate historical data before restoring it to evaluation.
                var contract = JsonSerializer.Deserialize<ApplicationConfigurationContract>(await File.ReadAllBytesAsync(path, ct), jsonOptions);
                if (contract is null || !string.Equals(contract.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Historical contract identity is invalid.");
                var audit = await AuditAsync("RestoreRequested", applicationId, actor, reason, recordId, ct);
                File.Move(path, target);
                await CompleteAuditAsync(audit, "Restored", ct);
            }
            else if (action == "delete")
            {
                var audit = await AuditAsync("DeleteRequested", applicationId, actor, reason, recordId, ct);
                File.Delete(path); // contract only: no settings, registration, secret, runbook or diagnostic evidence
                await CompleteAuditAsync(audit, "Deleted", ct);
            }
            else throw new InvalidDataException("Action does not apply to this contract revision.");
            return true;
        }
        finally { mutationGate.Release(); }
    }

    public async ValueTask<IReadOnlyList<ContractAudit>> HistoryAsync(CancellationToken ct = default)
    {
        string root = Path.Combine(directoryPath, "lifecycle-audit");
        if (!Directory.Exists(root)) return [];
        List<ContractAudit> result = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.json"))
        {
            var item = JsonSerializer.Deserialize<ContractAudit>(await File.ReadAllBytesAsync(file, ct), jsonOptions);
            if (item is not null) result.Add(item);
        }
        return result.OrderByDescending(x => x.AtUtc).ToArray();
    }

    private async ValueTask<ContractAudit> AuditAsync(string action, string id, string actor, string reason, string recordId, CancellationToken ct)
    {
        string root = Path.Combine(directoryPath, "lifecycle-audit");
        Directory.CreateDirectory(root);
        ApplicationConfigurationContract? contract = null;
        string[] candidates = [Path.Combine(directoryPath, recordId), Path.Combine(directoryPath, "retired", recordId), Path.Combine(directoryPath, id + ".configuration-contract.json")];
        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try { contract = JsonSerializer.Deserialize<ApplicationConfigurationContract>(await File.ReadAllBytesAsync(candidate, ct), jsonOptions); }
            catch (JsonException) { }
            if (contract is not null) break;
        }
        var item = new ContractAudit(DateTimeOffset.UtcNow, action, id, actor, reason, recordId, contract?.Version, contract?.LastRegisteredAtUtc);
        await WriteAuditAsync(item, ct);
        return item;
    }

    private ValueTask CompleteAuditAsync(ContractAudit requested, string action, CancellationToken ct)
        => WriteAuditAsync(requested with { Action = action, AtUtc = DateTimeOffset.UtcNow }, ct);

    private async ValueTask WriteAuditAsync(ContractAudit item, CancellationToken ct)
    {
        string root = Path.Combine(directoryPath, "lifecycle-audit");
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        string temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(item, jsonOptions), ct);
        File.Move(temp, target);
    }

    private static bool IsOwnedRevision(string path, string applicationId)
    {
        string name = Path.GetFileName(path);
        string prefix = applicationId + ".";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string timestamp = name[prefix.Length..].Split('.')[0];
        return timestamp.Length == 17 && timestamp.All(char.IsDigit);
    }

    public async ValueTask<bool> RetireLegacyCafeteriaAliasAsync(CancellationToken ct = default)
    {
        // Exact legacy alias only, and only when its documented replacement is already published.
        if (!File.Exists(Path.Combine(directoryPath, "Cafeteria.Services.configuration-contract.json")) ||
            !File.Exists(Path.Combine(directoryPath, "Aegis.Cafeteria.Services.configuration-contract.json"))) return false;
        var canonical = (await LoadAllAsync(ct)).FirstOrDefault(x => x.ApplicationId == "Aegis.Cafeteria.Services");
        if (canonical is null || canonical.Requirements.Any(x => x.Id == "contract-load")) return false;
        return await RetireAsync("Cafeteria.Services", "superseded-by-Aegis-Cafeteria-Services", ct);
    }

    private static string SanitizeReason(string reason)
    {
        string value = string.IsNullOrWhiteSpace(reason) ? "retired" : reason.Trim().ToLowerInvariant();
        return new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    }

    private static byte[] SemanticBytes(ApplicationConfigurationContract contract)
        => JsonSerializer.SerializeToUtf8Bytes(contract with { LastRegisteredAtUtc = null, RuntimeEnvironment = null }, jsonOptions);

    public static bool IsSafeApplicationId(string applicationId) => !string.IsNullOrWhiteSpace(applicationId) && applicationId.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool IsRetiredPublisher(string applicationId) => string.Equals(applicationId, RetiredCafeteriaApplicationId, StringComparison.OrdinalIgnoreCase);
    private static string EnsureSafeApplicationId(string applicationId) { if (!IsSafeApplicationId(applicationId)) throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'."); return applicationId; }
}

