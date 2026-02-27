#nullable enable
using System;
using System.Collections.Generic;

namespace GeeksCoreLibrary.Modules.GoogleAuth.Models;

public sealed class GoogleEasyObjectsSettings
{
    public bool AccountIdMandatory { get; set; } = true;
    public bool AllowAccountIdOverride { get; set; }

    public GoogleEasyObjectsDefault Defaults { get; set; } = new();

    /// <summary>
    /// Key: "EntityType:Action" (example: "Customer:Create")
    /// </summary>
    public Dictionary<string, GoogleEasyObjectsEntityTypes> EntityTypeRulesByKey { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public GoogleEasyObjectsEntityTypes GetRule(string entityType, string action)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));

        var key = $"{entityType.Trim()}:{action.Trim()}";
        if (!EntityTypeRulesByKey.TryGetValue(key, out var rule))
        {
            // Return an "empty" rule that still has defaults applied if you want to use it safely.
            return new GoogleEasyObjectsEntityTypes
            {
                EntityType = entityType.Trim(),
                Action = action.Trim(),
                Defaults = Defaults
            };
        }

        // Ensure rule has access to defaults for fallback helpers
        rule.Defaults ??= Defaults;
        return rule;
    }
}

public sealed class GoogleEasyObjectsDefault
{
    // Put global defaults here if you actually use them.
    // Leaving these nullable makes it clear when no default was set.
    public bool? Allowed { get; set; }
    public bool? DoRedirect { get; set; }
    public string? ReturnUrl { get; set; }
    public string? LoginUrl { get; set; }
    public string? CallbackUrl { get; set; }
    public int? CookieExpireTime { get; set; }
}

public sealed class GoogleEasyObjectsEntityTypes
{
    // From section header: [Customer:Create]
    public string EntityType { get; set; } = "";
    public string Action { get; set; } = "";

    // Raw values as they appear in config, with typed helpers.
    public bool? Allowed { get; set; }
    public bool? DoRedirect { get; set; }
    public string? ReturnUrl { get; set; }
    public string? LoginUrl { get; set; }
    public string? CallbackUrl { get; set; }
    public int? CookieExpireTime { get; set; }
    public GoogleEasyObjectsDefault? Defaults { get; set; }

    // Typed fallback helpers (rule value wins; else defaults; else provided final fallback)
    public bool GetAllowed(bool fallback = false) => Allowed ?? Defaults?.Allowed ?? fallback;
    public bool GetDoRedirect(bool fallback = false) => DoRedirect ?? Defaults?.DoRedirect ?? fallback;

    public string GetReturnUrl(string fallback = "/") => ReturnUrl ?? Defaults?.ReturnUrl ?? fallback;
    public string GetLoginUrl(string fallback = "/") => LoginUrl ?? Defaults?.LoginUrl ?? fallback;
    public string? GetCallbackUrl(string? fallback = null) => CallbackUrl ?? Defaults?.CallbackUrl ?? fallback;
    
    public int GetCookieExpireTime(int fallback = 60) => CookieExpireTime ?? Defaults?.CookieExpireTime ?? fallback;
}

public static class GoogleEasyObjectsSettingsParser
{
    /// <summary>
    /// Parses an INI-like config string with sections:
    /// [Settings:General], [Defaults:*], [EntityType:Action]
    /// Lines are Key=Value. Empty values are allowed (Key=).
    /// Quotes are optional; if value is wrapped in "..." it will be unwrapped and \" will be unescaped.
    /// Ignores blank lines and comment lines starting with # or ;.
    /// </summary>
    public static GoogleEasyObjectsSettings Parse(string configurationText, bool strictDuplicateKeys = false)
    {
        if (configurationText == null) throw new ArgumentNullException(nameof(configurationText));

        var settings = new GoogleEasyObjectsSettings();

        GoogleEasyObjectsEntityTypes? currentEntityRule = null;
        var inGeneralSettings = false;
        var inDefaultsSection = false;

        var lines = configurationText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var trimmedLine = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith(";"))
                continue;

