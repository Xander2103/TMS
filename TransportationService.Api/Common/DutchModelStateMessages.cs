using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TransportationService.Api.Common;

/// <summary>
/// WH-M8 (warehouse acceptance 2026-10-18): a decimal quantity ("1.5") for an integer field answered
/// with the raw serializer text "The JSON value could not be converted to System.Int32. Path: $.quantity
/// | LineNumber…" plus a confusing "The request field is required.". The 400 keeps its standard
/// ValidationProblemDetails shape; only those conversion messages are rewritten into Dutch and keyed
/// on the field name ("quantity") so the form can show them next to the input.
/// </summary>
public static partial class DutchModelStateMessages
{
    [GeneratedRegex(@"could not be converted to ([\w\.\[\]`,\s]+?)\. Path: \$\.?([^\s|]*)")]
    private static partial Regex ConversionError();

    public static Func<ActionContext, IActionResult> Wrap(Func<ActionContext, IActionResult> inner) => context =>
    {
        var bodyType = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body)?.ParameterType;
        Rewrite(context.ModelState, bodyType);
        return inner(context);
    };

    /// <param name="bodyType">
    /// The request DTO. For a positional record the serializer names the RECORD as the failing
    /// type ("could not be converted to …StockReceiptRequest. Path: $.quantity"), so the field's
    /// real type is looked up on the DTO instead.
    /// </param>
    public static void Rewrite(ModelStateDictionary modelState, Type? bodyType = null)
    {
        var rewrites = new List<(string OldKey, string NewKey, string Message)>();
        foreach (var (key, entry) in modelState)
        {
            foreach (var error in entry.Errors)
            {
                var match = ConversionError().Match(error.ErrorMessage);
                if (!match.Success)
                {
                    continue;
                }

                var field = match.Groups[2].Value;
                var typeName = FieldType(bodyType, field)?.FullName ?? match.Groups[1].Value;
                rewrites.Add((key, string.IsNullOrEmpty(field) ? key : field, MessageFor(typeName)));
            }
        }

        if (rewrites.Count == 0)
        {
            return;
        }

        foreach (var (oldKey, newKey, message) in rewrites)
        {
            modelState.Remove(oldKey);
            modelState.AddModelError(newKey, message);
        }

        // The "request field is required" line is only a consequence of the unreadable body.
        foreach (var key in modelState.Keys.ToList())
        {
            var entry = modelState[key]!;
            if (entry.Errors.Count > 0 && entry.Errors.All(e => e.ErrorMessage.EndsWith("field is required.", StringComparison.Ordinal))
                && !rewrites.Any(r => r.NewKey == key))
            {
                modelState.Remove(key);
            }
        }
    }

    /// <summary>"quantity" or "lines[0].quantity" → the property type on the DTO (case-insensitive), or null.</summary>
    private static Type? FieldType(Type? type, string path)
    {
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (type is null)
            {
                return null;
            }

            var name = raw.Split('[')[0];
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            type = property?.PropertyType;
            if (type is not null && raw.Contains('['))
            {
                type = type.IsArray ? type.GetElementType() : type.GetGenericArguments().FirstOrDefault();
            }
        }

        return type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
    }

    private static string MessageFor(string typeName) => typeName switch
    {
        _ when typeName.Contains("Int32") || typeName.Contains("Int64") || typeName.Contains("Int16") =>
            "Geef een geheel getal in (geen decimalen of tekst).",
        _ when typeName.Contains("Decimal") || typeName.Contains("Double") || typeName.Contains("Single") =>
            "Geef een geldig getal in.",
        _ when typeName.Contains("DateOnly") || typeName.Contains("DateTime") => "Geef een geldige datum in.",
        _ when typeName.Contains("TimeOnly") => "Geef een geldig tijdstip in.",
        _ when typeName.Contains("Guid") => "Ongeldige verwijzing.",
        _ when typeName.Contains("Boolean") => "Ongeldige waarde (ja of nee verwacht).",
        _ => "Ongeldige waarde.",
    };
}
