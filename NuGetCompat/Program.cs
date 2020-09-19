﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGetCompat
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        static async Task MainAsync(CancellationToken cancellationToken)
        {
            var source = "https://api.nuget.org/v3/index.json";
            var repository = Repository.Factory.GetCoreV3(source);
            var search = await repository.GetResourceAsync<PackageSearchResource>();
            var download = await repository.GetResourceAsync<DownloadResource>();
            var logger = NullLogger.Instance;
            var settings = Settings.LoadDefaultSettings(Directory.GetCurrentDirectory());
            var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);

            var take = 20;
            Console.WriteLine($"Searching for top {take} packages...");
            var results = await search.SearchAsync(
                searchTerm: string.Empty,
                new SearchFilter(includePrerelease: true),
                skip: 0,
                take: take,
                log: logger,
                cancellationToken: cancellationToken);

            using var sourceCacheContext = new SourceCacheContext();
            var packageDownloadContext = new PackageDownloadContext(sourceCacheContext);

            foreach (var result in results)
            {
                Console.Write($"Downloading {result.Identity.Id} {result.Identity.Version.ToNormalizedString()}...");
                using var downloadResult = await download.GetDownloadResourceResultAsync(
                    result.Identity,
                    packageDownloadContext,
                    globalPackagesFolder,
                    logger,
                    cancellationToken);
                Console.WriteLine(" done.");

                var files = downloadResult.PackageReader.GetFiles().ToList();

                var fromNuspecReader = SupportedFrameworksProvider.SuggestedByNuspecReader(
                    files,
                    $"{downloadResult.PackageReader.GetIdentity().Id.ToLowerInvariant()}.nuspec",
                    () => downloadResult.PackageReader.GetNuspec());
                fromNuspecReader = ReduceFrameworks(fromNuspecReader);

                var suggestedByRestore = SupportedFrameworksProvider.SuggestedByCompatibilityChecker(files);
                suggestedByRestore = ReduceFrameworks(suggestedByRestore);

                var supportedByRestore = SupportedFrameworksProvider.SupportedByCompatiblityChecker(files, downloadResult.PackageReader.NuspecReader);
                supportedByRestore = ReduceFrameworks(supportedByRestore);

                var supportedByRestore2 = await SupportedFrameworksProvider.SupportedByCompatiblityChecker2Async(
                    files,
                    downloadResult.PackageReader.NuspecReader,
                    logger);
                supportedByRestore2 = ReduceFrameworks(supportedByRestore2);

                var supportedByDuplicatingLogic = SupportedFrameworksProvider.SupportedByDuplicatingLogic(
                    files,
                    downloadResult.PackageReader.NuspecReader).ToHashSet();
                supportedByDuplicatingLogic = ReduceFrameworks(supportedByDuplicatingLogic);

                var sets = new Dictionary<string, HashSet<NuGetFramework>>
                {
                    { nameof(fromNuspecReader), fromNuspecReader },
                    { nameof(suggestedByRestore), suggestedByRestore },
                    { nameof(supportedByRestore), supportedByRestore },
                    { nameof(supportedByRestore2), supportedByRestore2 },
                    { nameof(supportedByDuplicatingLogic), supportedByDuplicatingLogic },
                };

                var haveAny = sets.Values.Any(x => x.Contains(NuGetFramework.AnyFramework));
                var haveDifferent = sets.Values.Any(x => !sets.Values.All(y => x.SetEquals(y)));

                if (haveAny || haveDifferent)
                {
                    foreach (var pair in sets)
                    {
                        DumpFrameworks(pair.Key, pair.Value);
                    }

                    Console.WriteLine();
                }
            }
        }

        private static void DumpFrameworks(string name, HashSet<NuGetFramework> frameworks)
        {
            Console.WriteLine($"From {name}:");
            foreach (var framework in frameworks)
            {
                Console.WriteLine($"  {framework.GetShortFolderName()}");
            }
        }

        private static HashSet<NuGetFramework> ReduceFrameworks(HashSet<NuGetFramework> frameworks)
        {
            var specificGroups = frameworks.ToLookup(x => x.IsSpecificFramework);
            var reducer = new FrameworkReducer();
            var reducedSpecific = reducer.ReduceDownwards(specificGroups[true]);

            var set = reducedSpecific
                .Concat(specificGroups[false].Distinct())
                .OrderBy(x => x, new NuGetFrameworkSorter())
                .ToSet();

            return set;
        }
    }
}