            // Section header
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal))
            {
                var currentSectionName = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
                currentEntityRule = null;
                inGeneralSettings = false;
                inDefaultsSection = false;

                if (currentSectionName.Equals("Settings:General", StringComparison.OrdinalIgnoreCase))
                {
                    inGeneralSettings = true;
                    continue;
                }

                if (currentSectionName.Equals("Defaults:*", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultsSection = true;
                    continue;
                }

                // Entity section: Entity:Action
                var parts = currentSectionName.Split(new[] { ':' }, 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    throw new FormatException($"Invalid section header at line {lineIndex + 1}: '{rawLine}'");

                var entityType = parts[0].Trim();
                var action = parts[1].Trim();
                var ruleKey = $"{entityType}:{action}";

                currentEntityRule = new GoogleEasyObjectsEntityTypes
                {
                    EntityType = entityType,
                    Action = action,
                    Defaults = settings.Defaults
                };

                if (strictDuplicateKeys && settings.EntityTypeRulesByKey.ContainsKey(ruleKey))
                    throw new FormatException($"Duplicate entity section '{ruleKey}' at line {lineIndex + 1}.");

                settings.EntityTypeRulesByKey[ruleKey] = currentEntityRule;
                continue;
            }

            // Must be key-value
            var equalsIndex = trimmedLine.IndexOf('=');
            if (equalsIndex < 0)
                throw new FormatException($"Invalid line (expected Key=Value) at line {lineIndex + 1}: '{rawLine}'");

            var key = trimmedLine.Substring(0, equalsIndex).Trim();
            var rawValue = trimmedLine.Substring(equalsIndex + 1).Trim();

            var value = ParsePossiblyQuotedValue(rawValue);

            if (inGeneralSettings)
            {
                ApplyGeneralSetting(settings, key, value, lineIndex + 1);
                continue;
            }

            if (inDefaultsSection)
            {
                ApplyDefaults(settings.Defaults, key, value, lineIndex + 1);
                continue;
            }

            if (currentEntityRule == null)
                throw new FormatException($"Key-value pair found outside of a valid section at line {lineIndex + 1}: '{rawLine}'");

            ApplyEntityRule(currentEntityRule, key, value, lineIndex + 1, strictDuplicateKeys);
        }

        return settings;
    }

    private static void ApplyGeneralSetting(GoogleEasyObjectsSettings settings, string key, string value, int oneBasedLineNumber)
    {
        if (key.Equals("AccountIdMandatory", StringComparison.OrdinalIgnoreCase))
        {
            settings.AccountIdMandatory = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        if (key.Equals("AllowAccountIdOverride", StringComparison.OrdinalIgnoreCase))
        {
            settings.AllowAccountIdOverride = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        // Unknown keys are usually mistakes; decide if you want to ignore instead.
        throw new FormatException($"Unknown [Settings:General] key '{key}' at line {oneBasedLineNumber}.");
    }

    private static void ApplyDefaults(GoogleEasyObjectsDefault defaults, string key, string value, int oneBasedLineNumber)
    {
        if (key.Equals("Allowed", StringComparison.OrdinalIgnoreCase))
        {
            defaults.Allowed = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        if (key.Equals("DoRedirect", StringComparison.OrdinalIgnoreCase))
        {
            defaults.DoRedirect = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        if (key.Equals("ReturnUrl", StringComparison.OrdinalIgnoreCase))
        {
            defaults.ReturnUrl = value; // empty allowed
            return;
        }

        if (key.Equals("LoginUrl", StringComparison.OrdinalIgnoreCase))
        {
            defaults.LoginUrl = value;
            return;
        }

        if (key.Equals("CallbackUrl", StringComparison.OrdinalIgnoreCase))
        {
            defaults.CallbackUrl = value;
            return;
        }
        
        if (key.Equals("CookieExpireTime", StringComparison.OrdinalIgnoreCase))
        {
            defaults.CookieExpireTime = ParseInt(value, oneBasedLineNumber, key);
            return;
        }

        throw new FormatException($"Unknown [Defaults:*] key '{key}' at line {oneBasedLineNumber}.");
    }

    private static void ApplyEntityRule(
        GoogleEasyObjectsEntityTypes rule,
        string key,
        string value,
        int oneBasedLineNumber,
        bool strictDuplicateKeys)
    {
        // Duplicate enforcement
        if (strictDuplicateKeys)
        {
            // This checks duplicates by seeing if the property already has a value
            var alreadySet =
                (key.Equals("Allowed", StringComparison.OrdinalIgnoreCase) && rule.Allowed.HasValue) ||
                (key.Equals("DoRedirect", StringComparison.OrdinalIgnoreCase) && rule.DoRedirect.HasValue) ||
                (key.Equals("ReturnUrl", StringComparison.OrdinalIgnoreCase) && rule.ReturnUrl != null) ||
                (key.Equals("LoginUrl", StringComparison.OrdinalIgnoreCase) && rule.LoginUrl != null) ||
                (key.Equals("CallbackUrl", StringComparison.OrdinalIgnoreCase) && rule.CallbackUrl != null) ||
                (key.Equals("CookieExpireTime", StringComparison.OrdinalIgnoreCase) && rule.CookieExpireTime != null);

            if (alreadySet)
                throw new FormatException($"Duplicate key '{key}' in [{rule.EntityType}:{rule.Action}] at line {oneBasedLineNumber}.");
        }

        if (key.Equals("Allowed", StringComparison.OrdinalIgnoreCase))
        {
            rule.Allowed = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        if (key.Equals("DoRedirect", StringComparison.OrdinalIgnoreCase))
        {
            rule.DoRedirect = ParseBoolean(value, oneBasedLineNumber, key);
            return;
        }

        if (key.Equals("ReturnUrl", StringComparison.OrdinalIgnoreCase))
        {
            rule.ReturnUrl = value; // empty allowed
            return;
        }

        if (key.Equals("LoginUrl", StringComparison.OrdinalIgnoreCase))
        {
            rule.LoginUrl = value; // empty allowed
            return;
        }

        if (key.Equals("CallbackUrl", StringComparison.OrdinalIgnoreCase))
        {
            rule.CallbackUrl = value;
            return;
        }
        
        if (key.Equals("CookieExpireTime", StringComparison.OrdinalIgnoreCase))
        {
            rule.CookieExpireTime = ParseInt(value, oneBasedLineNumber, key);
            return;
        }

        throw new FormatException($"Unknown key '{key}' in [{rule.EntityType}:{rule.Action}] at line {oneBasedLineNumber}.");
    }

    private static bool ParseBoolean(string value, int oneBasedLineNumber, string keyName)
    {
        if (bool.TryParse(value, out var parsedBoolean))
            return parsedBoolean;

        return value switch
        {
            // Accept 0/1 too, because config files love chaos
            "0" => false,
            "1" => true,
            _ => throw new FormatException(
                $"Invalid boolean for '{keyName}' at line {oneBasedLineNumber}: '{value}'. Expected true/false.")
        };
    }
    
    private static int ParseInt(string value, int oneBasedLineNumber, string keyName)
    {
        try
        {
            if (int.TryParse(value, out var parsedInt))
                return parsedInt;

        }
        catch
        {
            throw new FormatException(
                $"Invalid boolean for '{keyName}' at line {oneBasedLineNumber}: '{value}'. Expected a number.");
            
        }
        
        return 60;
    }

    private static string ParsePossiblyQuotedValue(string rawValue)
    {
        if (rawValue.Length < 2 || !rawValue.StartsWith("\"", StringComparison.Ordinal) ||
            !rawValue.EndsWith("\"", StringComparison.Ordinal)) return rawValue;
        
        var inner = rawValue.Substring(1, rawValue.Length - 2);
        // Minimal escape support
        inner = inner.Replace("\\\"", "\"");
        inner = inner.Replace("\\\\", "\\");
        return inner;

    }
}