using System;
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

namespace NuGetCompat.CommandLine
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

            var take = 1000;
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

            var i = 0;
            foreach (var result in results)
            {
                i++;
                var downloadResultTask = download.GetDownloadResourceResultAsync(
                    result.Identity,
                    packageDownloadContext,
                    globalPackagesFolder,
                    logger,
                    cancellationToken);
                using var cancellationTokenSource = new CancellationTokenSource();
                var delayTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationTokenSource.Token);

                var firstTask = await Task.WhenAny(downloadResultTask, delayTask);
                if (firstTask == delayTask)
                {
                    Console.Write($"Downloading #{i}: {result.Identity.Id} {result.Identity.Version.ToNormalizedString()}...");
                    await downloadResultTask;
                    Console.WriteLine(" done.");
                }
                else
                {
                    cancellationTokenSource.Cancel();
                }

                var downloadResult = await downloadResultTask;

                var files = downloadResult.PackageReader.GetFiles().ToList();

                var supportedByNuspecReader = SupportedFrameworksProvider.SupportedByNuspecReader(
                    files,
                    () => downloadResult.PackageReader.GetNuspec()).ToHashSet();

                var suggestedByNU1202 = SupportedFrameworksProvider.SuggestedByNU1202(files).ToHashSet();

                /*
                var supportedByPatternSets = SupportedFrameworksProvider.SupportedByPatternSets(files);
                supportedByPatternSets = ReduceFrameworks(supportedByPatternSets);
                */

                /*
                var supportedByFrameworkEnumeration = await SupportedFrameworksProvider.SupportedByFrameworkEnumerationAsync(
                    files,
                    downloadResult.PackageReader.NuspecReader,
                    logger);
                */

                var supportedByDuplicatedLogic = SupportedFrameworksProvider.SupportedByDuplicatedLogic(
                    files,
                    downloadResult.PackageReader.NuspecReader).ToHashSet();

                var sets = new Dictionary<string, HashSet<NuGetFramework>>
                {
                    { nameof(supportedByNuspecReader), supportedByNuspecReader.ToHashSet() },
                    { nameof(suggestedByNU1202), suggestedByNU1202.ToHashSet() },
                    // { nameof(supportedByPatternSets), supportedByPatternSets },
                    // { nameof(supportedByFrameworkEnumeration), supportedByFrameworkEnumeration },
                    { nameof(supportedByDuplicatedLogic), supportedByDuplicatedLogic },
                };

                var haveAny = sets.Values.Any(x => x.Contains(NuGetFramework.AnyFramework));
                var haveDifferent = sets.Values.Any(x => !sets.Values.All(y => x.SetEquals(y)));

                if (haveDifferent)
                {
                    Console.WriteLine($"{result.Identity.Id} {result.Identity.Version.ToNormalizedString()}:");
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
    }
}
