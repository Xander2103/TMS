namespace TransportationService.Api.Common.Storage;

/// <summary>
/// B1 (production readiness 2026-10-18): the ONE configuration of the durable storage root. Every
/// file the application persists — uploaded documents, POD signatures/photos, Peppol payloads,
/// logos, in-app backups and the Development message sink — lives under this root. It MUST sit
/// outside the release/bin directory: the deploy script rsyncs that directory with --delete.
/// See docs/storage.md ("Bestandsopslag") for the per-environment behaviour and the migration
/// procedure from the legacy <c>&lt;ContentRoot&gt;/App_Data</c> location.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute path of the durable storage root, e.g.
    /// <c>/var/lib/transportationservice/storage</c> (env: <c>Storage__RootPath</c>). Required
    /// outside Development unless <see cref="AllowContentRootStorage"/> is set.</summary>
    public string? RootPath { get; set; }

    /// <summary>Legacy opt-in for installs that have not migrated yet: outside Development, keep
    /// the old <c>&lt;ContentRoot&gt;/App_Data</c> location when <see cref="RootPath"/> is unset.
    /// The app starts with a loud warning; the deploy-wipe risk (B1) stays until the data moves.</summary>
    public bool AllowContentRootStorage { get; set; }
}
