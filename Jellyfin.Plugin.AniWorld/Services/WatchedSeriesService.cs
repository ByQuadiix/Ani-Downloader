using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniWorld.Services;

/// <summary>
/// Persists the list of watched series to SQLite.
/// A watched series is checked periodically for new episodes in its latest season.
/// </summary>
public class WatchedSeriesService : IDisposable
{
    private readonly ILogger<WatchedSeriesService> _logger;
    private readonly SqliteConnection _db;
    private readonly object _dbLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSeriesService"/> class.
    /// Reuses the same database file as <see cref="DownloadHistoryService"/>.
    /// </summary>
    public WatchedSeriesService(ILogger<WatchedSeriesService> logger)
    {
        _logger = logger;

        var pluginDataDir = Plugin.Instance?.DataFolderPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "AniWorldDownloader");

        Directory.CreateDirectory(pluginDataDir);
        var dbPath = Path.Combine(pluginDataDir, "downloads.db");

        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS watched_series (
                id              TEXT PRIMARY KEY,
                series_url      TEXT NOT NULL UNIQUE,
                series_title    TEXT NOT NULL DEFAULT '',
                source          TEXT NOT NULL DEFAULT 'aniworld',
                added_at        TEXT NOT NULL DEFAULT (datetime('now')),
                last_checked_at TEXT,
                known_episode_counts TEXT NOT NULL DEFAULT '{}'
            );

            CREATE INDEX IF NOT EXISTS idx_ws_url ON watched_series(series_url);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a series to the watch list. Returns the new record ID, or null if already watched.
    /// </summary>
    public WatchedSeries? AddWatch(string seriesUrl, string seriesTitle, string source)
    {
        lock (_dbLock)
        {
            // Check for duplicate
            using var checkCmd = _db.CreateCommand();
            checkCmd.CommandText = "SELECT id FROM watched_series WHERE series_url = @url";
            checkCmd.Parameters.AddWithValue("@url", seriesUrl);
            var existingId = checkCmd.ExecuteScalar() as string;
            if (existingId != null)
            {
                return null;
            }

            var id = Guid.NewGuid().ToString("N")[..12];

            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO watched_series (id, series_url, series_title, source, added_at, known_episode_counts)
                VALUES (@id, @url, @title, @source, @added, '{}')";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@url", seriesUrl);
            cmd.Parameters.AddWithValue("@title", seriesTitle);
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@added", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            return new WatchedSeries
            {
                Id = id,
                SeriesUrl = seriesUrl,
                SeriesTitle = seriesTitle,
                Source = source,
                AddedAt = DateTime.UtcNow,
                KnownEpisodeCounts = new Dictionary<int, int>(),
            };
        }
    }

    /// <summary>
    /// Removes a series from the watch list by ID.
    /// </summary>
    public bool RemoveWatch(string id)
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM watched_series WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Returns all watched series.
    /// </summary>
    public List<WatchedSeries> GetAllWatched()
    {
        lock (_dbLock)
        {
            var results = new List<WatchedSeries>();

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, series_url, series_title, source, added_at, last_checked_at, known_episode_counts FROM watched_series ORDER BY added_at DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadRow(reader));
            }

            return results;
        }
    }

    /// <summary>
    /// Checks whether a given series URL is already on the watch list.
    /// </summary>
    public bool IsWatched(string seriesUrl)
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM watched_series WHERE series_url = @url";
            cmd.Parameters.AddWithValue("@url", seriesUrl);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
    }

    /// <summary>
    /// Updates the last-checked timestamp and the known episode counts (season → episode count).
    /// </summary>
    public void UpdateAfterCheck(string id, Dictionary<int, int> knownEpisodeCounts)
    {
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                UPDATE watched_series
                SET last_checked_at = @checked, known_episode_counts = @counts
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@checked", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@counts", JsonSerializer.Serialize(knownEpisodeCounts));
            cmd.ExecuteNonQuery();
        }
    }

    private static WatchedSeries ReadRow(SqliteDataReader reader)
    {
        var countsJson = reader.IsDBNull(6) ? "{}" : reader.GetString(6);
        Dictionary<int, int> counts;
        try
        {
            counts = JsonSerializer.Deserialize<Dictionary<int, int>>(countsJson) ?? new();
        }
        catch
        {
            counts = new();
        }

        return new WatchedSeries
        {
            Id = reader.GetString(0),
            SeriesUrl = reader.GetString(1),
            SeriesTitle = reader.GetString(2),
            Source = reader.GetString(3),
            AddedAt = DateTime.Parse(reader.GetString(4)),
            LastCheckedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            KnownEpisodeCounts = counts,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A series entry on the watch list.
/// </summary>
public class WatchedSeries
{
    /// <summary>Gets or sets the record ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the series page URL.</summary>
    public string SeriesUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the display title.</summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the source site (aniworld / sto).</summary>
    public string Source { get; set; } = "aniworld";

    /// <summary>Gets or sets when this entry was added.</summary>
    public DateTime AddedAt { get; set; }

    /// <summary>Gets or sets when the series was last checked for new episodes.</summary>
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>
    /// Gets or sets the known episode count per season number at the time of the last check.
    /// Key = season number, Value = episode count seen last time.
    /// Used to detect whether new episodes have appeared.
    /// </summary>
    public Dictionary<int, int> KnownEpisodeCounts { get; set; } = new();
}
