using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniWorld.Configuration;
using Jellyfin.Plugin.AniWorld.Helpers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniWorld.Services;

/// <summary>
/// Jellyfin scheduled task that checks all watched series for new episodes
/// and automatically queues any that have not been downloaded yet.
/// Runs every 6 hours by default; can also be triggered manually.
/// Only the latest (highest-numbered) season is checked, because earlier
/// seasons of a finished series cannot gain new episodes.
/// </summary>
public class WatchedSeriesCheckTask : IScheduledTask
{
    private const int DelayBetweenSeriesMs = 2000;

    private readonly WatchedSeriesService _watchService;
    private readonly DownloadService _downloadService;
    private readonly AniWorldService _aniWorldService;
    private readonly StoService _stoService;
    private readonly ILogger<WatchedSeriesCheckTask> _logger;

    /// <inheritdoc />
    public string Name => "Check Watched Series for New Episodes";

    /// <inheritdoc />
    public string Key => "AniWorldWatchedSeriesCheck";

    /// <inheritdoc />
    public string Description =>
        "Checks all watched series for new episodes in the latest season and " +
        "automatically queues any missing episodes for download using the global default settings.";

    /// <inheritdoc />
    public string Category => "AniWorld Downloader";

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSeriesCheckTask"/> class.
    /// </summary>
    public WatchedSeriesCheckTask(
        WatchedSeriesService watchService,
        DownloadService downloadService,
        AniWorldService aniWorldService,
        StoService stoService,
        ILogger<WatchedSeriesCheckTask> logger)
    {
        _watchService = watchService;
        _downloadService = downloadService;
        _aniWorldService = aniWorldService;
        _stoService = stoService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            return;
        }

        if (config.MaintenanceMode)
        {
            _logger.LogInformation("WatchedSeriesCheck skipped: maintenance mode is active.");
            return;
        }

        var watched = _watchService.GetAllWatched();
        if (watched.Count == 0)
        {
            progress.Report(100);
            return;
        }

        _logger.LogInformation("WatchedSeriesCheck: checking {Count} watched series.", watched.Count);

        var totalQueued = 0;
        var totalSkipped = 0;

        for (int i = 0; i < watched.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var entry = watched[i];
            progress.Report((double)i / watched.Count * 100);

            try
            {
                var (queued, skipped) = await CheckSeriesAsync(entry, config, cancellationToken)
                    .ConfigureAwait(false);
                totalQueued += queued;
                totalSkipped += skipped;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WatchedSeriesCheck: error checking '{Title}' ({Url})", entry.SeriesTitle, entry.SeriesUrl);
            }

            // Polite delay between series to avoid hammering the source sites
            if (i < watched.Count - 1)
            {
                await Task.Delay(DelayBetweenSeriesMs, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "WatchedSeriesCheck finished. Queued {Queued} new episodes, skipped {Skipped} already downloaded.",
            totalQueued, totalSkipped);

        progress.Report(100);
    }

    private async Task<(int Queued, int Skipped)> CheckSeriesAsync(
        WatchedSeries entry,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var source = entry.Source;
        StreamingSiteService service = string.Equals(source, "sto", StringComparison.OrdinalIgnoreCase)
            ? _stoService
            : _aniWorldService;

        // Fetch series info to get the season list
        var seriesInfo = await service.GetSeriesInfoAsync(entry.SeriesUrl, cancellationToken)
            .ConfigureAwait(false);

        if (seriesInfo.Seasons == null || seriesInfo.Seasons.Count == 0)
        {
            _logger.LogDebug("WatchedSeriesCheck: no seasons found for '{Title}'.", entry.SeriesTitle);
            return (0, 0);
        }

        // Find the latest real season (ignore season 0 / specials)
        var latestSeason = seriesInfo.Seasons
            .Where(s => s.Number > 0)
            .OrderByDescending(s => s.Number)
            .FirstOrDefault();

        if (latestSeason == null)
        {
            return (0, 0);
        }

        // Resolve download settings (language, provider, path) from global defaults
        var language = config.GetPreferredLanguage(source);
        var provider = config.GetPreferredProvider(source);
        var basePath = config.GetDownloadPath(source, language);

        if (string.IsNullOrEmpty(basePath))
        {
            _logger.LogWarning(
                "WatchedSeriesCheck: no download path configured for source '{Source}', skipping '{Title}'.",
                source, entry.SeriesTitle);
            return (0, 0);
        }

        var seriesTitle = PathHelper.SanitizeFileName(seriesInfo.Title ?? entry.SeriesTitle);

        // Fetch episodes for the latest season
        var episodes = await service.GetEpisodesAsync(latestSeason.Url, cancellationToken)
            .ConfigureAwait(false);

        var queued = 0;
        var skipped = 0;

        foreach (var ep in episodes)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var (season, episode) = PathHelper.ParseSeasonEpisode(ep.Url, ep.Number > 0 ? ep.Number : null);
            var outputPath = PathHelper.BuildOutputPath(basePath, seriesTitle, ep.Url, ep.Number > 0 ? ep.Number : null);

            // Skip if already in history
            if (_downloadService.IsAlreadyDownloaded(seriesTitle, season, episode, language))
            {
                skipped++;
                continue;
            }

            var taskId = await _downloadService.StartDownloadAsync(
                ep.Url,
                language,
                provider,
                outputPath,
                seriesTitle,
                source,
                cancellationToken).ConfigureAwait(false);

            if (taskId != null)
            {
                queued++;
                _logger.LogInformation(
                    "WatchedSeriesCheck: queued S{Season:D2}E{Episode:D2} of '{Title}'.",
                    season, episode, seriesTitle);
            }
            else
            {
                skipped++;
            }
        }

        // Update the last-checked timestamp and the episode count for this season
        var updatedCounts = new Dictionary<int, int>(entry.KnownEpisodeCounts)
        {
            [latestSeason.Number] = episodes.Count,
        };
        _watchService.UpdateAfterCheck(entry.Id, updatedCounts);

        _logger.LogDebug(
            "WatchedSeriesCheck: '{Title}' S{Season} — {Ep} episodes found, {Q} queued, {S} skipped.",
            entry.SeriesTitle, latestSeason.Number, episodes.Count, queued, skipped);

        return (queued, skipped);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run automatically every 6 hours
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(6).Ticks,
        };
    }
}
